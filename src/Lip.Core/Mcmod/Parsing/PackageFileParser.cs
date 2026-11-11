using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lip.Core.Json;
using Semver;

namespace Lip.Core.Mcmod;

/// Thrown when a package file, or an entry of one, is not valid (protocol/lip.toml.md), or when
/// a tooth.json cannot be read (protocol/lip-compatibility.md, "tooth.json").
public class PackageFormatException(string message, Exception? inner = null)
    : FormatException(message, inner);

/// The package file is of a format newer than this lip knows; updating lip helps.
public sealed class NewerPackageFormatException(string message) : PackageFormatException(message);

/// Parses and validates package files (lip.schema.toml and protocol/lip.toml.md), and entries,
/// also those derived from a tooth.json (protocol/lip-compatibility.md, "tooth.json") that the
/// state file records. Keys the schema does not describe are ignored.
public static partial class PackageFileParser {
  private static readonly UTF8Encoding _strictUtf8 = new(
      encoderShouldEmitUTF8Identifier: false,
      throwOnInvalidBytes: true);

  private static readonly JsonDocumentOptions _jsonOptions = new() {
    AllowDuplicateProperties = false,
    AllowTrailingCommas = false,
    CommentHandling = JsonCommentHandling.Disallow,
  };

  /// Parses strict JSON [RFC8259]: UTF-8 (a byte order mark is ignored), no comments, no
  /// trailing commas and no duplicate member names. fileName names the file in errors. The caller
  /// disposes the result.
  public static JsonDocument ParseJson(byte[] utf8Json, string fileName) {
    ReadOnlyMemory<byte> bytes = utf8Json;

    if (bytes.Span.StartsWith(Encoding.UTF8.Preamble)) {
      bytes = bytes[Encoding.UTF8.Preamble.Length..];
    }

    try {
      _ = _strictUtf8.GetCharCount(bytes.Span);
    }
    catch (DecoderFallbackException ex) {
      throw new PackageFormatException($"Invalid {fileName}: not valid UTF-8.", ex);
    }

    try {
      return JsonDocument.Parse(bytes, _jsonOptions);
    }
    catch (JsonException ex) {
      throw new PackageFormatException(
          $"Invalid {fileName}: not strict JSON (no comments, trailing commas or duplicate keys).",
          ex);
    }
  }

  /// The "format" of a package file or of the state file, or null when it has none that is an
  /// integer.
  public static int? Format(JsonElement root) => IntegerMember(root, PackageFormat.FormatKey);

  /// The member key of an object that is an integer, or null.
  public static int? IntegerMember(JsonElement root, string key) =>
      root.ValueKind == JsonValueKind.Object
      && root.TryGetProperty(key, out JsonElement version)
      && version.ValueKind == JsonValueKind.Number
      && version.TryGetDouble(out double value)
      && value == Math.Floor(value) && value is >= int.MinValue and <= int.MaxValue
          ? (int)value
          : null;

  /// Reads lip.toml, a TOML file (protocol/README.md), with its templates rendered
  /// (protocol/lip.toml.md). The caller disposes the result. Throws PackageFormatException when
  /// it is not one, or a template cannot be rendered.
  public static JsonDocument ParseToml(byte[] toml) {
    JsonDocument written;
    try {
      written = TomlJson.Parse(toml);
    }
    catch (TomlFormatException ex) {
      throw new PackageFormatException(
          $"The {PackageFormat.FileName} is not a valid TOML file of the kind lip reads.", ex);
    }

    using (written) {
      return PackageTemplates.Render(written.RootElement);
    }
  }

  /// Parses a package file, the bytes of lip.toml. Throws PackageFormatException when it is not
  /// valid.
  public static PackageFile Parse(byte[] toml) {
    using JsonDocument json = ParseToml(toml);
    return Parse(json.RootElement);
  }

  /// Parses a package file. Throws PackageFormatException when it is not valid.
  public static PackageFile Parse(JsonElement root) {
    RequireObject(root, "$");
    int? format = Format(root);
    if (format > PackageFormat.Format) {
      throw new NewerPackageFormatException(
          $"The package file has \"{PackageFormat.FormatKey}\": {format}, a format this lip does not"
          + $" know (it knows format {PackageFormat.Format}). The package needs a newer lip: update"
          + " lip.");
    }

    if (format != PackageFormat.Format) {
      throw Invalid($"$.{PackageFormat.FormatKey}", $"must be {PackageFormat.Format}");
    }

    string id = ReadRequired(root, "id", "$", ReadString);
    if (Golang.Org.X.Mod.Module.CheckPath(id) is Exception ex) {
      throw Invalid("$.id", $"'{id}' is not a valid Go module path", ex);
    }

    SemVersion version = ReadRequired(root, "version", "$", ReadSemVersion);
    if (root.TryGetProperty("info", out JsonElement info)) {
      ReadInfo(info, "$.info");
    }

    JsonElement variants = ReadRequired(root, "variants", "$", (e, p) => {
      RequireObject(e, p);
      return e;
    });

    Dictionary<string, IReadOnlyList<PackageEntry>> entries = new(StringComparer.Ordinal);
    foreach (JsonProperty variant in variants.EnumerateObject()) {
      string path = $"$.variants[\"{variant.Name}\"]";
      if (variant.Name != "" && !Ref.IsValidVariant(variant.Name)) {
        throw Invalid(path, $"'{variant.Name}' is not a variant");
      }

      List<PackageEntry> list = ReadArray(
          variant.Value, path, nonEmpty: true, ParseEntryAt);
      for (int i = 0; i < list.Count; i++) {
        for (int j = 0; j < i; j++) {
          if (list[i].Side == list[j].Side && list[i].Platform == list[j].Platform) {
            throw Invalid($"{path}[{i}]",
                $"has the side and the platform of {path}[{j}]");
          }
        }
      }

      entries[variant.Name] = list;
    }

    return new PackageFile {
      Id = id,
      Version = version,
      Variants = entries,
      Json = root.Clone(),
    };
  }

  /// Parses an entry of a package file. Throws PackageFormatException.
  public static PackageEntry ParseEntry(JsonElement element) => ParseEntryAt(element, "$");

  /// Whether a member of place is valid (lip.schema.toml "place").
  public static bool IsValidPlacement(string src, string dest) {
    using JsonDocument json = JsonDocument.Parse(JsonSerializer.Serialize(new { src, dest }));
    try {
      _ = ReadPlacement(json.RootElement, "$");
      return true;
    }
    catch (PackageFormatException) {
      return false;
    }
  }

  /// Whether a string is a platform: a .NET runtime identifier, without wildcards.
  public static bool IsPlatform(string value) => PlatformRegex().IsMatch(value);

  /// Why an entry, or the root of the state file, is invalid when it uses the keys of lip before
  /// v0.40: "platform" for the side and "runtime" for the platform.
  public const string OldEnvironmentKeysReason =
      "uses the keys \"platform\" and \"runtime\" of lip before v0.40; rename \"platform\" to"
      + " \"side\" and \"runtime\" to \"platform\"";

  /// Whether an object uses the keys of lip before v0.40 for its environment: it has "runtime",
  /// or its "platform" is a side ("server" or "client") rather than a RID.
  public static bool UsesOldEnvironmentKeys(JsonElement element) =>
      element.ValueKind == JsonValueKind.Object
      && (element.TryGetProperty("runtime", out _)
          || (element.TryGetProperty("platform", out JsonElement platform)
              && platform.ValueKind == JsonValueKind.String
              && McmodSideExtensions.TryParse(platform.GetString()!, out _)));

  private static PackageEntry ParseEntryAt(JsonElement element, string path) {
    RequireObject(element, path);
    if (UsesOldEnvironmentKeys(element)) {
      throw Invalid(path, OldEnvironmentKeysReason);
    }

    List<EntryAsset> assets = ReadOptional(element, "assets", path, ArrayOf(ReadAsset)) ?? [];
    if (assets.Count(a => a.Type == AssetType.Mcmod) > 1) {
      throw Invalid($"{path}.assets", "has more than one asset of type \"mcmod\"");
    }

    return new PackageEntry {
      Side = ReadRequired(element, "side", path, ReadSide),
      Platform = ReadRequired(element, "platform", path, (e, p) => {
        string platform = ReadString(e, p);
        return IsPlatform(platform)
            ? platform
            : throw Invalid(p, $"'{platform}' is not a runtime identifier");
      }),
      Dependencies = ReadOptional(element, "deps", path, ReadDependencies) ?? [],
      Assets = assets,
      PreserveFiles = ReadOptional(element, "preserve", path, ArrayOf(ReadGlob)) ?? [],
      RemoveFiles = ReadOptional(element, "purge", path, ArrayOf(ReadGlob)) ?? [],
      Hooks = ReadOptional(element, "hooks", path, ReadHooks) ?? new(),
      Json = element.Clone(),
    };
  }

  private static void ReadInfo(JsonElement element, string path) {
    RequireObject(element, path);

    _ = ReadOptional(element, "name", path, ReadString);
    _ = ReadOptional(element, "description", path, ReadString);
    _ = ReadOptional(element, "tags", path, ArrayOf(ReadString));
    if (ReadOptional(element, "logo", path, ReadString) is string logo
        && !logo.StartsWith("https://", StringComparison.Ordinal)) {
      throw Invalid($"{path}.logo", $"'{logo}' does not start with \"https://\"");
    }
  }

  private static McmodSide ReadSide(JsonElement element, string path) =>
      element.ValueKind == JsonValueKind.String
      && McmodSideExtensions.TryParse(element.GetString()!, out McmodSide side)
          ? side
          : throw Invalid(path, "must be \"server\" or \"client\"");

  private static List<PackageDependency> ReadDependencies(JsonElement element, string path) {
    RequireObject(element, path);

    List<PackageDependency> dependencies = [];
    foreach (JsonProperty property in element.EnumerateObject()) {
      string memberPath = $"{path}[\"{property.Name}\"]";
      if (!Ref.TryParse(property.Name, out Ref? source, out string? error)
          || source.Kind != RefKind.Spec || source.GitRef is not null) {
        throw Invalid(memberPath, $"'{property.Name}' is not <package id> or "
            + $"<package id>#<variant>{(error is null ? "" : $" ({error})")}");
      }

      string text = ReadString(property.Value, memberPath);
      VersionRange range;
      try {
        range = VersionRange.Parse(text);
      }
      catch (FormatException ex) {
        throw Invalid(memberPath, $"'{text}' is not a version range", ex);
      }

      dependencies.Add(new PackageDependency(source, range));
    }

    return dependencies;
  }

  private static EntryAsset ReadAsset(JsonElement element, string path) {
    RequireObject(element, path);

    string type = ReadRequired(element, "type", path, ReadString);
    switch (type) {
      case "self":
        return new EntryAsset(
            AssetType.Self, [], ReadOptional(element, "place", path, ReadPlacements) ?? []);
      case "zip":
        return new EntryAsset(
            AssetType.Zip,
            ReadRequired(element, "urls", path, ReadUrls),
            ReadOptional(element, "place", path, ReadPlacements) ?? []);
      case "mcmod":
        if (element.TryGetProperty("place", out _)) {
          throw Invalid($"{path}.place", "is not allowed for an asset of type \"mcmod\"");
        }

        return new EntryAsset(AssetType.Mcmod, ReadRequired(element, "urls", path, ReadUrls), []);
      default:
        throw Invalid($"{path}.type", "must be \"self\", \"zip\" or \"mcmod\"");
    }
  }

  private static List<string> ReadUrls(JsonElement element, string path) =>
      ReadArray(element, path, nonEmpty: true, ReadString);

  private static List<EntryPlacement> ReadPlacements(JsonElement element, string path) =>
      ReadArray(element, path, nonEmpty: false, ReadPlacement);

  private static EntryPlacement ReadPlacement(JsonElement element, string path) {
    RequireObject(element, path);

    EntryPlacement placement = new(
        ReadRequired(element, "src", path, ReadPath),
        ReadRequired(element, "dest", path, ReadPath));

    bool destIsDirectory = placement.Dest == "" || placement.Dest.EndsWith('/');
    switch (placement.Form) {
      case PlacementForm.Glob:
        try {
          _ = new PathGlob(placement.Src);
        }
        catch (FormatException ex) {
          throw Invalid($"{path}.src", $"'{placement.Src}' is not a glob", ex);
        }

        if (!destIsDirectory) {
          throw Invalid($"{path}.dest", $"'{placement.Dest}' must be \"\" or end with '/' for a "
              + "glob src");
        }

        break;
      case PlacementForm.Directory when !destIsDirectory:
        throw Invalid($"{path}.dest", $"'{placement.Dest}' must be \"\" or end with '/' for a "
            + "directory src");
      case PlacementForm.File when destIsDirectory:
        throw Invalid($"{path}.dest", $"'{placement.Dest}' must name a file for a file src");
    }

    return placement;
  }

  /// A path (lip.schema.toml "path").
  private static string ReadPath(JsonElement element, string path) {
    string value = ReadString(element, path);

    string? reason = value switch {
      _ when value.StartsWith('/') => "must not start with '/'",
      _ when DriveLetterRegex().IsMatch(value) => "must not start with a drive letter",
      _ when value.Contains("//", StringComparison.Ordinal) => "must not contain '//'",
      _ when value.Contains('\\') => "must not contain '\\'",
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
      throw Invalid(path, $"'{pattern}' is not a glob", ex);
    }
  }

  private static Hooks ReadHooks(JsonElement element, string path) {
    RequireObject(element, path);

    Func<JsonElement, string, List<string>> commands = ArrayOf(ReadString);

    return new Hooks {
      PreInstall = ReadOptional(element, "pre_install", path, commands) ?? [],
      PostInstall = ReadOptional(element, "post_install", path, commands) ?? [],
      PreUninstall = ReadOptional(element, "pre_uninstall", path, commands) ?? [],
      PostUninstall = ReadOptional(element, "post_uninstall", path, commands) ?? [],
    };
  }

  private static SemVersion ReadSemVersion(JsonElement element, string path) {
    string s = ReadString(element, path);
    return SemVersion.TryParse(s, SemVersionStyles.Strict, out SemVersion? version)
        ? version
        : throw Invalid(path, $"'{s}' is not a version");
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

  private static PackageFormatException Invalid(
      string path,
      string reason,
      Exception? inner = null) =>
      new($"Invalid package file at {path}: {reason}.", inner);

  /// A RID: dot- or dash-separated parts of letters, digits and '_' (e.g. "win-x64",
  /// "linux-musl-arm64", "osx.13-arm64").
  [GeneratedRegex(@"^[A-Za-z0-9_]+(?:[.-][A-Za-z0-9_]+)*\z")]
  private static partial Regex PlatformRegex();

  [GeneratedRegex(@"^[A-Za-z]:")]
  private static partial Regex DriveLetterRegex();
}
