using System.Text.Json;
using System.Text.Json.Nodes;
using Lip.Core.Entities;
using Lip.Core.Infrastructure;
using Lip.Core.Json;
using Lip.Core.Migration.PackageManifests;

namespace Lip.Core.Mcmod.Compat;

/// The tooth.json of lip v0.x (protocol/lip-compatibility.md, "tooth.json"): a tooth.json of format
/// 1, 2 or 3 (1 and 2 are first read as 3 by PackageManifestMigration) is read, for an environment,
/// as the package file that Section 2 derives from it (Derive). Nothing of format 3 is carried
/// beyond that: the result is an ordinary package file.
///
/// For each label L of the variants of the tooth.json, the variants labeled L whose platform (a
/// RID pattern in tooth.json) is "" or matches the platform of the environment are merged exactly
/// like PackageManifest.GetVariant merges them for lip's own RID in lip v0.37.0, and become the
/// single entry of the variant L, for the side and the platform of the environment
/// (ConvertEntry).
public static class ToothCompat {
  /// The file at the root of an archive of lip v0.x that makes it a package.
  public const string ToothFileName = "tooth.json";

  /// The member of tooth.json, tooth_lock.json and liprc.json that holds their format.
  public const string FormatVersionKey = "format_version";

  /// The format_version of a tooth.json, tooth_lock.json or liprc.json; null when it has none
  /// that is an integer.
  public static int? FormatVersion(JsonElement root) =>
      PackageFileParser.IntegerMember(root, FormatVersionKey);

  /// Reads a tooth.json as format 3 (protocol/lip-compatibility.md, "tooth.json"). Throws
  /// PackageFormatException when it is not a JSON object with format_version 1, 2 or 3 that reads
  /// as format 3.
  public static PackageManifest ReadToothJson(JsonElement root) {
    int? formatVersion = FormatVersion(root);
    if (formatVersion is not (1 or 2 or 3)) {
      throw new PackageFormatException(
          $"The {ToothFileName} must have \"{FormatVersionKey}\": 1, 2 or 3, the formats of"
          + $" {ToothFileName} that lip reads, but it has "
          + (formatVersion is null ? "none." : $"{formatVersion}."));
    }

    try {
      using JsonDocument json = JsonDocument.Parse(root.GetRawText());
      return PackageManifestMigration.Migrate(json);
    }
    catch (Exception ex) when (ex is not OperationCanceledException) {
      // The migration and the format 3 model throw many kinds of exceptions on bad input.
      throw new PackageFormatException(
          $"The {ToothFileName} of format {formatVersion} does not follow that format.", ex);
    }
  }

  /// The package file that a tooth.json of format 3 is read as for an environment
  /// (protocol/lip-compatibility.md, "tooth.json"), with the version of the package as its version:
  /// version, or else the version of the tooth.json. Throws PackageFormatException when the
  /// variants of a label cannot be merged (lip v0.37.0 failed on them too), or when a derived entry
  /// is not valid in a package file.
  public static PackageFile Derive(
      PackageManifest manifest, LipEnvironment environment, Semver.SemVersion? version = null) {
    version ??= manifest.Version;
    Dictionary<string, JsonObject?> derived = new(StringComparer.Ordinal);
    foreach (string label in manifest.Variants.Select(v => v.Label).Distinct()) {
      PackageManifestVariant merged;
      try {
        merged = manifest.GetVariant(label, environment.Platform);
      }
      catch (KeyNotFoundException) {
        RunTrace.Write("compat", $"{manifest.Path} {version}: variant '{label}' has no variants "
            + $"for {environment.Platform}");
        continue;
      }
      catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) {
        throw new PackageFormatException(
            $"The {ToothFileName} of {manifest.Path} {version} is not valid: its variants labeled "
            + $"'{label}' cannot be merged for {environment.Platform}, because "
            + $"{MergeFailureReason(ex)}. lip v0.37.0 could not install them either; ask the "
            + $"package author to fix {ToothFileName}.",
            ex);
      }

      derived[label] = ConvertEntry(merged, environment.Side, environment.Platform);
      RunTrace.Write("compat", $"{manifest.Path} {version}: variant '{label}' "
          + (derived[label] is null ? "has no entry" : "derived")
          + $" for {environment.Side.ToSpecString()} {environment.Platform}");
    }

    JsonObject variants = [];
    foreach ((string label, JsonObject? entry) in derived) {
      if (entry is not null) {
        variants[label] = new JsonArray(entry);
      }
    }

    JsonObject document = new() {
      [PackageFormat.FormatKey] = PackageFormat.Format,
      ["id"] = manifest.Path,
      ["version"] = version.ToString(),
    };
    if (manifest.Info is PackageManifestInfo info) {
      document["info"] = Info(info);
    }

    document["variants"] = variants;

    JsonElement json = JsonSerializer.SerializeToElement(document);
    Dictionary<string, IReadOnlyList<PackageEntry>> entries = new(StringComparer.Ordinal);
    foreach (JsonProperty variant in json.GetProperty("variants").EnumerateObject()) {
      PackageEntry entry;
      try {
        entry = PackageFileParser.ParseEntry(variant.Value[0]);
      }
      catch (PackageFormatException ex) {
        throw new PackageFormatException(
            $"The {ToothFileName} of {manifest.Path} {version} cannot be read for "
            + $"{environment.Platform}: its variant '{variant.Name}' does not convert to a valid "
            + $"variant of {PackageFormat.FileName}. Ask the package author to move "
            + $"{ToothFileName} to {PackageFormat.FileName} with 'lip migrate'.",
            ex);
      }

      entries[variant.Name] = [entry];
    }

    return new PackageFile {
      Id = manifest.Path,
      Version = version,
      Variants = entries,
      Json = json,
    };
  }

  /// Why the variants of a label could not be merged (PackageManifest.GetVariant), in words for
  /// the user: GetVariant fails with an ArgumentException on a dependency two of them declare and
  /// with an InvalidOperationException on a script two of them have.
  public static string MergeFailureReason(Exception exception) => exception switch {
    ArgumentException => "more than one of them declares the same dependency",
    InvalidOperationException => "more than one of them sets the same kind of script"
        + " (pre_install, install, post_install, pre_uninstall, uninstall or post_uninstall)",
    _ => "none of them is for this platform",
  };

  /// The entry of a package file for the variants of a label merged for a platform
  /// (protocol/lip-compatibility.md, "tooth.json"), for a side and the platform; null when an asset
  /// has the type tar, tgz or uncompressed, which makes the variant absent. Placements and globs
  /// that are not valid in a package file are left out; notes, when given, tell about them and
  /// about dropped variants, each prefixed by field.
  public static JsonObject? ConvertEntry(
      PackageManifestVariant merged,
      McmodSide side,
      string platform,
      List<string>? notes = null,
      string field = "") {
    JsonObject entry = new() {
      ["side"] = side.ToSpecString(),
      ["platform"] = platform,
    };

    if (merged.Dependencies.Count > 0) {
      JsonObject dependencies = [];
      foreach ((PackageId id, Semver.SemVersionRange range) in merged.Dependencies) {
        dependencies[id.ToString()] = range.ToString();
      }

      entry["deps"] = dependencies;
    }

    JsonArray assets = [];
    for (int i = 0; i < merged.Assets.Count; i++) {
      PackageManifestAsset asset = merged.Assets[i];
      if (asset.Type is not (PackageManifestAsset.AssetType.Self
          or PackageManifestAsset.AssetType.Zip)) {
        notes?.Add($"{field}.assets[{i}]: {PackageFormat.FileName} has no asset type "
            + $"\"{JsonSerializer.Serialize(asset.Type).Trim('"')}\", so the variant has no entry "
            + "here. Publish its files as a ZIP archive and use an asset of type \"zip\".");
        return null;
      }

      JsonObject converted = new() {
        ["type"] = asset.Type == PackageManifestAsset.AssetType.Self ? "self" : "zip",
      };
      if (asset.Type == PackageManifestAsset.AssetType.Zip) {
        converted["urls"] = new JsonArray([.. asset.Urls.Select(u => (JsonNode)u.ToString())]);
      }

      JsonArray place = [];
      foreach (PackageManifestAssetPlacement placement in asset.Placements) {
        (string src, string dest) = ConvertPlacement(placement);
        if (PackageFileParser.IsValidPlacement(src, dest)) {
          place.Add(new JsonObject { ["src"] = src, ["dest"] = dest });
        } else {
          notes?.Add($"{field}.assets[{i}]: the placement '{placement.Src}' -> '{placement.Dst}' "
              + $"becomes '{src}' -> '{dest}', which is not valid in {PackageFormat.FileName}; it "
              + "was left out.");
        }
      }

      if (place.Count > 0) {
        converted["place"] = place;
      }

      assets.Add(converted);
    }

    if (assets.Count > 0) {
      entry["assets"] = assets;
    }

    AddGlobs(entry, "preserve",
        PreserveFiles(merged.PreserveFiles.Select(GlobListJsonConverter.Format)), notes,
        $"{field}.preserve_files");
    AddGlobs(entry, "purge",
        RemoveFiles(merged.RemoveFiles.Select(GlobListJsonConverter.Format)), notes,
        $"{field}.remove_files");

    Hooks hooks = ConvertScripts(merged.Scripts);
    JsonObject hooksObject = [];
    AddCommands(hooksObject, "pre_install", hooks.PreInstall);
    AddCommands(hooksObject, "post_install", hooks.PostInstall);
    AddCommands(hooksObject, "pre_uninstall", hooks.PreUninstall);
    AddCommands(hooksObject, "post_uninstall", hooks.PostUninstall);
    if (hooksObject.Count > 0) {
      entry["hooks"] = hooksObject;
    }

    return entry;
  }

  /// The member of place for a placement of format 3 (protocol/lip-compatibility.md, "tooth.json"):
  /// '\' becomes '/', empty and "." segments are removed; a "dir" placement has src and dest each
  /// followed by '/' unless empty, a "file" placement whose src has '*', '?' or '[' has dest
  /// followed by '/' unless empty, any other "file" placement is the file form.
  public static (string Src, string Dest) ConvertPlacement(
      PackageManifestAssetPlacement placement) {
    string src = NormalizePath(placement.Src);
    string dest = NormalizePath(placement.Dst);
    static string Directory(string path) => path == "" ? "" : path + "/";
    return placement.Type == PackageManifestAssetPlacement.PlacementType.Directory
        ? (Directory(src), Directory(dest))
        : PathGlob.HasMetacharacters(src)
            ? (src, Directory(dest))
            : (src, dest);
  }

  /// A path with '\' replaced by '/' and its empty and "." segments removed.
  public static string NormalizePath(string path) => string.Join(
      '/', path.Replace('\\', '/').Split('/').Where(s => s is not ("" or ".")));

  private static void AddGlobs(
      JsonObject entry, string key, IEnumerable<string> globs, List<string>? notes, string field) {
    JsonArray array = [];
    foreach (string glob in globs) {
      if (TryParseGlob(glob) is null) {
        notes?.Add($"{field}: the pattern '{glob}' is not a valid glob of "
            + $"{PackageFormat.FileName}; it was left out.");
        continue;
      }

      array.Add(glob);
    }

    if (array.Count > 0) {
      entry[key] = array;
    }
  }

  /// The info of a package file for the info of format 3 (protocol/lip-compatibility.md,
  /// "tooth.json"): its members as they are, with "avatar_url" as "logo" when it starts with
  /// "https://" and dropped otherwise.
  public static JsonObject Info(PackageManifestInfo info) {
    JsonObject result = [];
    if (info.Name is not null) {
      result["name"] = info.Name;
    }

    if (info.Description is not null) {
      result["description"] = info.Description;
    }

    if (info.Tags is not null) {
      result["tags"] = new JsonArray([.. info.Tags.Select(t => (JsonNode)t)]);
    }

    if (info.AvatarUrl is not null && IsLogo(info.AvatarUrl)) {
      result["logo"] = info.AvatarUrl;
    }

    return result;
  }

  /// Whether an "avatar_url" of format 3 is a "logo" of a package file: it starts with
  /// "https://".
  public static bool IsLogo(string avatarUrl) =>
      avatarUrl.StartsWith("https://", StringComparison.Ordinal);

  private static void AddCommands(JsonObject hooks, string key, IReadOnlyList<string> commands) {
    if (commands.Count > 0) {
      hooks[key] = new JsonArray([.. commands.Select(c => (JsonNode)c)]);
    }
  }

  /// The preserve of a package file for the patterns of preserve_files of format 3
  /// (protocol/lip-compatibility.md, "tooth.json"): '\' becomes '/', and a pattern without '/' is
  /// prefixed by "**/".
  public static IEnumerable<string> PreserveFiles(IEnumerable<string> patterns) =>
      patterns
          .Select(p => p.Replace('\\', '/'))
          .Select(p => p.Contains('/') ? p : $"**/{p}");

  /// The purge of a package file for the patterns of remove_files of format 3
  /// (protocol/lip-compatibility.md, "tooth.json"): '\' becomes '/', and a pattern not ending with
  /// '/' is also given with a trailing '/'.
  public static IEnumerable<string> RemoveFiles(IEnumerable<string> patterns) =>
      patterns
          .Select(p => p.Replace('\\', '/'))
          .SelectMany(p => p.EndsWith('/') ? [p] : new[] { p, p + "/" });

  /// The hooks of the scripts of format 3 (protocol/lip-compatibility.md, "tooth.json"): "install"
  /// runs after the files were placed, together with "post_install", and "uninstall" before the
  /// files are removed, together with "pre_uninstall".
  public static Hooks ConvertScripts(PackageManifestScripts scripts) => new() {
    PreInstall = scripts.PreInstall,
    PostInstall = [.. scripts.Install, .. scripts.PostInstall],
    PreUninstall = [.. scripts.PreUninstall, .. scripts.Uninstall],
    PostUninstall = scripts.PostUninstall,
  };

  /// The glob of a converted pattern, or null for a pattern that is not a valid glob of a package
  /// file.
  public static PathGlob? TryParseGlob(string pattern) {
    try {
      return new PathGlob(pattern);
    }
    catch (FormatException) {
      return null;
    }
  }
}
