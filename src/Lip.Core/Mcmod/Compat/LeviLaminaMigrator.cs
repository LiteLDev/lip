using System.Text.Json.Nodes;
using Lip.Core.Infrastructure;
using Semver;

namespace Lip.Core.Mcmod.Compat;

/// Converts a LeviLamina manifest (spec §10.1) into a manifest.json of format bf45b1 describing
/// the same mod (specs/cli.md §5.8, "From a LeviLamina manifest"). A package document has the
/// shape of a LeviLamina manifest, so the result is the manifest with "format" first. Values the
/// result needs that are missing or invalid (name, version, type, entry) are asked for through
/// IUserInteraction.Ask; the notes list what is dropped.
public static class LeviLaminaMigrator {
  public static async Task<ToothMigrator.Result> Migrate(JsonObject manifest, IUserInteraction ui) {
    List<string> notes = [];

    string? name = String(manifest, "name");
    string id = name is not null && PackageNames.IsValid(name)
        ? name
        : await ToothMigrator.AskId(ui, name is null
            ? "the package name (manifest.json has no \"name\")"
            : $"the package name ('{name}' is not a valid id)");

    string? versionText = String(manifest, "version");
    string version = versionText is not null && IsSemVer(versionText)
        ? versionText
        : await ToothMigrator.AskValid(
            ui,
            versionText is null
                ? "the package version (manifest.json has no \"version\")"
                : $"the package version ('{versionText}' is not a SemVer 2.0.0 version)",
            a => IsSemVer(a) ? null : "enter a SemVer 2.0.0 version, e.g. 1.0.0.");

    string type = String(manifest, "type") is string { Length: > 0 } t
        ? t
        : await ToothMigrator.AskValid(
            ui, "the mod type (manifest.json has no \"type\"), e.g. native",
            a => a != "" ? null : "enter the LeviLamina mod type.");

    string entry = String(manifest, "entry") is string { Length: > 0 } e
        ? e
        : await ToothMigrator.AskValid(
            ui, "the mod entry (manifest.json has no \"entry\"), e.g. MyMod.dll",
            a => a != "" ? null : "enter the entry file of the mod.");

    JsonObject document = new() { [ManifestFormat.FormatKey] = ManifestFormat.Format };
    foreach ((string key, JsonNode? value) in manifest) {
      document[key] = value?.DeepClone();
    }

    document["name"] = id;
    document["version"] = version;
    document["type"] = type;
    document["entry"] = entry;

    if (manifest.ContainsKey("platform") && String(manifest, "platform") is not ("server" or "client")) {
      document.Remove("platform");
      notes.Add("platform: it is neither \"server\" nor \"client\" and was dropped.");
    }

    foreach (string key in new[] { "dependencies", "conflicts" }) {
      if (manifest.ContainsKey(key)) {
        document[key] = Entries(manifest, key, notes);
      }
    }

    return new ToothMigrator.Result(document, notes);
  }

  private static string? String(JsonObject manifest, string key) =>
      manifest[key] is JsonValue value && value.TryGetValue(out string? s) ? s : null;

  private static bool IsSemVer(string text) =>
      SemVersion.TryParse(text, SemVersionStyles.Strict, out _);

  /// The {name, version?} entries of "dependencies" or "conflicts" that manifest.json accepts:
  /// an entry whose name is not an id is dropped, and a version that is not a node-semver range
  /// is removed.
  private static JsonArray Entries(JsonObject manifest, string key, List<string> notes) {
    JsonArray entries = [];
    if (manifest[key] is not JsonArray array) {
      notes.Add($"{key}: it is not an array; it was dropped.");
      return entries;
    }

    for (int i = 0; i < array.Count; i++) {
      string field = $"{key}[{i}]";
      if (array[i] is not JsonObject entry) {
        notes.Add($"{field}: it is not an object; it was dropped.");
        continue;
      }

      string? name = String(entry, "name");
      if (name is null || !PackageNames.IsValid(name)) {
        notes.Add($"{field}: '{name}' is not a valid id; the entry was dropped.");
        continue;
      }

      JsonObject converted = (JsonObject)entry.DeepClone();
      if (entry.ContainsKey("version")
          && !(String(entry, "version") is string version && VersionRange.TryParse(version, out _))) {
        converted.Remove("version");
        notes.Add($"{field}.version: '{entry["version"]}' is not a node-semver range; any version "
            + "is accepted now.");
      }

      entries.Add(converted);
    }

    return entries;
  }
}
