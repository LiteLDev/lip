using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DotNet.Globbing;
using Lip.Core.Entities;
using Lip.Core.Infrastructure;
using Lip.Core.Json;
using Semver;

namespace Lip.Core.Mcmod.Compat;

/// Converts a tooth.json of format 1–3 (already read as format 3) into one of format 4 (lip
/// migrate, protocol/cli.md §5.8), agreeing with how protocol/package.md §5 reads it for every
/// environment as far as format 4 can express it. The notes list what is dropped or converted
/// with another meaning, each naming the field of format 3 it comes from. Missing values are
/// asked for through IUserInteraction.Ask.
///
/// - Format 4 has no platform patterns and no "any runtime": the runtimes a platform pattern
///   stands for, and those the variants without a platform apply to, are asked for.
/// - Each label becomes a variant. It has an entry for each platform and runtime, holding the
///   variants of the label merged for the runtime as Section 5 merges them.
/// - With a variant labeled "client", the label "" has entries for the server only, and the
///   entries of "client" for the client are also those of the variant "" (Section 5, last
///   rule).
/// - Assets of type "self" and "zip" keep their placements, converted to the placements of
///   format 4; the asset types tar, tgz and uncompressed have no equivalent and are dropped.
public static partial class ToothMigrator {
  public record Result(JsonObject Document, IReadOnlyList<string> Notes);

  private static readonly JsonSerializerOptions _writeOptions = new() {
    WriteIndented = true,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    NewLine = "\n",
  };

  private static readonly string[] _knownScripts = [
    "pre_install", "install", "post_install", "pre_uninstall", "uninstall", "post_uninstall",
  ];

  /// selfFiles, when given, returns the '/'-separated paths of the files of the tooth.json's own
  /// archive (its "self" asset). Each placement of a self asset is then checked against what lip
  /// v0.37.0 placed from those files (ToothPlacements.Match): one that placed nothing is left out
  /// (format 4 would fail on it), and one whose format 4 form would place other files (e.g. a
  /// pattern placing files from subdirectories) is expanded into one placement per file. Notes
  /// tell about both.
  public static async Task<Result> Migrate(
      PackageManifest tooth,
      IUserInteraction ui,
      Func<IReadOnlyList<string>>? selfFiles = null) {
    List<string> notes = [];
    Context context = new(tooth, selfFiles, notes);

    JsonObject document = new() {
      [ToothFormat.FormatVersionKey] = ToothFormat.FormatVersion,
      ["format_uuid"] = ToothFormat.FormatUuid,
      ["tooth"] = tooth.Path,
      ["version"] = tooth.Version.ToString(),
    };
    JsonObject info = ToothCompat.Info(tooth.Info);
    if (tooth.Info.AvatarUrl != "" && !ToothCompat.IsLogo(tooth.Info.AvatarUrl)) {
      notes.Add($"info.avatar_url: '{tooth.Info.AvatarUrl}' does not start with \"https://\", "
          + "which info.logo of format 4 must; it was dropped. Set info.logo to the https URL of "
          + "the logo image.");
    }

    if (info.Count > 0) {
      document["info"] = info;
    }

    document["variants"] = await Variants(context, ui);
    return new Result(document, notes);
  }

  private sealed record Context(
      PackageManifest Tooth,
      Func<IReadOnlyList<string>>? SelfFiles,
      List<string> Notes) {
    private IReadOnlyList<string>? _selfFiles;

    public IReadOnlyList<string>? Files => _selfFiles ??= SelfFiles?.Invoke();
  }

  private static async Task<JsonObject> Variants(Context context, IUserInteraction ui) {
    PackageManifest tooth = context.Tooth;

    // The runtimes every platform pattern stands for, asked once per pattern, and the runtimes
    // the variants without a platform apply to, asked once.
    Dictionary<string, List<string>> patterns = new(StringComparer.Ordinal);
    foreach (PackageManifestVariant variant in tooth.Variants) {
      if (IsPattern(variant.Platform) && !patterns.ContainsKey(variant.Platform)) {
        patterns[variant.Platform] = await AskRuntimes(
            ui,
            $"the runtimes (RIDs, comma-separated) the platform pattern '{variant.Platform}' "
                + "stands for",
            variant.Platform);
      }
    }

    List<string> anyRuntime = tooth.Variants.Any(v => v.Platform == "")
        ? await AskRuntimes(
            ui,
            "the runtimes (RIDs, comma-separated, e.g. win-x64) the variants without a platform "
                + "apply to",
            null)
        : [];

    bool hasClient = tooth.Variants.Any(v => v.Label == ToothCompat.ClientLabel);
    JsonObject variants = [];
    void AddEntry(string variant, JsonObject entry) {
      if (variants[variant] is not JsonArray entries) {
        entries = [];
        variants[variant] = entries;
      }

      entries.Add(entry);
    }

    foreach (string label in tooth.Variants.Select(v => v.Label).Distinct()) {
      List<PackageManifestVariant> labeled = [.. tooth.Variants.Where(v => v.Label == label)];
      List<string> runtimes = [
        .. labeled
            .SelectMany(v => v.Platform == "" ? anyRuntime
                : IsPattern(v.Platform) ? patterns[v.Platform]
                : [v.Platform])
            .Distinct(StringComparer.Ordinal),
      ];

      foreach (string runtime in runtimes) {
        JsonObject content = Content(
            context, Merge(tooth, label, runtime), $"variants (label '{label}', {runtime})");
        AddEntry(label, Entry(McmodPlatform.Server, runtime, content));
        if (label != "" || !hasClient) {
          AddEntry(label, Entry(McmodPlatform.Client, runtime, content));
        }

        // On the client, the variant "" is the one of the label "client" (protocol/package.md
        // §5).
        if (label == ToothCompat.ClientLabel) {
          AddEntry("", Entry(McmodPlatform.Client, runtime, content));
        }
      }
    }

    return variants;

    static JsonObject Entry(McmodPlatform platform, string runtime, JsonObject content) {
      JsonObject entry = new() {
        ["platform"] = platform.ToSpecString(),
        ["runtime"] = runtime,
      };
      foreach ((string key, JsonNode? value) in content) {
        entry[key] = value?.DeepClone();
      }

      return entry;
    }
  }

  /// The variants of a label merged for a runtime, as protocol/package.md §5 merges them.
  private static PackageManifestVariant Merge(PackageManifest tooth, string label, string runtime) {
    try {
      return tooth.GetVariant(label, runtime);
    }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
        or KeyNotFoundException) {
      throw new InvalidOperationException(
          $"The variants labeled '{label}' cannot be merged for {runtime}, as tooth.json could "
          + $"not merge them either: {ex.Message}", ex);
    }
  }

  /// The content of an entry for a merged variant: dependencies, assets, preserve_files,
  /// remove_files and hooks.
  private static JsonObject Content(Context context, PackageManifestVariant variant, string field) {
    List<string> notes = context.Notes;
    JsonObject content = [];

    JsonObject dependencies = [];
    foreach ((PackageId dependency, SemVersionRange range) in variant.Dependencies) {
      string dependencyField = $"{field}.dependencies[\"{dependency}\"]";
      string text = range.ToString();
      if (!VersionRange.TryParse(text, out VersionRange? reparsed)
          || Probes(range).Any(p => range.Contains(p) != reparsed!.IsSatisfiedBy(p))) {
        notes.Add($"{dependencyField}: the range '{range}' cannot be written as a range of "
            + "format 4; it was left out.");
        continue;
      }

      dependencies[dependency.ToString()] = text;
      if (dependency.Variant == ToothCompat.ClientLabel) {
        notes.Add($"{dependencyField}: keeps its \"client\" variant. A tooth.json of format 3 "
            + "named the variant \"\" of a package of format 4 by it (protocol/package.md §5); "
            + "format 4 does not, so drop \"#client\" once that package has moved to format 4.");
      }
    }

    if (dependencies.Count > 0) {
      content["dependencies"] = dependencies;
    }

    JsonArray assets = [];
    for (int i = 0; i < variant.Assets.Count; i++) {
      PackageManifestAsset asset = variant.Assets[i];
      string assetField = $"{field}.assets[{i}]";
      if (asset.Type is not (PackageManifestAsset.AssetType.Self
          or PackageManifestAsset.AssetType.Zip)) {
        notes.Add($"{assetField}: format 4 has no asset type "
            + $"\"{JsonSerializer.Serialize(asset.Type).Trim('"')}\"; it was dropped. Publish its "
            + "files as a ZIP archive and add an asset of type \"zip\".");
        continue;
      }

      JsonArray placements = [];
      foreach (PackageManifestAssetPlacement placement in asset.Placements) {
        foreach (JsonObject converted in Placements(context, asset, placement, assetField)) {
          placements.Add(converted);
        }
      }

      JsonObject result = asset.Type == PackageManifestAsset.AssetType.Self
          ? new() { ["type"] = "self" }
          : new() {
            ["type"] = "zip",
            ["urls"] = new JsonArray([.. asset.Urls.Select(u => (JsonNode)u.ToString())]),
          };
      if (placements.Count > 0) {
        result["place"] = placements;
      }

      assets.Add(result);
    }

    if (assets.Count > 0) {
      content["assets"] = assets;
    }

    List<string> preserveFiles = [.. variant.PreserveFiles.Select(GlobListJsonConverter.Format)];
    foreach (string pattern in preserveFiles.Where(p => !p.Replace('\\', '/').Contains('/'))) {
      notes.Add($"{field}.preserve_files: '{pattern}' has no '/', so tooth.json also matched it "
          + $"against file names in every directory; it became '**/{pattern}'.");
    }

    AddGlobs(content, "preserve", ToothCompat.PreserveFiles(preserveFiles), notes,
        $"{field}.preserve_files");
    AddGlobs(content, "purge", ToothCompat.RemoveFiles(
        variant.RemoveFiles.Select(GlobListJsonConverter.Format)), notes,
        $"{field}.remove_files");

    Hooks hookCommands = ToothCompat.ConvertScripts(variant.Scripts);
    JsonObject hooks = [];
    AddCommands(hooks, "pre_install", hookCommands.PreInstall);
    AddCommands(hooks, "post_install", hookCommands.PostInstall);
    AddCommands(hooks, "pre_uninstall", hookCommands.PreUninstall);
    AddCommands(hooks, "post_uninstall", hookCommands.PostUninstall);
    if (hooks.Count > 0) {
      content["hooks"] = hooks;
    }

    return content;
  }

  /// The placements of format 4 for a placement of format 3: its format 4 form, or, for a self
  /// asset whose files are known, when that form would place other files than lip v0.37.0 did,
  /// one file-form placement per file v0.37.0 placed (none when it placed nothing).
  private static List<JsonObject> Placements(
      Context context,
      PackageManifestAsset asset,
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
        notes.Add($"{field}: {description} contains a pattern in src, which tooth.json took "
            + "literally and format 4 does not; check it.");
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

    notes.Add($"{field}: {description} cannot be written as one placement of format 4 that "
        + $"places the same files; it was expanded into {placed.Count} placements, so files "
        + "added later are not placed.");
    return [.. placed.Select(p => new JsonObject { ["src"] = p.Key, ["dest"] = p.Target })];
  }

  /// What a placement of format 4 of a self asset places from the files
  /// (protocol/workspace.md §4 step 2).
  private static IEnumerable<(string Key, string Target)> Placed(
      JsonObject placement, IReadOnlyList<string> files) {
    string src = (string)placement["src"]!;
    string dest = (string)placement["dest"]!;
    IEnumerable<string> candidates = files.Where(f => f != ToothFormat.ToothFileName);
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

    return candidates.Contains(src) ? [(src, dest)] : [];
  }

  private static string DirectoryForm(string path) {
    string trimmed = path.Trim('/');
    return trimmed is "" or "." ? "" : trimmed + "/";
  }

  private static void AddGlobs(
      JsonObject content, string key, IEnumerable<string> globs, List<string> notes,
      string field) {
    JsonArray array = [];
    foreach (string glob in globs) {
      if (ToothCompat.TryParseGlob(glob) is null) {
        notes.Add($"{field}: the pattern '{glob}' is not a valid glob and was left out.");
        continue;
      }

      if (glob.Contains('{')) {
        notes.Add($"{field}: the pattern '{glob}' contains '{{', which format 4 matches "
            + "literally; check it.");
      }

      array.Add(glob);
    }

    if (array.Count > 0) {
      content[key] = array;
    }
  }

  private static void AddCommands(JsonObject hooks, string key, IReadOnlyList<string> commands) {
    if (commands.Count > 0) {
      hooks[key] = new JsonArray([.. commands.Select(c => (JsonNode)c)]);
    }
  }

  /// Asks for a non-empty list of runtimes, each matching pattern when it is given.
  private static async Task<List<string>> AskRuntimes(
      IUserInteraction ui, string question, string? pattern) {
    Glob? glob = pattern is null ? null : Glob.Parse(pattern);
    string answer = await AskValid(ui, question, a => {
      List<string> runtimes = SplitList(a);
      if (runtimes.Count == 0) {
        return "enter at least one RID, e.g. win-x64.";
      }

      if (runtimes.FirstOrDefault(r => !ToothParser.IsRuntime(r)) is string invalid) {
        return $"'{invalid}' is not a runtime identifier.";
      }

      return glob is not null && runtimes.FirstOrDefault(r => !glob.IsMatch(r)) is string other
          ? $"'{other}' does not match '{pattern}'."
          : null;
    });
    return SplitList(answer);
  }

  private static List<string> SplitList(string text) => [
    .. text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Distinct(StringComparer.Ordinal),
  ];

  /// Asks until the answer is valid (validate returns null) and returns the trimmed answer.
  /// Throws CannotAskException when the user cannot be asked.
  public static async Task<string> AskValid(
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

  /// Notes for the fields of a raw tooth.json that its format 3 form (tooth, read from root)
  /// does not keep and that therefore cannot reach the migrated tooth.json: dependencies of
  /// format 1 and 2 whose range the migration to format 3 could not read, prerequisites of
  /// format 2, and scripts of format 3 other than the six install and uninstall hooks (e.g.
  /// pre_pack, or custom scripts).
  public static IReadOnlyList<string> DroppedFields(JsonElement root, PackageManifest tooth) {
    List<string> notes = [];
    switch (ToothParser.FormatVersion(root)) {
      case 1 or 2:
        HashSet<string> kept = [
          .. tooth.Variants.SelectMany(v => v.Dependencies.Keys).Select(id => id.ToString()),
        ];
        // As in the migration to format 3: a platform of format 2 with its own dependencies uses
        // them instead of the root ones.
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
          notes.Add("prerequisites: has no equivalent in format 4 and was dropped; declare the "
              + "packages that must already be installed as dependencies instead.");
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
                notes.Add($"variants[{i}].scripts.{script.Name}: format 4 only has install and "
                    + "uninstall hooks; it was dropped.");
              }
            }
          }

          i++;
        }

        break;
    }

    return notes;
  }

  /// A note when utf8Json is not a valid tooth.json of format 4, else null.
  public static string? ValidationNote(byte[] utf8Json) {
    try {
      ToothParser.Parse(utf8Json);
      return null;
    }
    catch (ToothFormatException ex) {
      return $"The result is not a valid tooth.json of format 4 yet: {ex.Message}";
    }
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
    }
  }

  /// A version in the text of a range: up to three numeric components (wildcards read as 0).
  [GeneratedRegex(@"(?<![\w.-])(\d+)(?:\.(\d+|[*xX]))?(?:\.(\d+|[*xX]))?")]
  private static partial Regex RangeVersionRegex();

  /// The UTF-8 bytes of a tooth.json as lip writes it: indented by two spaces, with a final
  /// line break.
  public static byte[] Serialize(JsonObject document) =>
      Encoding.UTF8.GetBytes(document.ToJsonString(_writeOptions) + "\n");
}
