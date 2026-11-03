using System.Text.Json;
using System.Text.Json.Nodes;
using Lip.Core.Infrastructure;
using Semver;

namespace Lip.Core.Mcmod.Compat;

/// Converts a LeviLamina manifest (spec §10.1) into a manifest.json of format bf45b1 describing
/// the same mod (specs/cli.md §5.8, "From a LeviLamina manifest"). Values the result needs that are
/// missing or invalid (id, version, mod.loader, mod.entry) are asked for through
/// IUserInteraction.Ask; the notes list what is dropped or converted with another meaning.
public static class LeviLaminaMigrator {
  /// The LeviLamina "loadBefore" name standing for every mod.
  public const string BeforeAllMods = "__before_all_mods__";

  private static readonly HashSet<string> _known = [
    "name", "version", "platform", "description", "author", "type", "entry", "passive",
    "extraInfo", "dependencies", "conflicts", "optionalDependencies", "loadAfter", "loadBefore",
  ];

  public static async Task<ToothMigrator.Result> Migrate(JsonObject manifest, IUserInteraction ui) {
    List<string> notes = [];

    string? name = String(manifest, "name");
    string id = name is not null && PackageNames.IsValid(name)
        ? name
        : await ToothMigrator.AskId(ui, name is null
            ? "the package id (manifest.json has no \"name\")"
            : $"the package id ('{name}' is not a valid id)");

    string? versionText = String(manifest, "version");
    string version = versionText is not null && IsSemVer(versionText)
        ? versionText
        : await ToothMigrator.AskValid(
            ui,
            versionText is null
                ? "the package version (manifest.json has no \"version\")"
                : $"the package version ('{versionText}' is not a SemVer 2.0.0 version)",
            a => IsSemVer(a) ? null : "enter a SemVer 2.0.0 version, e.g. 1.0.0.");

    JsonObject document = new() {
      [ManifestFormat.FormatKey] = ManifestFormat.Format,
      ["id"] = id,
      ["version"] = version,
    };

    if (String(manifest, "platform") is "server" or "client") {
      document["platform"] = String(manifest, "platform");
    } else if (manifest.ContainsKey("platform")) {
      notes.Add("platform: it is neither \"server\" nor \"client\" and was dropped.");
    }

    JsonObject info = [];
    if (String(manifest, "description") is string description) {
      info["description"] = description;
    }

    if (String(manifest, "author") is string author) {
      info["authors"] = new JsonArray(author);
    }

    if (info.Count > 0) {
      document["info"] = info;
    }

    JsonObject dependencies = Map(manifest, "dependencies", notes);
    if (dependencies.Count > 0) {
      document["dependencies"] = dependencies;
    }

    JsonObject conflicts = Map(manifest, "conflicts", notes);
    if (conflicts.Count > 0) {
      document["conflicts"] = conflicts;
    }

    JsonObject mod = new() {
      ["loader"] = String(manifest, "type") is string { Length: > 0 } type
          ? type
          : await ToothMigrator.AskValid(
              ui, "the mod loader (manifest.json has no \"type\"), e.g. native",
              a => a != "" ? null : "enter the LeviLamina mod type."),
      ["entry"] = String(manifest, "entry") is string { Length: > 0 } entry
          ? entry
          : await ToothMigrator.AskValid(
              ui, "the mod entry (manifest.json has no \"entry\"), e.g. MyMod.dll",
              a => a != "" ? null : "enter the entry file of the mod."),
    };

    if (manifest["passive"] is JsonValue passive
        && passive.GetValueKind() is JsonValueKind.True or JsonValueKind.False) {
      mod["passive"] = passive.DeepClone();
    }

    JsonArray loadAfter = Names(manifest, ["optionalDependencies", "loadAfter"], notes, null);
    if (loadAfter.Count > 0) {
      mod["loadAfter"] = loadAfter;
    }

    JsonArray loadBefore = Names(manifest, ["loadBefore"], notes, BeforeAllMods);
    if (loadBefore.Count > 0) {
      mod["loadBefore"] = loadBefore;
    }

    JsonObject extra = manifest["extraInfo"] is JsonObject extraInfo
        ? (JsonObject)extraInfo.DeepClone()
        : [];
    foreach ((string key, JsonNode? value) in manifest) {
      if (_known.Contains(key)) {
        continue;
      }

      if (extra.ContainsKey(key)) {
        notes.Add($"{key}: extraInfo already has \"{key}\"; this key was dropped.");
      } else {
        extra[key] = value?.DeepClone();
        notes.Add($"{key}: moved into mod.extra.");
      }
    }

    if (extra.Count > 0) {
      mod["extra"] = extra;
    }

    document["mod"] = mod;
    return new ToothMigrator.Result(document, notes);
  }

  private static string? String(JsonObject manifest, string key) =>
      manifest[key] is JsonValue value && value.TryGetValue(out string? s) ? s : null;

  private static bool IsSemVer(string text) =>
      SemVersion.TryParse(text, SemVersionStyles.Strict, out _);

  /// "dependencies" or "conflicts": each {name, version?} entry as name mapped to its version, or
  /// "*" when it has none or it is not a node-semver range.
  private static JsonObject Map(JsonObject manifest, string key, List<string> notes) {
    JsonObject map = [];
    foreach ((JsonObject entry, string field) in Entries(manifest, key, notes)) {
      if (!Name(entry, field, notes, out string? name)) {
        continue;
      }

      string range = "*";
      if (entry["version"] is JsonValue value && value.TryGetValue(out string? version)) {
        if (VersionRange.TryParse(version, out _)) {
          range = version;
        } else {
          notes.Add($"{field}.version: '{version}' is not a node-semver range; any version is "
              + "accepted now.");
        }
      }

      if (!map.TryAdd(name!, range)) {
        notes.Add($"{field}: '{name}' appears twice; the later entry was dropped.");
      }
    }

    return map;
  }

  /// The names of the entries of the keys, without duplicates and versions; sentinel becomes
  /// "*".
  private static JsonArray Names(
      JsonObject manifest, string[] keys, List<string> notes, string? sentinel) {
    List<string> names = [];
    foreach (string key in keys) {
      foreach ((JsonObject entry, string field) in Entries(manifest, key, notes)) {
        string? name = entry["name"] is JsonValue value && value.TryGetValue(out string? s)
            ? s
            : null;
        if (name is not null && name == sentinel) {
          name = "*";
        } else if (!Name(entry, field, notes, out name)) {
          continue;
        }

        if (entry.ContainsKey("version")) {
          notes.Add($"{field}.version: the load order takes no version; it was dropped.");
        }

        if (!names.Contains(name!)) {
          names.Add(name!);
        }
      }
    }

    return [.. names.Select(n => (JsonNode)n)];
  }

  private static bool Name(JsonObject entry, string field, List<string> notes, out string? name) {
    name = entry["name"] is JsonValue value && value.TryGetValue(out string? s) ? s : null;
    if (name is null || !PackageNames.IsValid(name)) {
      notes.Add($"{field}: '{name}' is not a valid id; the entry was dropped.");
      return false;
    }

    return true;
  }

  private static IEnumerable<(JsonObject Entry, string Field)> Entries(
      JsonObject manifest, string key, List<string> notes) {
    if (!manifest.TryGetPropertyValue(key, out JsonNode? node)) {
      yield break;
    }

    if (node is not JsonArray array) {
      notes.Add($"{key}: it is not an array; it was dropped.");
      yield break;
    }

    for (int i = 0; i < array.Count; i++) {
      if (array[i] is JsonObject entry) {
        yield return (entry, $"{key}[{i}]");
      } else {
        notes.Add($"{key}[{i}]: it is not an object; it was dropped.");
      }
    }
  }
}
