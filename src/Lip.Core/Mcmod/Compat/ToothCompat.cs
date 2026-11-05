using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Lip.Core.Entities;
using Lip.Core.Json;

namespace Lip.Core.Mcmod.Compat;

/// Tooth files of earlier formats (protocol/package.md §5): a tooth file of format 1, 2 or 3
/// (1 and 2 are first read as 3 by PackageManifestMigration) is read, for an environment, as the
/// tooth file of format 4 that Section 5 derives from it (Derive).
///
/// For each label L of the variants of the tooth file, the variants labeled L whose platform is
/// "" or matches the runtime are merged exactly like PackageManifest.GetVariant merges them for
/// lip's own runtime in lip v0.37.0, and become the single entry of the variant L, for the
/// platform and the runtime of the environment. Its assets, preserve_files and remove_files keep their meaning in
/// lip v0.37.0 (the entry is Legacy: see ToothEntry), and its scripts become hooks
/// (ConvertScripts). On the client platform, a tooth file with the label "client" has the variant
/// "" derived from that label.
public static partial class ToothCompat {
  public const string ClientLabel = "client";

  /// The tooth file of format 4 that a tooth file of format 3 is read as for an environment
  /// (protocol/package.md §5). Throws ToothFormatException when the variants of a label cannot be
  /// merged (lip v0.37.0 failed on them too).
  public static ToothFile Derive(PackageManifest manifest, LipEnvironment environment) {
    Dictionary<string, JsonObject> derived = new(StringComparer.Ordinal);
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

      derived[label] = Entry(merged, environment);
    }

    if (environment.Platform == McmodPlatform.Client
        && manifest.Variants.Any(v => v.Label == ClientLabel)) {
      if (derived.TryGetValue(ClientLabel, out JsonObject? client)) {
        derived[""] = (JsonObject)client.DeepClone();
      } else {
        derived.Remove("");
      }
    }

    JsonObject variants = [];
    foreach ((string label, JsonObject entry) in derived) {
      variants[label] = new JsonArray(entry);
    }

    JsonObject document = new() {
      [ToothFormat.FormatVersionKey] = ToothFormat.FormatVersion,
      ["format_uuid"] = ToothFormat.FormatUuid,
      ["tooth"] = manifest.Path,
      ["version"] = manifest.Version.ToString(),
      ["info"] = JsonSerializer.SerializeToNode(manifest.Info),
      ["variants"] = variants,
    };

    JsonElement json = JsonSerializer.SerializeToElement(document);
    return new ToothFile {
      Tooth = manifest.Path,
      Version = manifest.Version,
      Variants = derived.ToDictionary(
          d => d.Key,
          d => (IReadOnlyList<ToothEntry>)[ToothParser.ParseEntry(
              json.GetProperty("variants").GetProperty(d.Key)[0], legacy: true)],
          StringComparer.Ordinal),
      Json = json,
    };
  }

  /// The derived entry of a merged variant, as JSON (protocol/package.md §5).
  private static JsonObject Entry(PackageManifestVariant merged, LipEnvironment environment) {
    JsonObject entry = new() {
      ["platform"] = environment.Platform.ToSpecString(),
      ["runtime"] = environment.Runtime,
    };

    if (merged.Dependencies.Count > 0) {
      JsonObject dependencies = [];
      foreach ((PackageId id, Semver.SemVersionRange range) in merged.Dependencies) {
        dependencies[id.ToString()] = range.ToString();
      }

      entry["dependencies"] = dependencies;
    }

    if (merged.Assets.Count > 0) {
      entry["assets"] = JsonSerializer.SerializeToNode(merged.Assets);
    }

    if (merged.PreserveFiles.Count > 0) {
      entry["preserve_files"] = new JsonArray(
          [.. merged.PreserveFiles.Select(g => (JsonNode)GlobListJsonConverter.Format(g))]);
    }

    if (merged.RemoveFiles.Count > 0) {
      entry["remove_files"] = new JsonArray(
          [.. merged.RemoveFiles.Select(g => (JsonNode)GlobListJsonConverter.Format(g))]);
    }

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

  private static void AddCommands(JsonObject hooks, string key, IReadOnlyList<string> commands) {
    if (commands.Count > 0) {
      hooks[key] = new JsonArray([.. commands.Select(c => (JsonNode)c)]);
    }
  }

  /// The tooth name of a tooth path (protocol/package.md §1): its last '/'-separated element, or
  /// the one before it when the last is "v" followed by one or more ASCII digits.
  public static string GetToothName(string tooth) {
    string[] elements = tooth.Split('/');
    return elements.Length > 1 && MajorVersionSuffix().IsMatch(elements[^1])
        ? elements[^2]
        : elements[^1];
  }

  [GeneratedRegex(@"^v[0-9]+\z")]
  private static partial Regex MajorVersionSuffix();

  /// The globs that patterns of preserve_files of format 3 stand for. lip v0.37.0 matched a
  /// pattern against the path relative to the workspace root and against the bare file name, so a
  /// pattern without '/' matches the file name in every directory ("**/" is prepended). '\'
  /// becomes '/'.
  public static IEnumerable<string> PreserveFiles(IEnumerable<string> patterns) =>
      patterns
          .Select(p => p.Replace('\\', '/'))
          .Select(p => p.Contains('/') ? p : $"**/{p}");

  /// The globs that patterns of remove_files of format 3 stand for. lip v0.37.0 matched a pattern
  /// against both files and directories (a directory also with a trailing '/'), and removed a
  /// matched directory with everything in it, so a pattern without a trailing '/' matches files
  /// and directories (both forms are returned). '\' becomes '/'.
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

  /// The glob of a converted pattern, or null for a pattern that is not a valid glob (empty
  /// segments, a leading '/', an incomplete bracket expression), which matches nothing.
  public static PathGlob? TryParseGlob(string pattern) {
    try {
      return new PathGlob(pattern);
    }
    catch (FormatException) {
      return null;
    }
  }
}
