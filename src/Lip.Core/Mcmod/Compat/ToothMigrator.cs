using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Lip.Core.Entities;
using Lip.Core.Json;
using Semver;

namespace Lip.Core.Mcmod.Compat;

/// Converts a tooth.json (v1–v3, already migrated to v3) into a format 4 manifest.json document,
/// as faithfully as the formats allow. It is a helper for package authors, not a guarantee: the
/// notes list everything that needs a manual decision, each naming the tooth.json field it is
/// about (in its v3 form). lip itself does not need the document: it converts tooth.json archives
/// in memory (ToothCompat, spec §9), with the same mapping where manifest.json can express it.
/// ToothRepositoryMigrator writes the document into a package repository.
///
/// - One variant without a label whose assets are all "self": a concrete file package
///   ("type": "files") with the variant's placements, dependencies (as installDependencies),
///   preserve_files, remove_files and scripts, mapped like ToothCompat maps them.
/// - Anything else: a routing document with one artifact per variant. Each artifact points to
///   where the concrete package of that variant would be published (a GitHub release asset
///   for github.com paths, a directory of the repository otherwise); creating those packages
///   is left to the author.
public static partial class ToothMigrator {
  public record Result(JsonObject Document, IReadOnlyList<string> Notes);

  private static readonly string[] _knownScripts = [
    "pre_install", "install", "post_install", "pre_uninstall", "uninstall", "post_uninstall",
  ];

  /// selfFiles, when given, returns the '/'-separated paths of the files of the tooth.json's own
  /// archive (its "self" asset); it is called only for a concrete document. Each placement is
  /// then checked against what lip v3 placed from those files (ToothPlacements.Match): one that
  /// placed nothing is left out (manifest.json would fail on it), and one whose manifest.json
  /// form would place other files (e.g. a pattern) is expanded into one file placement per file.
  /// Notes tell about both.
  public static Result Migrate(
      PackageManifest tooth, Func<IReadOnlyList<string>>? selfFiles = null) {
    List<string> notes = [];
    string name = PackageName(tooth.Path);
    if (name != tooth.Path.Split('/')[^1]) {
      notes.Add($"tooth: the package name '{name}' was derived from '{tooth.Path}'; check it.");
    }

    PackageManifestVariant? single = tooth.Variants.Count == 1 ? tooth.Variants[0] : null;
    JsonObject document = single is not null && single.Label == ""
        && single.Assets.All(a => a.Type == PackageManifestAsset.AssetType.Self)
        ? Concrete(tooth, name, single, selfFiles, notes)
        : Routing(tooth, name, notes);

    if (tooth.Info.Name != "" || tooth.Info.Description != "" || tooth.Info.Tags.Count > 0
        || tooth.Info.AvatarUrl != "") {
      notes.Add("info: the info section (name, description, tags, avatar_url) has no equivalent "
          + "in manifest.json and was dropped.");
    }

    return new Result(document, notes);
  }

  /// Notes for the fields of a raw tooth.json that its v3 form (tooth, migrated from root) does
  /// not keep and that therefore cannot reach the migrated document: v1 and v2 dependencies whose
  /// range the migration to v3 could not read, v2 prerequisites, and v3 scripts other than the six
  /// install and uninstall hooks (e.g. pre_pack, or custom scripts).
  public static IReadOnlyList<string> DroppedFields(JsonElement root, PackageManifest tooth) {
    List<string> notes = [];
    if (root.ValueKind != JsonValueKind.Object
        || !root.TryGetProperty("format_version", out JsonElement formatVersion)
        || formatVersion.ValueKind != JsonValueKind.Number) {
      return notes;
    }

    switch (formatVersion.GetInt32()) {
      case 1 or 2:
        HashSet<string> kept = [
          .. tooth.Variants.SelectMany(v => v.Dependencies.Keys).Select(id => id.ToString()),
        ];
        // As in the migration to v3: a v2 platform with its own dependencies uses them instead
        // of the root ones.
        static JsonElement? Dependencies(JsonElement e) =>
            e.ValueKind == JsonValueKind.Object
            && e.TryGetProperty("dependencies", out JsonElement d)
            && d.ValueKind == JsonValueKind.Object ? d : null;
        JsonElement? rootDependencies = Dependencies(root);
        IEnumerable<JsonElement?> effective =
            root.TryGetProperty("platforms", out JsonElement p)
            && p.ValueKind == JsonValueKind.Array && p.GetArrayLength() > 0
                ? p.EnumerateArray().Select(e => Dependencies(e) ?? rootDependencies)
                : [rootDependencies];
        IEnumerable<string> dependencies = effective
            .OfType<JsonElement>()
            .SelectMany(d => d.EnumerateObject().Select(e => e.Name))
            .Distinct();
        foreach (string dependency in dependencies.Where(d => !kept.Contains(d))) {
          notes.Add($"dependencies[\"{dependency}\"]: its range cannot be read in tooth.json "
              + "format 3, so it was dropped (lip installs this tooth.json without it, too); add "
              + "it to installDependencies by hand.");
        }

        if (root.TryGetProperty("prerequisites", out JsonElement prerequisites)
            && prerequisites.ValueKind == JsonValueKind.Object
            && prerequisites.EnumerateObject().Any()) {
          notes.Add("prerequisites: has no equivalent in manifest.json and was dropped; declare "
              + "the packages that must already be installed as dependencies instead.");
        }

        break;

      case 3:
        if (!root.TryGetProperty("variants", out JsonElement variants)
            || variants.ValueKind != JsonValueKind.Array) {
          break;
        }

        int i = 0;
        foreach (JsonElement variant in variants.EnumerateArray()) {
          if (variant.ValueKind == JsonValueKind.Object
              && variant.TryGetProperty("scripts", out JsonElement scripts)
              && scripts.ValueKind == JsonValueKind.Object) {
            foreach (JsonProperty script in scripts.EnumerateObject()) {
              bool empty = script.Value.ValueKind == JsonValueKind.Array
                  && script.Value.GetArrayLength() == 0;
              if (!_knownScripts.Contains(script.Name) && !empty) {
                notes.Add($"variants[{i}].scripts.{script.Name}: manifest.json only has install "
                    + "and uninstall scripts; it was dropped.");
              }
            }
          }

          i++;
        }

        break;
    }

    return notes;
  }

  /// A note when utf8Json is not a valid manifest.json, else null.
  public static string? ValidationNote(byte[] utf8Json) {
    try {
      new DocumentParser().Parse(utf8Json);
      return null;
    }
    catch (ManifestFormatException ex) {
      return $"The result is not a valid manifest.json yet: {ex.Message}";
    }
  }

  private static JsonObject Concrete(
      PackageManifest tooth,
      string name,
      PackageManifestVariant variant,
      Func<IReadOnlyList<string>>? selfFiles,
      List<string> notes) {
    const string Field = "variants[0]";
    IReadOnlyList<string>? files = selfFiles?.Invoke();
    JsonObject document = Header(tooth, name);
    document["type"] = "files";

    if (variant.Platform != "") {
      if (IsGlob(variant.Platform)) {
        notes.Add($"{Field}.platform: the pattern '{variant.Platform}' cannot be expressed as a "
            + "rid; the package is not restricted to a rid.");
      } else {
        document["rid"] = variant.Platform;
      }
    }

    document["refs"] = new JsonArray(tooth.Path);

    JsonArray installDependencies = [];
    foreach ((PackageId id, SemVersionRange range) in variant.Dependencies) {
      string field = $"{Field}.dependencies[\"{id}\"]";
      JsonObject dependency = [];
      if (ConvertRange(range, notes, field) is string version) {
        dependency["version"] = version;
      }

      dependency["refs"] = new JsonArray(id.ToString());
      installDependencies.Add(dependency);

      if (id.Variant == ToothCompat.ClientLabel) {
        notes.Add($"{field}: keeps its \"client\" variant. tooth.json mapped it to the default "
            + "variant of a package that uses manifest.json (spec §9.3); manifest.json does not, "
            + "so drop \"#client\" once that package has moved to manifest.json.");
      }
    }

    if (installDependencies.Count > 0) {
      document["installDependencies"] = installDependencies;
    }

    JsonArray placements = [];
    foreach (PackageManifestAssetPlacement placement in variant.Assets.SelectMany(a => a.Placements)) {
      foreach (JsonObject converted in Placements(placement, files, notes, $"{Field}.assets")) {
        placements.Add(converted);
      }
    }

    document["placements"] = placements;

    List<string> preserveFiles = [.. variant.PreserveFiles.Select(GlobListJsonConverter.Format)];
    foreach (string pattern in preserveFiles.Where(p => !p.Replace('\\', '/').Contains('/'))) {
      notes.Add($"{Field}.preserve_files: '{pattern}' has no '/', so tooth.json also matched it "
          + $"against file names in every directory; it became '**/{pattern}'.");
    }

    AddGlobs(document, "preserveFiles", ToothCompat.PreserveFiles(preserveFiles), notes,
        $"{Field}.preserve_files");
    AddGlobs(document, "removeFiles", ToothCompat.RemoveFiles(
        variant.RemoveFiles.Select(GlobListJsonConverter.Format)), notes,
        $"{Field}.remove_files");

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
      notes.Add("variants: the unlabeled and \"client\" variants became server and client "
          + "artifacts of the default variant.");
    } else if (tooth.Variants.FirstOrDefault(v => v.Label != "") is { } labeled) {
      // §9.1: tooth.json gave each labelled variant an identity of its own.
      notes.Add($"variants: lip installs each labeled variant of a tooth.json as a package of "
          + $"its own, named \"{tooth.Path}#<label>\"; give the packages the labeled artifacts "
          + $"point to distinct names (e.g. '{PackageNames.FromText($"{name}-{labeled.Label}", name)}')"
          + " so that "
          + "they can be installed together and depend on each other.");
    }

    JsonArray artifacts = [];
    for (int i = 0; i < tooth.Variants.Count; i++) {
      PackageManifestVariant variant = tooth.Variants[i];
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
          notes.Add($"variants[{i}].platform: the pattern '{variant.Platform}' cannot be "
              + "expressed as a rid; the artifact is not restricted to a rid.");
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

      notes.Add(VariantNote(variant, i, reference));
    }

    if (artifacts.Count == 0) {
      notes.Add("variants: the tooth.json has no variants; add at least one artifact.");
    }

    document["artifacts"] = artifacts;
    return document;
  }

  /// What the concrete package a routed variant points to must contain.
  private static string VariantNote(PackageManifestVariant variant, int index, string reference) {
    List<string> contents = ["its files"];

    string[] downloaded = [
      .. variant.Assets
          .Where(a => a.Type != PackageManifestAsset.AssetType.Self)
          .Select(a => JsonSerializer.Serialize(a.Type).Trim('"'))
          .Distinct(),
    ];
    if (downloaded.Length > 0) {
      contents[0] = $"its files (including those of its {string.Join(", ", downloaded)} assets, "
          + "which manifest.json cannot download)";
    }

    if (variant.Dependencies.Count > 0) {
      contents.Add("its dependencies ("
          + string.Join(", ", variant.Dependencies.Select(d => $"{d.Key} {d.Value}"))
          + ") as installDependencies");
    }

    if (variant.PreserveFiles.Count > 0) {
      contents.Add("preserveFiles");
    }

    if (variant.RemoveFiles.Count > 0) {
      contents.Add("removeFiles");
    }

    PackageManifestScripts s = variant.Scripts;
    if (s.PreInstall.Count + s.Install.Count + s.PostInstall.Count + s.PreUninstall.Count
        + s.Uninstall.Count + s.PostUninstall.Count > 0) {
      contents.Add("scripts");
    }

    return $"variants[{index}]"
        + (variant.Label == "" ? "" : $" ('{variant.Label}')")
        + (variant.Platform == "" ? "" : $" ({variant.Platform})")
        + $": must become a concrete package at {reference}: a manifest.json with "
        + string.Join(", ", contents) + ".";
  }

  private static JsonObject Header(PackageManifest tooth, string name) => new() {
    [ManifestFormat.FormatVersionKey] = ManifestFormat.Version,
    [ManifestFormat.FormatUuidKey] = ManifestFormat.Uuid,
    ["name"] = name,
    ["version"] = tooth.Version.ToString(),
  };

  /// The placements for a tooth.json placement: its manifest.json form, or, when selfFiles are
  /// known and that form would place other files than lip v3 did, one file placement per file
  /// v3 placed (none when it placed nothing).
  private static List<JsonObject> Placements(
      PackageManifestAssetPlacement placement,
      IReadOnlyList<string>? selfFiles,
      List<string> notes,
      string field) {
    bool isDirectory = placement.Type == PackageManifestAssetPlacement.PlacementType.Directory;
    string src = placement.Src.Replace('\\', '/');
    string dest = placement.Dst.Replace('\\', '/');
    string description = $"the {(isDirectory ? "dir" : "file")} placement '{placement.Src}' -> "
        + $"'{placement.Dst}'";

    JsonObject? converted = isDirectory
        ? new JsonObject { ["src"] = DirectoryForm(src), ["dest"] = DirectoryForm(dest) }
        : IsGlob(src)
            ? null
            : new JsonObject { ["src"] = src.TrimStart('/'), ["dest"] = dest.TrimStart('/') };

    if (selfFiles is null) {
      if (isDirectory && IsGlob(src)) {
        notes.Add($"{field}: {description} contains a pattern in src, which tooth.json and "
            + "manifest.json both take literally; check it.");
      } else if (converted is null) {
        notes.Add($"{field}: {description} uses a pattern, which manifest.json placements do "
            + "not support; list the files instead.");
      }

      return converted is null ? [] : [converted];
    }

    List<(string Key, string Target)> placed = [
      .. ToothPlacements.Match(
              selfFiles, [new AssetPlacement("", isDirectory, placement.Src, placement.Dst)])
          .Select(m => (m.Key, ToothPlacements.NormalizeTarget(m.TargetPath))),
    ];

    if (placed.Count == 0) {
      bool namesDirectory = !isDirectory
          && selfFiles.Any(f => f.StartsWith(src.Trim('/') + "/", StringComparison.Ordinal));
      notes.Add($"{field}: {description} "
          + (namesDirectory
              ? "names a directory, but tooth.json file placements place files only, so it "
                  + "placed nothing; it was left out. Use a dir placement to place the directory."
              : "matches no file of the repository, so tooth.json placed nothing for it; it was "
                  + "left out."));
      return [];
    }

    if (converted is not null
        && Placed(converted, selfFiles).ToHashSet().SetEquals(placed)) {
      return [converted];
    }

    notes.Add($"{field}: {description} cannot be written as one manifest.json placement that "
        + $"places the same files; it was expanded into {placed.Count} file placements, so files "
        + "added later are not placed.");
    return [.. placed.Select(p => new JsonObject { ["src"] = p.Key, ["dest"] = p.Target })];
  }

  /// What a manifest.json placement places from the files (spec §7).
  private static IEnumerable<(string Key, string Target)> Placed(
      JsonObject placement, IReadOnlyList<string> files) {
    string src = (string)placement["src"]!;
    string dest = (string)placement["dest"]!;
    if (src == "" || src.EndsWith('/')) {
      return files
          .Where(f => f.StartsWith(src, StringComparison.Ordinal)
              && f != ManifestFormat.ManifestFileName)
          .Select(f => (f, dest + f[src.Length..]));
    }

    return files.Contains(src) ? [(src, dest)] : [];
  }

  private static string DirectoryForm(string path) {
    string trimmed = path.Trim('/');
    return trimmed is "" or "." ? "" : trimmed + "/";
  }

  private static void AddGlobs(
      JsonObject document, string key, IEnumerable<string> globs, List<string> notes,
      string field) {
    JsonArray array = [];
    foreach (string glob in globs) {
      if (ToothCompat.TryParseGlob(glob) is null) {
        notes.Add($"{field}: the pattern '{glob}' can never match and was left out.");
        continue;
      }

      if (glob.IndexOfAny(['?', '[', '{', '!']) >= 0) {
        notes.Add($"{field}: the pattern '{glob}' uses syntax beyond '*' and '**', which "
            + "manifest.json matches literally; check it.");
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
  /// it cannot be expressed. A note also tells when the converted range accepts a different set
  /// of versions, as far as probing versions around its bounds tells.
  private static string? ConvertRange(SemVersionRange range, List<string> notes, string field) {
    if (range.Equals(SemVersionRange.All) || range.Equals(SemVersionRange.AllRelease)) {
      return null;
    }

    string text = range.ToString().Replace(" || ", "||");
    foreach (string candidate in new[] { text, $"={text}" }) {
      VersionRange converted;
      try {
        converted = VersionRange.Parse(candidate);
      }
      catch (FormatException) {
        // Try the next form.
        continue;
      }

      if (Probes(range).FirstOrDefault(
          p => range.Contains(p) != converted.IsSatisfiedBy(p)) is SemVersion differing) {
        notes.Add($"{field}: the range '{range}' became '{converted.Text}', which "
            + (range.Contains(differing) ? "rejects" : "accepts")
            + $" {differing} unlike tooth.json; check it.");
      }

      return converted.Text;
    }

    notes.Add($"{field}: the range '{range}' could not be converted; it was left out.");
    return null;
  }

  /// Versions on and next to every version in the text of a range, released and prerelease.
  private static IEnumerable<SemVersion> Probes(SemVersionRange range) {
    foreach (Match match in RangeVersionRegex().Matches(range.ToString())) {
      int[] parts = [.. Enumerable.Range(1, 3).Select(g => int.TryParse(
          match.Groups[g].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int n)
          ? n : 0)];
      (int major, int minor, int patch) = (parts[0], parts[1], parts[2]);
      (int, int, int)[] neighbors = [
        (major, minor, patch), (major, minor, patch + 1), (major, minor + 1, 0),
        (major + 1, 0, 0), (major, minor, patch - 1), (major, minor - 1, 0), (major - 1, 0, 0),
      ];

      foreach ((int a, int b, int c) in neighbors) {
        if (a < 0 || b < 0 || c < 0 || a > ushort.MaxValue || b > ushort.MaxValue
            || c > ushort.MaxValue) {
          continue;
        }

        yield return new SemVersion(a, b, c);
        yield return new SemVersion(a, b, c, ["0"]);
        yield return new SemVersion(a, b, c, ["rc", "1"]);
      }

      string prerelease = $"{major}.{minor}.{patch}-{match.Groups[4].Value}";
      foreach (string probe in new[] { prerelease, $"{prerelease}.1" }) {
        if (match.Groups[4].Success
            && SemVersion.TryParse(probe, SemVersionStyles.Strict, out SemVersion? version)) {
          yield return version;
        }
      }
    }
  }

  private static bool IsGlob(string s) => s.IndexOfAny(['*', '?', '[', '{']) >= 0;

  /// A valid package name (spec §4.2) from the last path segment.
  private static string PackageName(string path) =>
      PackageNames.FromText(path.Split('/')[^1], "package");

  /// A version in the text of a range: up to three numeric components (wildcards read as 0) and
  /// an optional prerelease.
  [GeneratedRegex(@"(?<![\w.-])(\d+)(?:\.(\d+|[*xX]))?(?:\.(\d+|[*xX]))?(?:-([0-9A-Za-z.-]+))?")]
  private static partial Regex RangeVersionRegex();
}
