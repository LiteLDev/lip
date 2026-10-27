using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Semver;

namespace Lip.Core.Mcmod;

/// Parses and validates a root manifest.json (spec §1, §4).
///
/// A document carrying both format markers (formatVersion 4 and the format UUID) is a v4 document.
/// A document carrying neither is a plain LeviLamina manifest: it is accepted only as a concrete
/// document, and the refs of its dependencies and its installDependencies are ignored (it never
/// creates install edges). A document carrying exactly one marker, or a marker with another value,
/// is invalid.
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

    JsonDocument json;
    try {
      json = JsonDocument.Parse(bytes, _jsonOptions);
    }
    catch (JsonException ex) {
      throw new ManifestFormatException(
          $"Invalid manifest.json: not strict JSON ({ex.Message})", ex);
    }

    using (json) {
      JsonElement root = json.RootElement;
      if (root.ValueKind != JsonValueKind.Object) {
        throw Invalid("$", "the top level must be an object");
      }

      bool isMarked = ReadMarkers(root);

      if (root.TryGetProperty("artifacts", out _)) {
        if (!isMarked) {
          throw Invalid(
              "$.artifacts",
              $"a routing document must declare \"{ManifestFormat.FormatVersionKey}\": "
              + $"{ManifestFormat.Version} and \"{ManifestFormat.FormatUuidKey}\": "
              + $"\"{ManifestFormat.Uuid}\"");
        }

        return ParseRouting(root);
      }

      return ParseConcrete(root, isMarked);
    }
  }

  private static bool ReadMarkers(JsonElement root) {
    bool hasVersion = root.TryGetProperty(ManifestFormat.FormatVersionKey, out JsonElement version);
    bool hasUuid = root.TryGetProperty(ManifestFormat.FormatUuidKey, out JsonElement uuid);

    if (hasVersion && !(version.ValueKind == JsonValueKind.Number
        && version.TryGetInt32(out int v) && v == ManifestFormat.Version)) {
      throw Invalid($"$.{ManifestFormat.FormatVersionKey}", $"must be {ManifestFormat.Version}");
    }

    if (hasUuid && !(uuid.ValueKind == JsonValueKind.String
        && uuid.GetString() == ManifestFormat.Uuid)) {
      throw Invalid($"$.{ManifestFormat.FormatUuidKey}", $"must be \"{ManifestFormat.Uuid}\"");
    }

    if (hasVersion != hasUuid) {
      (string present, string missing) = hasVersion
          ? (ManifestFormat.FormatVersionKey, ManifestFormat.FormatUuidKey)
          : (ManifestFormat.FormatUuidKey, ManifestFormat.FormatVersionKey);
      throw Invalid($"$.{missing}", $"is required when \"{present}\" is present");
    }

    return hasVersion;
  }

  #region Routing documents (spec §4.1)

  private static RoutingDocument ParseRouting(JsonElement root) {
    JsonElement artifactsJson = root.GetProperty("artifacts");
    List<Artifact> artifacts = ReadArray(
        artifactsJson, "$.artifacts", nonEmpty: true, ReadArtifact);

    CheckArtifactsAreUnambiguous(artifacts);

    return new RoutingDocument {
      IsMarked = true,
      Name = ReadOptional(root, "name", "$", ReadString),
      Version = ReadOptional(root, "version", "$", ReadSemVersion),
      Artifacts = artifacts,
      Refs = ReadOptional(root, "refs", "$", (e, p) => ReadRefs(e, p, nonEmpty: false)) ?? [],
    };
  }

  private static Artifact ReadArtifact(JsonElement element, string path) {
    RequireObject(element, path);

    string variant = ReadOptional(element, "variant", path, ReadString) ?? "";
    if (variant != "" && !Ref.IsValidVariant(variant)) {
      throw Invalid($"{path}.variant", $"'{variant}' is not a valid variant");
    }

    return new Artifact {
      Variant = variant,
      Platforms = ReadOptional(element, "platform", path, ReadArtifactPlatforms),
      Rids = ReadOptional(element, "rid", path, ReadArtifactRids),
      Refs = ReadRequired(element, "refs", path, (e, p) => ReadArray(
          e, p, nonEmpty: true, ReadRefTemplate)),
    };
  }

  private static List<McmodPlatform> ReadArtifactPlatforms(JsonElement element, string path) {
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

  private static List<string> ReadArtifactRids(JsonElement element, string path) =>
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

    // A templated ref is validated by the resolver after expansion (spec §4.1).
    if (!KnownPlaceholderRegex().IsMatch(template)
        && !Ref.TryParse(template, out _, out string? error)) {
      throw Invalid(path, $"invalid ref '{template}': {error}");
    }

    return template;
  }

  /// Two artifacts with the same variant are ambiguous when they write the same selector keys
  /// and the values of every written key intersect (spec §4.1 validity).
  private static void CheckArtifactsAreUnambiguous(List<Artifact> artifacts) {
    for (int i = 0; i < artifacts.Count; i++) {
      for (int j = i + 1; j < artifacts.Count; j++) {
        Artifact a = artifacts[i];
        Artifact b = artifacts[j];

        if (a.Variant == b.Variant
            && Overlaps(a.Platforms, b.Platforms)
            && Overlaps(a.Rids, b.Rids)) {
          throw Invalid(
              $"$.artifacts[{j}]",
              $"ambiguous with $.artifacts[{i}]: same variant '{a.Variant}', same selector keys "
              + "and overlapping values");
        }
      }
    }
  }

  private static bool Overlaps<T>(IReadOnlyList<T>? a, IReadOnlyList<T>? b) =>
      (a is null && b is null) || (a is not null && b is not null && a.Intersect(b).Any());

  #endregion

  #region Concrete documents (spec §4.2)

  private ConcreteDocument ParseConcrete(JsonElement root, bool isMarked) {
    string name = ReadRequired(root, "name", "$", ReadString);
    if (!NameRegex().IsMatch(name)) {
      throw Invalid("$.name", $"'{name}' does not match ^[A-Za-z0-9][A-Za-z0-9._-]*$");
    }

    string type = ReadRequired(root, "type", "$", ReadString);
    bool isFilePackage = type == "files";

    if (!isFilePackage && root.TryGetProperty("placements", out _)) {
      throw Invalid("$.placements", "only allowed when \"type\" is \"files\"");
    }

    Func<JsonElement, string, Dep> readDep = (e, p) => ReadDep(e, p, isMarked);

    return new ConcreteDocument {
      IsMarked = isMarked,
      Name = name,
      Version = ReadRequired(root, "version", "$", ReadSemVersion),
      Type = type,
      Platform = root.TryGetProperty("platform", out JsonElement platform)
          ? ReadPackagePlatform(platform, "$.platform")
          : PackagePlatform.Universal,
      Rid = ReadOptional(root, "rid", "$", ReadString),
      Refs = ReadOptional(root, "refs", "$", (e, p) => ReadRefs(e, p, nonEmpty: false)) ?? [],
      Dependencies = ReadOptional(root, "dependencies", "$", ArrayOf(readDep)) ?? [],
      OptionalDependencies =
          ReadOptional(root, "optionalDependencies", "$", ArrayOf(readDep)) ?? [],
      Conflicts = ReadOptional(root, "conflicts", "$", ArrayOf(readDep)) ?? [],
      // Unmarked documents never create install edges, so their installDependencies are ignored.
      InstallDependencies = isMarked
          ? ReadOptional(root, "installDependencies", "$", ArrayOf(ReadInstallDep)) ?? []
          : [],
      Placements = ReadOptional(root, "placements", "$", ArrayOf(ReadPlacement)) ?? [],
      PreserveFiles = ReadOptional(root, "preserveFiles", "$", ArrayOf(ReadGlob)) ?? [],
      RemoveFiles = ReadOptional(root, "removeFiles", "$", ArrayOf(ReadGlob)) ?? [],
      Scripts = ReadOptional(root, "scripts", "$", ReadScripts) ?? new(),
    };
  }

  private static PackagePlatform ReadPackagePlatform(JsonElement element, string path) {
    string? value = element.ValueKind == JsonValueKind.String ? ReadString(element, path) : null;

    if (value == "universal") {
      return PackagePlatform.Universal;
    }

    return value is not null && McmodPlatformExtensions.TryParse(value, out McmodPlatform platform)
        ? platform switch {
          McmodPlatform.Server => PackagePlatform.Server,
          _ => PackagePlatform.Client,
        }
        : throw Invalid(path, "must be \"server\", \"client\" or \"universal\"");
  }

  private Dep ReadDep(JsonElement element, string path, bool isMarked) {
    RequireObject(element, path);

    return new Dep {
      Name = ReadRequired(element, "name", path, ReadString),
      Version = ReadOptional(element, "version", path, ReadRange),
      // Unmarked documents never create install edges, so the refs of their entries are ignored.
      Refs = isMarked
          ? ReadOptional(element, "refs", path, (e, p) => ReadRefs(e, p, nonEmpty: true))
          : null,
    };
  }

  private InstallDep ReadInstallDep(JsonElement element, string path) {
    RequireObject(element, path);

    return new InstallDep {
      Name = ReadOptional(element, "name", path, ReadString),
      Version = ReadOptional(element, "version", path, ReadRange),
      Refs = ReadRequired(element, "refs", path, (e, p) => ReadRefs(e, p, nonEmpty: true)),
    };
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

  private static Placement ReadPlacement(JsonElement element, string path) {
    RequireObject(element, path);

    string src = ReadRequired(element, "src", path, ReadPlacementPath);
    string dest = ReadRequired(element, "dest", path, ReadPlacementPath);

    if (IsDirectoryForm(src) != IsDirectoryForm(dest)) {
      throw Invalid(
          path,
          $"src '{src}' and dest '{dest}' must both be directories (\"\" or ending with '/') "
          + "or both be files");
    }

    return new Placement(src, dest);
  }

  private static bool IsDirectoryForm(string path) => path == "" || path.EndsWith('/');

  private static string ReadPlacementPath(JsonElement element, string path) {
    string value = ReadString(element, path);

    string? reason = value switch {
      _ when value.StartsWith('/') => "must not start with '/'",
      _ when value.Contains('\\') => "must not contain '\\'",
      _ when DriveLetterRegex().IsMatch(value) => "must not start with a drive letter",
      _ when value.Split('/').Contains("..") => "must not contain a '..' segment",
      _ => null,
    };

    return reason is null ? value : throw Invalid(path, $"'{value}' {reason}");
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

  private static Scripts ReadScripts(JsonElement element, string path) {
    RequireObject(element, path);

    Func<JsonElement, string, List<string>> commands = ArrayOf(ReadString);

    return new Scripts {
      PreInstall = ReadOptional(element, "preInstall", path, commands) ?? [],
      PostInstall = ReadOptional(element, "postInstall", path, commands) ?? [],
      PreUninstall = ReadOptional(element, "preUninstall", path, commands) ?? [],
      PostUninstall = ReadOptional(element, "postUninstall", path, commands) ?? [],
    };
  }

  #endregion

  #region Primitives

  private static List<Ref> ReadRefs(JsonElement element, string path, bool nonEmpty) =>
      ReadArray(element, path, nonEmpty, (e, p) => {
        string s = ReadString(e, p);
        return Ref.TryParse(s, out Ref? r, out string? error)
            ? r
            : throw Invalid(p, $"invalid ref '{s}': {error}");
      });

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

  private static void RequireObject(JsonElement element, string path) {
    if (element.ValueKind != JsonValueKind.Object) {
      throw Invalid(path, $"must be an object, not {Describe(element)}");
    }
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
      Func<JsonElement, string, T> read) where T : class =>
      parent.TryGetProperty(key, out JsonElement value) ? read(value, $"{parentPath}.{key}") : null;

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

  [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]*$")]
  private static partial Regex NameRegex();

  [GeneratedRegex(@"\{\{(name|version|platform|rid)\}\}")]
  private static partial Regex KnownPlaceholderRegex();

  [GeneratedRegex(@"^[A-Za-z]:")]
  private static partial Regex DriveLetterRegex();

  #endregion
}
