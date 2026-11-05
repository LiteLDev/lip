using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Lip.Core.Entities;
using Lip.Core.Infrastructure;
using Lip.Core.Json;
using Semver;

namespace Lip.Core.Mcmod.Compat;

/// Converts a tooth.json (v1–v3, already migrated to v3) into a manifest.json of format bf45b1
/// (specs/cli.md §5.8), as faithfully as the formats allow. lip itself does not need the result:
/// it reads tooth.json archives as they are (ToothCompat, spec §10.2), with the same mapping.
/// RepositoryMigrator writes the result into a package repository. The notes list what is
/// dropped or converted with another meaning, each naming the tooth.json field it comes from
/// (in its v3 form). Missing values are asked for through IUserInteraction.Ask.
///
/// - A single unlabeled variant whose assets are all "self", for no platform or one exact
///   runtime: a package document holding its content.
/// - Anything else: a meta document. Its targets stand for the merged content of the variants of
///   each label per runtime (patterns are asked for the runtimes they stand for), and point
///   where the packages are to be published (GitHub release assets for github.com paths, under
///   an asked URL prefix otherwise). Targets holds a package document per target, in order;
///   packing and publishing them is left to the author (lip pack).
public static partial class ToothMigrator {
  public record Result(JsonObject Document, IReadOnlyList<string> Notes) {
    /// The package document of each target of a meta document, in order; empty otherwise.
    public IReadOnlyList<JsonObject> Targets { get; init; } = [];
  }

  private static readonly string[] _knownScripts = [
    "pre_install", "install", "post_install", "pre_uninstall", "uninstall", "post_uninstall",
  ];

  /// selfFiles, when given, returns the '/'-separated paths of the files of the tooth.json's own
  /// archive (its "self" asset). Each placement of a self asset is then checked against what lip
  /// v3 placed from those files (ToothPlacements.Match): one that placed nothing is left out
  /// (manifest.json would fail on it), and one whose manifest.json form would place other files
  /// (e.g. a pattern placing files from subdirectories) is expanded into one file per file.
  /// Notes tell about both.
  public static async Task<Result> Migrate(
      PackageManifest tooth,
      IUserInteraction ui,
      Func<IReadOnlyList<string>>? selfFiles = null) {
    List<string> notes = [];
    Context context = new(tooth, ui, selfFiles, notes);

    string id = ToothCompat.GetIdentity(tooth.Path, "");
    if (!PackageNames.IsValid(id)) {
      id = await AskId(ui, $"the package id (the tooth name '{id}' is not a valid id)");
    }

    if (tooth.Info.Name != "" || tooth.Info.Description != "" || tooth.Info.Tags.Count > 0
        || tooth.Info.AvatarUrl != "") {
      notes.Add("info: manifest.json has no package information; it was dropped.");
    }

    PackageManifestVariant? single = tooth.Variants.Count == 1 ? tooth.Variants[0] : null;
    if (single is not null && single.Label == ""
        && single.Assets.All(a => a.Type == PackageManifestAsset.AssetType.Self)
        && !IsPattern(single.Platform)) {
      JsonObject document = Package(id, tooth.Version, null,
          single.Platform == "" ? null : single.Platform);
      foreach ((string key, JsonNode? value) in Content(context, single, "variants[0]")) {
        document[key] = value?.DeepClone();
      }

      return new Result(document, notes);
    }

    (JsonArray targets, List<JsonObject> packages) = await Targets(context, id);
    JsonObject meta = new() {
      [ManifestFormat.FormatKey] = ManifestFormat.Format,
      ["version"] = tooth.Version.ToString(),
      ["targets"] = targets,
    };
    return new Result(meta, notes) { Targets = packages };
  }

  /// The start of a package document: format, name, version, and platform and runtime when
  /// given.
  private static JsonObject Package(string name, SemVersion version, string? platform, string? runtime) {
    JsonObject document = new() {
      [ManifestFormat.FormatKey] = ManifestFormat.Format,
      ["name"] = name,
      ["version"] = version.ToString(),
    };
    if (platform is not null) {
      document["platform"] = platform;
    }

    if (runtime is not null) {
      document["runtime"] = runtime;
    }

    return document;
  }

  private sealed record Context(
      PackageManifest Tooth,
      IUserInteraction Ui,
      Func<IReadOnlyList<string>>? SelfFiles,
      List<string> Notes) {
    private IReadOnlyList<string>? _selfFiles;

    public IReadOnlyList<string>? Files => _selfFiles ??= SelfFiles?.Invoke();
  }

  /// The targets of a meta document (specs/cli.md §5.8 rule 3), and the package document each
  /// target is to name.
  private static async Task<(JsonArray Targets, List<JsonObject> Packages)> Targets(
      Context context, string id) {
    PackageManifest tooth = context.Tooth;

    // The runtimes every pattern platform stands for, asked once per pattern.
    Dictionary<string, List<string>> patterns = new(StringComparer.Ordinal);
    foreach (PackageManifestVariant variant in tooth.Variants) {
      if (IsPattern(variant.Platform) && !patterns.ContainsKey(variant.Platform)) {
        patterns[variant.Platform] = await AskRuntimes(context.Ui, variant.Platform);
      }
    }

    string prefix = await ReleasePrefix(context.Ui, tooth.Path);
    bool hasClient = tooth.Variants.Any(v => v.Label == ToothCompat.ClientLabel);

    // Each target: its variant, platform and runtimes (null when absent), and content.
    List<(string Variant, string? Platform, List<string>? Runtimes, string Name, JsonObject Content)>
        selected = [];
    foreach (string label in tooth.Variants.Select(v => v.Label).Distinct()) {
      List<PackageManifestVariant> variants = [.. tooth.Variants.Where(v => v.Label == label)];
      string field = $"variants (label '{label}')";

      List<string> runtimes = [
        .. variants
            .SelectMany(v => v.Platform == "" ? []
                : IsPattern(v.Platform) ? patterns[v.Platform]
                : [v.Platform])
            .Distinct(StringComparer.Ordinal),
      ];

      // Runtimes whose merged content is the same make one target.
      List<(List<string>? Runtimes, JsonObject Content)> groups = [];
      foreach (string runtime in runtimes) {
        JsonObject content = Content(context, Merge(label, variants.Where(
            v => v.Platform == "" || v.Platform == runtime
                || (IsPattern(v.Platform) && patterns[v.Platform].Contains(runtime)))), field);
        int existing = groups.FindIndex(g => JsonNode.DeepEquals(g.Content, content));
        if (existing >= 0) {
          groups[existing].Runtimes!.Add(runtime);
        } else {
          groups.Add(([runtime], content));
        }
      }

      if (variants.Any(v => v.Platform == "")) {
        groups.Add((null, Content(context, Merge(label, variants.Where(v => v.Platform == "")), field)));
      }

      string name = label == "" ? id
          : ToothCompat.GetIdentity(tooth.Path, label) is string labelId
              && PackageNames.IsValid(labelId)
              ? labelId
              : PackageNames.FromText($"{id}-{label.Replace('/', '-')}", id);
      string? platform = hasClient && label == "" ? McmodPlatform.Server.ToSpecString() : null;

      foreach ((List<string>? groupRuntimes, JsonObject content) in groups) {
        selected.Add((label, platform, groupRuntimes, name, content));

        // tooth.json selects the "client" label for "" on client platforms (spec §10.2 rule 2).
        if (label == ToothCompat.ClientLabel) {
          selected.Add(("", McmodPlatform.Client.ToSpecString(), groupRuntimes, name, content));
        }
      }
    }

    JsonArray targets = [];
    List<JsonObject> packages = [];
    foreach ((string variant, string? platform, List<string>? runtimes, string name, JsonObject content)
        in selected) {
      JsonObject target = [];
      if (variant != "") {
        target["variant"] = variant;
      }

      if (platform is not null) {
        target["platform"] = platform;
      }

      if (runtimes is not null) {
        target["runtime"] = runtimes.Count == 1
            ? runtimes[0]
            : new JsonArray([.. runtimes.Select(r => (JsonNode)r)]);
      }

      target["source"] = prefix + name
          + (platform is not null ? "-{{platform}}" : "")
          + (runtimes is not null ? "-{{runtime}}" : "")
          + ".mcmod";
      targets.Add(target);

      // A package for several runtimes leaves its runtime open.
      JsonObject package = Package(
          name, tooth.Version, platform, runtimes is [string runtime] ? runtime : null);
      foreach ((string key, JsonNode? value) in content) {
        package[key] = value?.DeepClone();
      }

      packages.Add(package);
    }

    return (targets, packages);
  }

  /// Merges variants as tooth.json does (spec §10.2 rule 2): dependencies, assets, preserve_files
  /// and remove_files concatenated, each script hook from the one variant that has it.
  private static PackageManifestVariant Merge(string label, IEnumerable<PackageManifestVariant> variants) {
    List<PackageManifestVariant> merged = [.. variants];
    try {
      return new PackageManifestVariant {
        Label = label,
        Dependencies = merged.SelectMany(v => v.Dependencies).ToDictionary(d => d.Key, d => d.Value),
        Assets = [.. merged.SelectMany(v => v.Assets)],
        PreserveFiles = [.. merged.SelectMany(v => v.PreserveFiles)],
        RemoveFiles = [.. merged.SelectMany(v => v.RemoveFiles)],
        Scripts = new() {
          PreInstall = Hook(merged, s => s.PreInstall),
          Install = Hook(merged, s => s.Install),
          PostInstall = Hook(merged, s => s.PostInstall),
          PreUninstall = Hook(merged, s => s.PreUninstall),
          Uninstall = Hook(merged, s => s.Uninstall),
          PostUninstall = Hook(merged, s => s.PostUninstall),
        },
      };
    }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) {
      throw new InvalidOperationException(
          $"The variants labeled '{label}' cannot be merged, as tooth.json could not merge them"
          + $" either: {ex.Message}", ex);
    }

    static List<string> Hook(
        List<PackageManifestVariant> variants, Func<PackageManifestScripts, List<string>> hook) =>
        variants.SingleOrDefault(v => hook(v.Scripts).Count > 0) is { } v ? hook(v.Scripts) : [];
  }

  /// The content of a (merged) variant (specs/cli.md §5.8 rule 4).
  private static JsonObject Content(Context context, PackageManifestVariant variant, string field) {
    List<string> notes = context.Notes;
    JsonObject content = [];

    JsonArray dependencies = [];
    foreach ((PackageId dependency, SemVersionRange range) in variant.Dependencies) {
      string dependencyField = $"{field}.dependencies[\"{dependency}\"]";
      string key = ToothCompat.GetIdentity(dependency.Path, dependency.Variant);
      if (!PackageNames.IsValid(key)) {
        notes.Add($"{dependencyField}: the package it names would have the identity '{key}', "
            + "which is not a valid id; it was dropped.");
        continue;
      }

      JsonObject value = new() { ["name"] = key };
      if (ConvertRange(range, notes, dependencyField) is string version) {
        value["version"] = version;
      }

      value["source"] = dependency.ToString();
      dependencies.Add(value);

      if (dependency.Variant == ToothCompat.ClientLabel) {
        notes.Add($"{dependencyField}: keeps its \"client\" variant. tooth.json read it as the "
            + "default variant of a package that uses manifest.json (spec §10.2 rule 3); "
            + "manifest.json does not, so drop \"#client\" once that package has moved to "
            + "manifest.json.");
      }
    }

    if (dependencies.Count > 0) {
      content["dependencies"] = dependencies;
    }

    JsonObject archives = [];
    JsonArray files = [];
    for (int i = 0; i < variant.Assets.Count; i++) {
      PackageManifestAsset asset = variant.Assets[i];
      string assetField = $"{field}.assets[{i}]";
      string? from = null;
      if (asset.Type != PackageManifestAsset.AssetType.Self) {
        from = $"asset{i}";
        JsonObject archive = new() {
          ["url"] = asset.Urls.Count == 1
              ? asset.Urls[0].ToString()
              : new JsonArray([.. asset.Urls.Select(u => (JsonNode)u.ToString())]),
          ["format"] = asset.Type switch {
            PackageManifestAsset.AssetType.Zip => "zip",
            PackageManifestAsset.AssetType.Tar => "tar",
            PackageManifestAsset.AssetType.Tgz => "tgz",
            _ => "file",
          },
        };
        archives[from] = archive;
      }

      foreach (PackageManifestAssetPlacement placement in asset.Placements) {
        foreach (JsonObject file in Files(context, asset, from, placement, assetField)) {
          files.Add(file);
        }
      }
    }

    if (archives.Count > 0) {
      content["archives"] = archives;
    }

    if (files.Count > 0) {
      content["files"] = files;
    }

    List<string> preserveFiles = [.. variant.PreserveFiles.Select(GlobListJsonConverter.Format)];
    foreach (string pattern in preserveFiles.Where(p => !p.Replace('\\', '/').Contains('/'))) {
      notes.Add($"{field}.preserve_files: '{pattern}' has no '/', so tooth.json also matched it "
          + $"against file names in every directory; it became '**/{pattern}'.");
    }

    AddGlobs(content, "preserve", ToothCompat.PreserveFiles(preserveFiles), notes,
        $"{field}.preserve_files");
    AddGlobs(content, "remove", ToothCompat.RemoveFiles(
        variant.RemoveFiles.Select(GlobListJsonConverter.Format)), notes,
        $"{field}.remove_files");

    Hooks converted = ToothCompat.ConvertScripts(variant.Scripts);
    JsonObject hooks = [];
    AddCommands(hooks, "preInstall", converted.PreInstall);
    AddCommands(hooks, "postInstall", converted.PostInstall);
    AddCommands(hooks, "preUninstall", converted.PreUninstall);
    AddCommands(hooks, "postUninstall", converted.PostUninstall);
    if (hooks.Count > 0) {
      content["hooks"] = hooks;
    }

    return content;
  }

  /// The files for a tooth.json placement: its manifest.json form, or, for a self asset whose
  /// files are known, when that form would place other files than lip v3 did, one file-form
  /// file per file v3 placed (none when it placed nothing).
  private static List<JsonObject> Files(
      Context context,
      PackageManifestAsset asset,
      string? from,
      PackageManifestAssetPlacement placement,
      string field) {
    List<string> notes = context.Notes;
    bool isDirectory = placement.Type == PackageManifestAssetPlacement.PlacementType.Directory;
    string src = placement.Src.Replace('\\', '/').TrimStart('/');
    string dest = ToothPlacements.NormalizeTarget(placement.Dst);
    string description = $"the {(isDirectory ? "dir" : "file")} placement '{placement.Src}' -> "
        + $"'{placement.Dst}'";

    if (dest.StartsWith('/') || dest == ".." || dest.StartsWith("../", StringComparison.Ordinal)) {
      notes.Add($"{field}: {description} places files outside the workspace; it was left out.");
      return [];
    }

    if (asset.Type == PackageManifestAsset.AssetType.Uncompressed) {
      // The single downloaded file is the file named after its archive (spec §7 "Archives").
      return [WithFrom(new JsonObject { ["src"] = from, ["dest"] = dest.TrimEnd('/') }, from)];
    }

    JsonObject? converted = isDirectory
        ? new JsonObject { ["src"] = DirectoryForm(src), ["dest"] = DirectoryForm(dest) }
        : !IsPattern(src)
            ? new JsonObject { ["src"] = src, ["dest"] = dest }
            : ToothCompat.TryParseGlob(src) is null
                ? null
                : new JsonObject { ["src"] = src, ["dest"] = DirectoryForm(dest) };

    IReadOnlyList<string>? selfFiles =
        asset.Type == PackageManifestAsset.AssetType.Self ? context.Files : null;
    if (selfFiles is null) {
      if (isDirectory && IsPattern(src)) {
        notes.Add($"{field}: {description} contains a pattern in src, which tooth.json and "
            + "manifest.json both take literally; check it.");
      } else if (converted is null) {
        notes.Add($"{field}: {description} uses a pattern that is not a valid glob; it was "
            + "left out. List the files instead.");
      } else if (!isDirectory && IsPattern(src)
          && (src.Contains("**", StringComparison.Ordinal)
              || IsPattern(PathGlob.FixedPrefix(src) is var p ? src[p.Length..].TrimEnd('/')
                  .Split('/')[..^1] is { Length: > 0 } dirs ? string.Join('/', dirs) : "" : ""))) {
        notes.Add($"{field}: {description} places the files it matches in subdirectories at "
            + "their path, while tooth.json placed them by file name only; check it.");
      }

      return converted is null ? [] : [WithFrom(converted, from)];
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

    notes.Add($"{field}: {description} cannot be written as one manifest.json file that "
        + $"places the same files; it was expanded into {placed.Count} files, so files added "
        + "later are not placed.");
    return [.. placed.Select(p => new JsonObject { ["src"] = p.Key, ["dest"] = p.Target })];

    static JsonObject WithFrom(JsonObject file, string? from) {
      if (from is null) {
        return file;
      }

      JsonObject result = new() { ["from"] = from };
      foreach ((string key, JsonNode? value) in file) {
        result[key] = value?.DeepClone();
      }

      return result;
    }
  }

  /// What a file of the package's own archive places from the files (spec §7).
  private static IEnumerable<(string Key, string Target)> Placed(
      JsonObject file, IReadOnlyList<string> files) {
    string src = (string)file["src"]!;
    string dest = (string)file["dest"]!;
    IEnumerable<string> candidates = files.Where(f => f != ManifestFormat.ManifestFileName);
    if (PathGlob.HasMetacharacters(src)) {
      PathGlob glob = new(src);
      string prefix = PathGlob.FixedPrefix(src);
      return candidates.Where(glob.IsMatch).Select(f => (f, dest + f[prefix.Length..]));
    }

    if (src == "" || src.EndsWith('/')) {
      return candidates
          .Where(f => f.StartsWith(src, StringComparison.Ordinal))
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
        notes.Add($"{field}: the pattern '{glob}' is not a valid glob and was left out.");
        continue;
      }

      if (glob.Contains('{')) {
        notes.Add($"{field}: the pattern '{glob}' contains '{{', which manifest.json matches "
            + "literally; check it.");
      }

      array.Add(glob);
    }

    if (array.Count > 0) {
      document[key] = array;
    }
  }

  private static void AddCommands(JsonObject hooks, string key, IReadOnlyList<string> commands) {
    if (commands.Count > 0) {
      hooks[key] = new JsonArray([.. commands.Select(c => (JsonNode)c)]);
    }
  }

  /// The URL prefix (ending with '/') the packages of a meta document are published under.
  private static async Task<string> ReleasePrefix(IUserInteraction ui, string path) {
    string[] elements = path.Split('/');
    if (elements.Length >= 3 && elements[0] == "github.com") {
      return $"https://github.com/{elements[1]}/{elements[2]}/releases/download/v{{{{version}}}}/";
    }

    string prefix = await AskValid(
        ui,
        $"the URL prefix the packages of {path} will be published under (e.g. "
            + "https://example.com/releases/v{{version}})",
        answer => Uri.TryCreate(answer, UriKind.Absolute, out Uri? uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && RefTemplate.FindUnknownPlaceholder(answer) is null
            ? null
            : "enter an https URL; it may use {{version}}, {{platform}} and {{runtime}}.");
    return prefix.EndsWith('/') ? prefix : prefix + "/";
  }

  private static async Task<List<string>> AskRuntimes(IUserInteraction ui, string pattern) {
    string answer = await AskValid(
        ui,
        $"the runtimes (RIDs, comma-separated) the platform pattern '{pattern}' stands for",
        a => SplitList(a).Count > 0 ? null : "enter at least one RID, e.g. win-x64,win-arm64.");
    return SplitList(answer);
  }

  private static List<string> SplitList(string text) => [
    .. text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Distinct(StringComparer.Ordinal),
  ];

  internal static Task<string> AskId(IUserInteraction ui, string question) => AskValid(
      ui, question,
      a => PackageNames.IsValid(a) ? null : "an id must match ^[A-Za-z0-9][A-Za-z0-9._-]*$.");

  /// Asks until the answer is valid (validate returns null) and returns the trimmed answer.
  internal static async Task<string> AskValid(
      IUserInteraction ui, string question, Func<string, string?> validate) {
    while (true) {
      // A user interaction that returns no answer cannot ask.
      string answer = (await ui.Ask(question))?.Trim()
          ?? throw new CannotAskException($"lip needs {question}, but cannot ask for it here.");
      if (validate(answer) is not string error) {
        return answer;
      }

      await ui.PrintWarning($"Invalid answer: {error}");
    }
  }

  /// Whether a tooth.json platform or path is a pattern.
  private static bool IsPattern(string s) => s.IndexOfAny(['*', '?', '[']) >= 0;

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
              + "it to dependencies by hand.");
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
                    + "and uninstall hooks; it was dropped.");
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

  /// Converts a v3 range to the node-semver syntax (spec §1 "range"), or returns null with a note when
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

  /// A version in the text of a range: up to three numeric components (wildcards read as 0) and
  /// an optional prerelease.
  [GeneratedRegex(@"(?<![\w.-])(\d+)(?:\.(\d+|[*xX]))?(?:\.(\d+|[*xX]))?(?:-([0-9A-Za-z.-]+))?")]
  private static partial Regex RangeVersionRegex();
}
