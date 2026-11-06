using System.Text.Json;
using System.Text.Json.Nodes;
using Lip.Core.Entities;
using Lip.Core.Json;

namespace Lip.Core.Mcmod.Compat;

/// Tooth files of earlier formats (protocol/package.md §5): a tooth file of format 1, 2 or 3
/// (1 and 2 are first read as 3 by PackageManifestMigration) is read, for an environment, as the
/// tooth file of format 4 that Section 5 derives from it (Derive). Nothing of format 3 is carried
/// beyond that: the result is an ordinary tooth file of format 4.
///
/// For each label L of the variants of the tooth file, the variants labeled L whose platform is
/// "" or matches the runtime are merged exactly like PackageManifest.GetVariant merges them for
/// lip's own runtime in lip v0.37.0, and become the single entry of the variant L, for the
/// platform and the runtime of the environment (ConvertEntry). On the client platform, a tooth
/// file with the label "client" has the variant "" derived from that label.
public static class ToothCompat {
  public const string ClientLabel = "client";

  /// The tooth file of format 4 that a tooth file of format 3 is read as for an environment
  /// (protocol/package.md §5), with the version of the package as its version: version, or else
  /// the version of the tooth file. Its entries are Derived, and their dependencies ClientCompat.
  /// Throws ToothFormatException when the variants of a label cannot be merged (lip v0.37.0 failed
  /// on them too), or when a derived entry is not valid in format 4.
  public static ToothFile Derive(
      PackageManifest manifest, LipEnvironment environment, Semver.SemVersion? version = null) {
    version ??= manifest.Version;
    Dictionary<string, JsonObject?> derived = new(StringComparer.Ordinal);
    foreach (string label in manifest.Variants.Select(v => v.Label).Distinct()) {
      PackageManifestVariant merged;
      try {
        merged = manifest.GetVariant(label, environment.Runtime);
      }
      catch (KeyNotFoundException) {
        continue;
      }
      catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) {
        throw new ToothFormatException(
            $"Invalid tooth.json: the variants labeled '{label}' cannot be merged: {ex.Message}",
            ex);
      }

      derived[label] = ConvertEntry(merged, environment.Platform, environment.Runtime);
    }

    if (environment.Platform == McmodPlatform.Client
        && manifest.Variants.Any(v => v.Label == ClientLabel)) {
      derived[""] = derived.TryGetValue(ClientLabel, out JsonObject? client)
          ? (JsonObject?)client?.DeepClone()
          : null;
    }

    JsonObject variants = [];
    foreach ((string label, JsonObject? entry) in derived) {
      if (entry is not null) {
        variants[label] = new JsonArray(entry);
      }
    }

    JsonObject document = new() {
      [ToothFormat.FormatVersionKey] = ToothFormat.FormatVersion,
      ["format_uuid"] = ToothFormat.FormatUuid,
      ["tooth"] = manifest.Path,
      ["version"] = version.ToString(),
    };
    if (manifest.Info is PackageManifestInfo info) {
      document["info"] = Info(info);
    }

    document["variants"] = variants;

    JsonElement json = JsonSerializer.SerializeToElement(document);
    Dictionary<string, IReadOnlyList<ToothEntry>> entries = new(StringComparer.Ordinal);
    foreach (JsonProperty variant in json.GetProperty("variants").EnumerateObject()) {
      ToothEntry entry = ToothParser.ParseEntry(variant.Value[0]);
      entries[variant.Name] = [entry with {
        Derived = true,
        Dependencies = [.. entry.Dependencies.Select(d => d with { ClientCompat = true })],
      }];
    }

    return new ToothFile {
      Tooth = manifest.Path,
      Version = version,
      Variants = entries,
      Json = json,
    };
  }

  /// The entry of format 4 for the variants of a label merged for a runtime (protocol/package.md
  /// §5), for a platform and the runtime; null when an asset has the type tar, tgz or
  /// uncompressed, which makes the variant absent. Placements and globs that are not valid in
  /// format 4 are left out; notes, when given, tell about them and about dropped variants, each
  /// prefixed by field.
  public static JsonObject? ConvertEntry(
      PackageManifestVariant merged,
      McmodPlatform platform,
      string runtime,
      List<string>? notes = null,
      string field = "") {
    JsonObject entry = new() {
      ["platform"] = platform.ToSpecString(),
      ["runtime"] = runtime,
    };

    if (merged.Dependencies.Count > 0) {
      JsonObject dependencies = [];
      foreach ((PackageId id, Semver.SemVersionRange range) in merged.Dependencies) {
        dependencies[id.ToString()] = range.ToString();
      }

      entry["dependencies"] = dependencies;
    }

    JsonArray assets = [];
    for (int i = 0; i < merged.Assets.Count; i++) {
      PackageManifestAsset asset = merged.Assets[i];
      if (asset.Type is not (PackageManifestAsset.AssetType.Self
          or PackageManifestAsset.AssetType.Zip)) {
        notes?.Add($"{field}.assets[{i}]: format 4 has no asset type "
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
        if (ToothParser.IsValidPlacement(src, dest)) {
          place.Add(new JsonObject { ["src"] = src, ["dest"] = dest });
        } else {
          notes?.Add($"{field}.assets[{i}]: the placement '{placement.Src}' -> '{placement.Dst}' "
              + $"becomes '{src}' -> '{dest}', which is not valid in format 4; it was left out.");
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

  /// The member of place for a placement of format 3 (protocol/package.md §5): '\' becomes '/',
  /// empty and "." segments are removed; a "dir" placement has src and dest each followed by '/'
  /// unless empty, a "file" placement whose src has '*', '?' or '[' has dest followed by '/'
  /// unless empty, any other "file" placement is the file form.
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
        notes?.Add($"{field}: the pattern '{glob}' is not a valid glob of format 4; it was left "
            + "out.");
        continue;
      }

      array.Add(glob);
    }

    if (array.Count > 0) {
      entry[key] = array;
    }
  }

  /// The info of format 4 for the info of format 3 (protocol/package.md §5): its members as they
  /// are, with "avatar_url" as "logo" when it starts with "https://" and dropped otherwise.
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

  /// Whether an "avatar_url" of format 3 is a "logo" of format 4: it starts with "https://".
  public static bool IsLogo(string avatarUrl) =>
      avatarUrl.StartsWith("https://", StringComparison.Ordinal);

  private static void AddCommands(JsonObject hooks, string key, IReadOnlyList<string> commands) {
    if (commands.Count > 0) {
      hooks[key] = new JsonArray([.. commands.Select(c => (JsonNode)c)]);
    }
  }

  /// The preserve of format 4 for the patterns of preserve_files of format 3 (protocol/package.md
  /// §5): '\' becomes '/', and a pattern without '/' is prefixed by "**/".
  public static IEnumerable<string> PreserveFiles(IEnumerable<string> patterns) =>
      patterns
          .Select(p => p.Replace('\\', '/'))
          .Select(p => p.Contains('/') ? p : $"**/{p}");

  /// The purge of format 4 for the patterns of remove_files of format 3 (protocol/package.md §5):
  /// '\' becomes '/', and a pattern not ending with '/' is also given with a trailing '/'.
  public static IEnumerable<string> RemoveFiles(IEnumerable<string> patterns) =>
      patterns
          .Select(p => p.Replace('\\', '/'))
          .SelectMany(p => p.EndsWith('/') ? [p] : new[] { p, p + "/" });

  /// The hooks of the scripts of format 3 (protocol/package.md §5): "install" runs after the files
  /// were placed, together with "post_install", and "uninstall" before the files are removed,
  /// together with "pre_uninstall".
  public static Hooks ConvertScripts(PackageManifestScripts scripts) => new() {
    PreInstall = scripts.PreInstall,
    PostInstall = [.. scripts.Install, .. scripts.PostInstall],
    PreUninstall = [.. scripts.PreUninstall, .. scripts.Uninstall],
    PostUninstall = scripts.PostUninstall,
  };

  /// The glob of a converted pattern, or null for a pattern that is not a valid glob of format 4.
  public static PathGlob? TryParseGlob(string pattern) {
    try {
      return new PathGlob(pattern);
    }
    catch (FormatException) {
      return null;
    }
  }
}
