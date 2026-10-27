using System.Text;
using System.Text.Json.Nodes;
using Lip.Core.Entities;
using Lip.Core.Json;
using Semver;

namespace Lip.Core.Mcmod.Compat;

/// Converts a tooth.json (v1–v3, already migrated to v3) into a format 4 manifest.json file, as
/// faithfully as the formats allow. It is a helper for package authors, not a guarantee: the
/// notes list everything that needs a manual decision. lip itself does not need the file: it
/// converts tooth.json archives in memory (ToothCompat, spec §9), with the same mapping where
/// manifest.json can express it.
///
/// - One variant without a label whose assets are all "self": a concrete file package
///   ("type": "files") with the variant's placements, dependencies (as installDependencies),
///   preserve_files, remove_files and scripts, mapped like ToothCompat maps them.
/// - Anything else: a routing document with one artifact per variant. Each artifact points to
///   where the concrete package of that variant would be published (a GitHub release asset
///   for github.com paths, a directory of the repository otherwise); creating those packages
///   is left to the author.
public static class ToothMigrator {
  public record Result(JsonObject Document, IReadOnlyList<string> Notes);

  public static Result Migrate(PackageManifest tooth) {
    List<string> notes = [];
    string name = PackageName(tooth.Path);
    if (name != tooth.Path.Split('/')[^1]) {
      notes.Add($"The package name '{name}' was derived from '{tooth.Path}'; check it.");
    }

    PackageManifestVariant? single = tooth.Variants.Count == 1 ? tooth.Variants[0] : null;
    JsonObject document = single is not null && single.Label == ""
        && single.Assets.All(a => a.Type == PackageManifestAsset.AssetType.Self)
        ? Concrete(tooth, name, single, notes)
        : Routing(tooth, name, notes);

    if (tooth.Info.Name != "" || tooth.Info.Description != "" || tooth.Info.Tags.Count > 0
        || tooth.Info.AvatarUrl != "") {
      notes.Add("The info section (name, description, tags, avatar_url) has no equivalent in "
          + "manifest.json and was dropped.");
    }

    try {
      new DocumentParser().Parse(Encoding.UTF8.GetBytes(document.ToJsonString()));
    }
    catch (ManifestFormatException ex) {
      notes.Add($"The result is not a valid manifest.json yet: {ex.Message}");
    }

    return new Result(document, notes);
  }

  private static JsonObject Concrete(
      PackageManifest tooth, string name, PackageManifestVariant variant, List<string> notes) {
    JsonObject document = Header(tooth, name);
    document["type"] = "files";

    if (variant.Platform != "") {
      if (IsGlob(variant.Platform)) {
        notes.Add($"The platform pattern '{variant.Platform}' cannot be expressed as a rid; "
            + "the package is not restricted to a rid.");
      } else {
        document["rid"] = variant.Platform;
      }
    }

    document["refs"] = new JsonArray(tooth.Path);

    JsonArray installDependencies = [];
    foreach ((PackageId id, SemVersionRange range) in variant.Dependencies) {
      JsonObject dependency = [];
      if (ConvertRange(range, notes, id.ToString()) is string version) {
        dependency["version"] = version;
      }

      dependency["refs"] = new JsonArray(id.ToString());
      installDependencies.Add(dependency);

      if (id.Variant == ToothCompat.ClientLabel) {
        notes.Add($"The dependency {id} keeps its \"client\" variant. tooth.json mapped it to the "
            + "default variant of a package that uses manifest.json (spec §9.3); manifest.json "
            + "does not, so drop \"#client\" once that package has moved to manifest.json.");
      }
    }

    if (installDependencies.Count > 0) {
      document["installDependencies"] = installDependencies;
    }

    JsonArray placements = [];
    foreach (PackageManifestAssetPlacement placement in variant.Assets.SelectMany(a => a.Placements)) {
      if (Placement(placement, notes) is JsonObject converted) {
        placements.Add(converted);
      }
    }

    document["placements"] = placements;

    AddGlobs(document, "preserveFiles", ToothCompat.PreserveFiles(
        variant.PreserveFiles.Select(GlobListJsonConverter.Format)), notes);
    AddGlobs(document, "removeFiles", ToothCompat.RemoveFiles(
        variant.RemoveFiles.Select(GlobListJsonConverter.Format)), notes);

    Scripts convertedScripts = ToothCompat.ConvertScripts(variant.Scripts);
    JsonObject scripts = [];
    AddScripts(scripts, "preInstall", convertedScripts.PreInstall);
    AddScripts(scripts, "postInstall", convertedScripts.PostInstall);
    AddScripts(scripts, "preUninstall", convertedScripts.PreUninstall);
    AddScripts(scripts, "postUninstall", convertedScripts.PostUninstall);
    if (scripts.Count > 0) {
      document["scripts"] = scripts;
    }

    return document;
  }

  private static JsonObject Routing(PackageManifest tooth, string name, List<string> notes) {
    JsonObject document = Header(tooth, name);
    document["refs"] = new JsonArray(tooth.Path);

    bool github = tooth.Path.StartsWith("github.com/", StringComparison.Ordinal)
        && tooth.Path.Split('/').Length == 3;

    // A tooth.json whose only labels are "" and "client" selects "client" on client platforms
    // (spec §9.2): that is a platform selector in manifest.json.
    HashSet<string> labels = [.. tooth.Variants.Select(v => v.Label)];
    bool byPlatform = labels.SetEquals(["", ToothCompat.ClientLabel]);
    if (byPlatform) {
      notes.Add("The unlabeled and \"client\" variants became server and client artifacts of the "
          + "default variant.");
    }

    JsonArray artifacts = [];
    foreach (PackageManifestVariant variant in tooth.Variants) {
      JsonObject artifact = [];
      string label = variant.Label == "" ? "default" : variant.Label.Replace('/', '-');
      if (byPlatform) {
        label = variant.Label == "" ? "server" : "client";
        artifact["platform"] = label;
      } else if (variant.Label != "") {
        artifact["variant"] = variant.Label;
      }

      if (variant.Platform != "") {
        if (IsGlob(variant.Platform)) {
          notes.Add($"Variant '{variant.Label}': the platform pattern '{variant.Platform}' "
              + "cannot be expressed as a rid; the artifact is not restricted to a rid.");
        } else {
          artifact["rid"] = variant.Platform;
        }
      }

      string reference = github
          ? $"https://{tooth.Path}/releases/download/v{{{{version}}}}/{{{{name}}}}-"
              + (byPlatform ? "{{platform}}" : label)
              + (artifact.ContainsKey("rid") ? "-{{rid}}" : "") + ".mcmod"
          : $"./{label}" + (artifact.ContainsKey("rid") ? "-{{rid}}" : "") + "/";
      artifact["refs"] = new JsonArray(reference);
      artifacts.Add(artifact);

      notes.Add($"Variant '{variant.Label}'"
          + (variant.Platform == "" ? "" : $" ({variant.Platform})")
          + $" must become a concrete package at {reference}: a manifest.json with its files"
          + (variant.Dependencies.Count == 0 ? "" : ", and its dependencies ("
              + string.Join(", ", variant.Dependencies.Select(d => $"{d.Key} {d.Value}"))
              + ") as installDependencies")
          + ".");
    }

    if (artifacts.Count == 0) {
      notes.Add("The tooth.json has no variants; add at least one artifact.");
    }

    document["artifacts"] = artifacts;
    return document;
  }

  private static JsonObject Header(PackageManifest tooth, string name) => new() {
    [ManifestFormat.FormatVersionKey] = ManifestFormat.Version,
    [ManifestFormat.FormatUuidKey] = ManifestFormat.Uuid,
    ["name"] = name,
    ["version"] = tooth.Version.ToString(),
  };

  private static JsonObject? Placement(PackageManifestAssetPlacement placement, List<string> notes) {
    string src = placement.Src.Replace('\\', '/');
    string dest = placement.Dst.Replace('\\', '/');

    if (placement.Type == PackageManifestAssetPlacement.PlacementType.Directory) {
      return new JsonObject {
        ["src"] = DirectoryForm(src),
        ["dest"] = DirectoryForm(dest),
      };
    }

    if (IsGlob(src)) {
      notes.Add($"The file placement '{placement.Src}' -> '{placement.Dst}' uses a pattern, "
          + "which manifest.json placements do not support; list the files instead.");
      return null;
    }

    return new JsonObject { ["src"] = src.TrimStart('/'), ["dest"] = dest.TrimStart('/') };
  }

  private static string DirectoryForm(string path) {
    string trimmed = path.Trim('/');
    return trimmed is "" or "." ? "" : trimmed + "/";
  }

  private static void AddGlobs(
      JsonObject document, string key, IEnumerable<string> globs, List<string> notes) {
    JsonArray array = [];
    foreach (string glob in globs) {
      if (ToothCompat.TryParseGlob(glob) is null) {
        notes.Add($"The {key} pattern '{glob}' can never match and was left out.");
        continue;
      }

      if (glob.IndexOfAny(['?', '[', '{', '!']) >= 0) {
        notes.Add($"The {key} pattern '{glob}' uses syntax beyond '*' and '**'; check it.");
      }

      array.Add(glob);
    }

    if (array.Count > 0) {
      document[key] = array;
    }
  }

  private static void AddScripts(JsonObject scripts, string key, IReadOnlyList<string> commands) {
    if (commands.Count > 0) {
      scripts[key] = new JsonArray([.. commands.Select(c => (JsonNode)c)]);
    }
  }

  /// Converts a v3 range to the LeviLamina syntax (spec §4.2), or returns null with a note when
  /// it cannot be expressed.
  private static string? ConvertRange(SemVersionRange range, List<string> notes, string id) {
    if (range.Equals(SemVersionRange.All) || range.Equals(SemVersionRange.AllRelease)) {
      return null;
    }

    string text = range.ToString().Replace(" || ", "||");
    foreach (string candidate in new[] { text, $"={text}" }) {
      try {
        VersionRange converted = VersionRange.Parse(candidate);
        return converted.Text;
      }
      catch (FormatException) {
        // Try the next form.
      }
    }

    notes.Add($"The version range '{range}' of {id} could not be converted; it was left out.");
    return null;
  }

  private static bool IsGlob(string s) => s.IndexOfAny(['*', '?', '[', '{']) >= 0;

  /// A valid package name (spec §4.2) from the last path segment.
  private static string PackageName(string path) =>
      PackageNames.FromText(path.Split('/')[^1], "package");
}
