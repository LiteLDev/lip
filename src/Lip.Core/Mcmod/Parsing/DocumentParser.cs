using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Semver;

namespace Lip.Core.Mcmod;

/// Parses and validates a root manifest.json (spec §1, §4, §10.1).
///
/// A document with "format": "bf45b1" is a document of this format: a meta document when it has
/// "targets", a package document otherwise. A document without "format" is a LeviLamina manifest,
/// read as a mod without install edges (spec §10.1). Any other "format" is invalid.
public partial class DocumentParser(Func<string, IVersionConstraint>? parseRange = null)
    : IDocumentParser {
  private static readonly UTF8Encoding _strictUtf8 = new(
      encoderShouldEmitUTF8Identifier: false,
      throwOnInvalidBytes: true);

  private static readonly JsonDocumentOptions _jsonOptions = new() {
    AllowDuplicateProperties = false,
    AllowTrailingCommas = false,
    CommentHandling = JsonCommentHandling.Disallow,
  };

  private readonly Func<string, IVersionConstraint> _parseRange = parseRange ?? VersionRange.Parse;

  public ManifestDocument Parse(byte[] utf8Json) {
    using JsonDocument json = ParseJson(utf8Json);
    JsonElement root = json.RootElement;
    if (root.ValueKind != JsonValueKind.Object) {
      throw Invalid("$", "the top level must be an object");
    }

    if (!root.TryGetProperty(ManifestFormat.FormatKey, out JsonElement format)) {
      return ParseLegacy(root);
    }

    if (format.ValueKind != JsonValueKind.String || format.GetString() != ManifestFormat.Format) {
      throw Invalid($"$.{ManifestFormat.FormatKey}", $"must be \"{ManifestFormat.Format}\"");
    }

    return root.TryGetProperty("targets", out _) ? ParseMeta(root) : ParsePackage(root);
  }

  /// Parses strict JSON (spec §1 "Document"): UTF-8 (a byte order mark is ignored), no comments,
  /// no trailing commas and no duplicate member names. The caller disposes the result.
  public static JsonDocument ParseJson(byte[] utf8Json) {
    ReadOnlyMemory<byte> bytes = utf8Json;

    // RFC 8259 §8.1 allows parsers to ignore a byte order mark.
    if (bytes.Span.StartsWith(Encoding.UTF8.Preamble)) {
      bytes = bytes[Encoding.UTF8.Preamble.Length..];
    }

    try {
      _ = _strictUtf8.GetCharCount(bytes.Span);
    }
    catch (DecoderFallbackException ex) {
      throw new ManifestFormatException("Invalid manifest.json: not valid UTF-8.", ex);
    }

    try {
      return JsonDocument.Parse(bytes, _jsonOptions);
    }
    catch (JsonException ex) {
      throw new ManifestFormatException(
          $"Invalid manifest.json: not strict JSON ({ex.Message})", ex);
    }
  }

  /// Whether a string is an id (spec §1): ^[A-Za-z0-9][A-Za-z0-9._-]*$.
  public static bool IsId(string value) => IdRegex().IsMatch(value);

  #region LeviLamina manifests (spec §10.1)

  private PackageDocument ParseLegacy(JsonElement root) => new() {
    Id = ReadRequired(root, "name", "$", ReadId),
    Version = ReadRequired(root, "version", "$", ReadSemVersion),
    IsMod = true,
    Platform = root.TryGetProperty("platform", out JsonElement platform)
        && platform.ValueKind == JsonValueKind.String
        && McmodPlatformExtensions.TryParse(platform.GetString()!, out McmodPlatform p)
        ? p
        : null,
    Conflicts = ReadOptional(root, "conflicts", "$", ArrayOf(ReadLegacyConflict)) ?? [],
  };

  private Dep ReadLegacyConflict(JsonElement element, string path) {
    RequireObject(element, path);

    return new Dep {
      Id = ReadRequired(element, "name", path, ReadString),
      Version = ReadOptional(element, "version", path, ReadRange),
    };
  }

  #endregion

  #region Meta documents (spec §4.2)

  private static MetaDocument ParseMeta(JsonElement root) {
    string? id = ReadOptional(root, "id", "$", ReadId);
    SemVersion? version = ReadOptional(root, "version", "$", ReadSemVersion);

    List<MetaTarget> targets = ReadArray(
        root.GetProperty("targets"), "$.targets", nonEmpty: true,
        (e, p) => ReadTarget(e, p, id, version));

    CheckTargetsAreUnambiguous(targets);

    return new MetaDocument { Id = id, Version = version, Targets = targets };
  }

  private static MetaTarget ReadTarget(
      JsonElement element, string path, string? id, SemVersion? version) {
    RequireObject(element, path);

    string variant = ReadOptional(element, "variant", path, ReadString) ?? "";
    if (variant != "" && !Ref.IsValidVariant(variant)) {
      throw Invalid($"{path}.variant", $"'{variant}' is not a valid variant");
    }

    // A null override removes the template's value (spec §4.2 "Templates").
    if (element.TryGetProperty("id", out JsonElement idOverride)) {
      id = idOverride.ValueKind == JsonValueKind.Null ? null : ReadId(idOverride, $"{path}.id");
    }

    if (element.TryGetProperty("version", out JsonElement versionOverride)) {
      version = versionOverride.ValueKind == JsonValueKind.Null
          ? null
          : ReadSemVersion(versionOverride, $"{path}.version");
    }

    return new MetaTarget {
      Variant = variant,
      Platforms = ReadOptional(element, "platform", path, ReadTargetPlatforms),
      Runtimes = ReadOptional(element, "runtime", path, ReadTargetRuntimes),
      Source = ReadRequired(element, "source", path, (e, p) => ReadOneOrMany(e, p, ReadRefTemplate)),
      TemplateId = id,
      TemplateVersion = version,
    };
  }

  private static List<McmodPlatform> ReadTargetPlatforms(JsonElement element, string path) {
    const string Reason = "must be \"server\", \"client\" or a non-empty array of them";

    static McmodPlatform ReadOne(JsonElement e, string p) =>
        e.ValueKind == JsonValueKind.String
        && McmodPlatformExtensions.TryParse(e.GetString()!, out McmodPlatform platform)
            ? platform
            : throw Invalid(p, Reason);

    return element.ValueKind switch {
      JsonValueKind.String => [ReadOne(element, path)],
      JsonValueKind.Array => ReadArray(element, path, nonEmpty: true, ReadOne),
      _ => throw Invalid(path, Reason),
    };
  }

  private static List<string> ReadTargetRuntimes(JsonElement element, string path) =>
      element.ValueKind switch {
        JsonValueKind.String => [ReadString(element, path)],
        JsonValueKind.Array => ReadArray(element, path, nonEmpty: true, ReadString),
        _ => throw Invalid(path, "must be a string or a non-empty array of strings"),
      };

  private static string ReadRefTemplate(JsonElement element, string path) {
    string template = ReadString(element, path);

    if (RefTemplate.FindUnknownPlaceholder(template) is string unknown) {
      throw Invalid(path, $"unknown placeholder {unknown} in '{template}'");
    }

    // A templated ref is validated by the resolver after expansion (spec §4.2).
    if (!RefTemplate.HasPlaceholder(template)
        && !Ref.TryParse(template, out _, out string? error)) {
      throw Invalid(path, $"invalid ref '{template}': {error}");
    }

    return template;
  }

  /// Two targets with the same variant are ambiguous when they write the same selector keys
  /// and the values of every written key intersect (spec §4.2 validity).
  private static void CheckTargetsAreUnambiguous(List<MetaTarget> targets) {
    for (int i = 0; i < targets.Count; i++) {
      for (int j = i + 1; j < targets.Count; j++) {
        MetaTarget a = targets[i];
        MetaTarget b = targets[j];

        if (a.Variant == b.Variant
            && Overlaps(a.Platforms, b.Platforms)
            && Overlaps(a.Runtimes, b.Runtimes)) {
          throw Invalid(
              $"$.targets[{j}]",
              $"ambiguous with $.targets[{i}]: same variant '{a.Variant}', same selector keys "
              + "and overlapping values");
        }
      }
    }
  }

  private static bool Overlaps<T>(IReadOnlyList<T>? a, IReadOnlyList<T>? b) =>
      (a is null && b is null) || (a is not null && b is not null && a.Intersect(b).Any());

  #endregion

  #region Package documents (spec §4.1)

  private PackageDocument ParsePackage(JsonElement root) {
    bool isMod = root.TryGetProperty("mod", out JsonElement mod);
    if (isMod) {
      ReadMod(mod, "$.mod");
      foreach (string key in new[] { "files", "archives" }) {
        if (root.TryGetProperty(key, out _)) {
          throw Invalid($"$.{key}", "is not allowed together with \"mod\"");
        }
      }
    }

    if (root.TryGetProperty("info", out JsonElement info)) {
      ReadInfo(info, "$.info");
    }

    List<PackageArchive> archives =
        ReadOptional(root, "archives", "$", (e, p) => ReadMap(e, p, ReadArchive)) ?? [];
    List<PackageFile> files = ReadOptional(root, "files", "$", ArrayOf(ReadFile)) ?? [];
    for (int i = 0; i < files.Count; i++) {
      if (files[i].From is string from && !archives.Any(a => a.Name == from)) {
        throw Invalid($"$.files[{i}].from", $"'{from}' is not a key of \"archives\"");
      }
    }

    return new PackageDocument {
      Id = ReadRequired(root, "id", "$", ReadId),
      Version = ReadRequired(root, "version", "$", ReadSemVersion),
      IsMod = isMod,
      Platform = ReadOptional(root, "platform", "$", ReadPlatform),
      Runtime = ReadOptional(root, "runtime", "$", ReadString),
      Dependencies = ReadOptional(root, "dependencies", "$", (e, p) => ReadMap(e, p, ReadDependency))
          ?? [],
      Conflicts = ReadOptional(root, "conflicts", "$", (e, p) => ReadMap(e, p, ReadConflict)) ?? [],
      Files = files,
      Archives = archives,
      Preserve = ReadOptional(root, "preserve", "$", ArrayOf(ReadGlob)) ?? [],
      Remove = ReadOptional(root, "remove", "$", ArrayOf(ReadGlob)) ?? [],
      Hooks = ReadOptional(root, "hooks", "$", ReadHooks) ?? new(),
    };
  }

  private static McmodPlatform? ReadPlatform(JsonElement element, string path) =>
      element.ValueKind == JsonValueKind.String
      && McmodPlatformExtensions.TryParse(element.GetString()!, out McmodPlatform platform)
          ? platform
          : throw Invalid(path, "must be \"server\" or \"client\"");

  private static void ReadInfo(JsonElement element, string path) {
    RequireObject(element, path);

    _ = ReadOptional(element, "name", path, ReadString);
    _ = ReadOptional(element, "description", path, ReadString);
    _ = ReadOptional(element, "authors", path, ArrayOf(ReadString));
    _ = ReadOptional(element, "tags", path, ArrayOf(ReadString));
    _ = ReadOptional(element, "icon", path, ReadHttpsUrl);
  }

  private Dep ReadDependency(string id, JsonElement element, string path) {
    if (element.ValueKind == JsonValueKind.String) {
      return new Dep { Id = id, Version = ReadRange(element, path) };
    }

    RequireObject(element, path, "must be a range or an object");

    List<Ref>? refs = ReadOptional(element, "source", path, (e, p) => ReadOneOrMany(e, p, ReadRef));
    if (element.TryGetProperty("mod", out JsonElement mod)) {
      if (mod.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) {
        throw Invalid($"{path}.mod", $"must be a boolean, not {Describe(mod)}");
      }

      if (mod.ValueKind == JsonValueKind.False && refs is null) {
        throw Invalid($"{path}.mod", "false is only allowed together with \"source\"");
      }
    }

    return new Dep {
      Id = id,
      Version = ReadOptional(element, "version", path, ReadRange),
      Refs = refs,
    };
  }

  private Dep ReadConflict(string id, JsonElement element, string path) =>
      new() { Id = id, Version = ReadRange(element, path) };

  private static void ReadMod(JsonElement element, string path) {
    RequireObject(element, path);

    foreach (string key in new[] { "loader", "entry" }) {
      if (ReadRequired(element, key, path, ReadString) == "") {
        throw Invalid($"{path}.{key}", "must not be empty");
      }
    }

    if (element.TryGetProperty("passive", out JsonElement passive)
        && passive.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) {
      throw Invalid($"{path}.passive", $"must be a boolean, not {Describe(passive)}");
    }

    foreach (string key in new[] { "loadAfter", "loadBefore" }) {
      _ = ReadOptional(element, key, path, ArrayOf((e, p) => {
        string value = ReadString(e, p);
        return value == "*" || IsId(value)
            ? value
            : throw Invalid(p, $"'{value}' is neither an id nor \"*\"");
      }));
    }

    if (element.TryGetProperty("extra", out JsonElement extra)) {
      RequireObject(extra, $"{path}.extra");
    }
  }

  private static PackageFile ReadFile(JsonElement element, string path) {
    RequireObject(element, path);

    string? from = ReadOptional(element, "from", path, ReadString);
    string src = ReadRequired(element, "src", path, ReadFilePath);
    string dest = ReadRequired(element, "dest", path, ReadFilePath);
    PackageFile file = new(from, src, dest);

    bool destIsDirectory = dest == "" || dest.EndsWith('/');
    switch (file.Form) {
      case PackageFileForm.Glob:
        try {
          _ = new PathGlob(src);
        }
        catch (FormatException ex) {
          throw Invalid($"{path}.src", $"invalid glob '{src}'", ex);
        }

        if (!destIsDirectory) {
          throw Invalid($"{path}.dest", $"'{dest}' must be \"\" or end with '/' for a glob src");
        }

        break;
      case PackageFileForm.Directory when !destIsDirectory:
        throw Invalid($"{path}.dest", $"'{dest}' must be \"\" or end with '/' for a directory src");
      case PackageFileForm.File when destIsDirectory:
        throw Invalid($"{path}.dest", $"'{dest}' must name a file for a file src");
    }

    return file;
  }

  private static string ReadFilePath(JsonElement element, string path) {
    string value = ReadString(element, path);
    string trimmed = value.EndsWith('/') ? value[..^1] : value;

    string? reason = value switch {
      "" => null,
      _ when value.StartsWith('/') => "must not start with '/'",
      _ when value.Contains('\\') => "must not contain '\\'",
      _ when DriveLetterRegex().IsMatch(value) => "must not start with a drive letter",
      _ when trimmed.Split('/').Contains("") => "must not contain an empty segment",
      _ when trimmed.Split('/').Contains("..") => "must not contain a '..' segment",
      _ => null,
    };

    return reason is null ? value : throw Invalid(path, $"'{value}' {reason}");
  }

  private static PackageArchive ReadArchive(string name, JsonElement element, string path) {
    RequireObject(element, path);

    string? sha256 = ReadOptional(element, "sha256", path, ReadString);
    if (sha256 is not null && !Sha256Regex().IsMatch(sha256)) {
      throw Invalid($"{path}.sha256", "must be 64 hexadecimal digits");
    }

    string? format = ReadOptional(element, "format", path, ReadString);
    return new PackageArchive(
        name,
        ReadRequired(element, "url", path, (e, p) => ReadOneOrMany(e, p, ReadHttpsUrl)),
        sha256?.ToLowerInvariant(),
        format switch {
          null => PackageArchiveFormat.Detect,
          "zip" => PackageArchiveFormat.Zip,
          "tar" => PackageArchiveFormat.Tar,
          "tgz" => PackageArchiveFormat.Tgz,
          "file" => PackageArchiveFormat.File,
          _ => throw Invalid($"{path}.format", "must be \"zip\", \"tar\", \"tgz\" or \"file\""),
        });
  }

  private IVersionConstraint ReadRange(JsonElement element, string path) {
    string text = ReadString(element, path);
    try {
      return _parseRange(text);
    }
    catch (FormatException ex) {
      throw Invalid(path, $"invalid version range '{text}': {ex.Message}", ex);
    }
    catch (ArgumentException ex) {
      throw Invalid(path, $"invalid version range '{text}': {ex.Message}", ex);
    }
  }

  private static PathGlob ReadGlob(JsonElement element, string path) {
    string pattern = ReadString(element, path);
    try {
      return new PathGlob(pattern);
    }
    catch (FormatException ex) {
      throw Invalid(path, $"invalid glob '{pattern}'", ex);
    }
  }

  private static Hooks ReadHooks(JsonElement element, string path) {
    RequireObject(element, path);

    Func<JsonElement, string, List<string>> commands = ArrayOf(ReadString);

    return new Hooks {
      PreInstall = ReadOptional(element, "preInstall", path, commands) ?? [],
      PostInstall = ReadOptional(element, "postInstall", path, commands) ?? [],
      PreUninstall = ReadOptional(element, "preUninstall", path, commands) ?? [],
      PostUninstall = ReadOptional(element, "postUninstall", path, commands) ?? [],
    };
  }

  #endregion

  #region Primitives

  private static Ref ReadRef(JsonElement element, string path) {
    string s = ReadString(element, path);
    return Ref.TryParse(s, out Ref? r, out string? error)
        ? r
        : throw Invalid(path, $"invalid ref '{s}': {error}");
  }

  private static string ReadId(JsonElement element, string path) {
    string value = ReadString(element, path);
    return IsId(value)
        ? value
        : throw Invalid(path, $"'{value}' does not match ^[A-Za-z0-9][A-Za-z0-9._-]*$");
  }

  private static string ReadHttpsUrl(JsonElement element, string path) {
    string value = ReadString(element, path);
    return Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps
        ? value
        : throw Invalid(path, $"'{value}' is not an https URL");
  }

  private static SemVersion ReadSemVersion(JsonElement element, string path) {
    string s = ReadString(element, path);
    return SemVersion.TryParse(s, SemVersionStyles.Strict, out SemVersion? version)
        ? version
        : throw Invalid(path, $"'{s}' is not a SemVer 2.0.0 version");
  }

  private static string ReadString(JsonElement element, string path) {
    if (element.ValueKind != JsonValueKind.String) {
      throw Invalid(path, $"must be a string, not {Describe(element)}");
    }

    try {
      return element.GetString()!;
    }
    catch (InvalidOperationException ex) {
      // An escaped lone surrogate such as "\uD800" is valid JSON syntax but not valid Unicode.
      throw Invalid(path, "is not a valid Unicode string", ex);
    }
  }

  private static void RequireObject(JsonElement element, string path, string? reason = null) {
    if (element.ValueKind != JsonValueKind.Object) {
      throw Invalid(path, $"{reason ?? "must be an object"}, not {Describe(element)}");
    }
  }

  /// A value or a non-empty array of values.
  private static List<T> ReadOneOrMany<T>(
      JsonElement element, string path, Func<JsonElement, string, T> readItem) =>
      element.ValueKind == JsonValueKind.Array
          ? ReadArray(element, path, nonEmpty: true, readItem)
          : [readItem(element, path)];

  /// An object whose member names are ids (spec §1), in document order.
  private static List<T> ReadMap<T>(
      JsonElement element, string path, Func<string, JsonElement, string, T> readValue) {
    RequireObject(element, path);

    List<T> values = [];
    foreach (JsonProperty property in element.EnumerateObject()) {
      string memberPath = $"{path}[\"{property.Name}\"]";
      if (!IsId(property.Name)) {
        throw Invalid(memberPath, $"'{property.Name}' does not match ^[A-Za-z0-9][A-Za-z0-9._-]*$");
      }

      values.Add(readValue(property.Name, property.Value, memberPath));
    }

    return values;
  }

  private static Func<JsonElement, string, List<T>> ArrayOf<T>(
      Func<JsonElement, string, T> readItem) =>
      (e, p) => ReadArray(e, p, nonEmpty: false, readItem);

  private static List<T> ReadArray<T>(
      JsonElement element,
      string path,
      bool nonEmpty,
      Func<JsonElement, string, T> readItem) {
    if (element.ValueKind != JsonValueKind.Array) {
      throw Invalid(path, $"must be an array, not {Describe(element)}");
    }

    if (nonEmpty && element.GetArrayLength() == 0) {
      throw Invalid(path, "must not be empty");
    }

    return [.. element.EnumerateArray().Select((item, i) => readItem(item, $"{path}[{i}]"))];
  }

  private static T ReadRequired<T>(
      JsonElement parent,
      string key,
      string parentPath,
      Func<JsonElement, string, T> read) =>
      parent.TryGetProperty(key, out JsonElement value)
          ? read(value, $"{parentPath}.{key}")
          : throw Invalid($"{parentPath}.{key}", "is required");

  private static T? ReadOptional<T>(
      JsonElement parent,
      string key,
      string parentPath,
      Func<JsonElement, string, T> read) =>
      parent.TryGetProperty(key, out JsonElement value) ? read(value, $"{parentPath}.{key}") : default;

  private static string Describe(JsonElement element) => element.ValueKind switch {
    JsonValueKind.Object => "an object",
    JsonValueKind.Array => "an array",
    JsonValueKind.String => "a string",
    JsonValueKind.Number => "a number",
    JsonValueKind.True or JsonValueKind.False => "a boolean",
    JsonValueKind.Null => "null",
    _ => element.ValueKind.ToString(),
  };

  private static ManifestFormatException Invalid(
      string path,
      string reason,
      Exception? inner = null) =>
      new($"Invalid manifest.json at {path}: {reason}.", inner);

  [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]*\z")]
  private static partial Regex IdRegex();

  [GeneratedRegex(@"^[0-9A-Fa-f]{64}\z")]
  private static partial Regex Sha256Regex();

  [GeneratedRegex(@"^[A-Za-z]:")]
  private static partial Regex DriveLetterRegex();

  #endregion
}
