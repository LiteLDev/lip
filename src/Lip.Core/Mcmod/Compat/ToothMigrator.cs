using System.Text;

using System.Text.Json;
using System.Text.Json.Nodes;
using DotNet.Globbing;
using Lip.Core.Entities;
using Lip.Core.Infrastructure;
using Lip.Core.Json;
using Semver;

namespace Lip.Core.Mcmod.Compat;

/// Converts a tooth.json of format 1–3 (already read as format 3) into a package file (lip
/// migrate, protocol/compatibility.md §5), agreeing with how protocol/compatibility.md §2 reads
/// it for every environment as far as a package file can express it. The notes list what is
/// dropped or converted with another meaning, each naming the field of format 3 it comes from.
/// Missing values, and
/// dependency ranges that are not ranges of a package file, are asked for through
/// IUserInteraction.Ask.
///
/// - A package file has no platform patterns and no "any runtime": the runtimes a platform pattern
///   stands for, and those the variants without a platform apply to, are asked for.
/// - Each label becomes a variant. It has an entry for each platform and runtime, holding the
///   variants of the label merged for the runtime and converted exactly as Section 2 converts
///   them (ToothCompat.ConvertEntry); a runtime whose merged variants have an asset of type tar,
///   tgz or uncompressed has no entry.
/// - With a variant labeled "client", the label "" has entries for the server only, and the
///   entries of "client" for the client are also those of the variant "" (Section 2).
public static class ToothMigrator {
  public record Result(JsonObject Document, IReadOnlyList<string> Notes);


  private static readonly string[] _knownScripts = [
    "pre_install", "install", "post_install", "pre_uninstall", "uninstall", "post_uninstall",
  ];

  public static async Task<Result> Migrate(
      PackageManifest tooth,
      IUserInteraction ui) {
    List<string> notes = [];
    Context context = new(tooth, notes, ui, []);
    RunTrace.Write("migrate", $"converting tooth.json of {tooth.Path} {tooth.Version} with "
        + $"{tooth.Variants.Count} variants");

    JsonObject document = new() {
      [PackageFormat.FormatKey] = PackageFormat.Format,
      ["id"] = tooth.Path,
      ["version"] = tooth.Version.ToString(),
    };
    if (tooth.Info is PackageManifestInfo info) {
      document["info"] = ToothCompat.Info(info);
      if (info.AvatarUrl is { Length: > 0 } avatarUrl && !ToothCompat.IsLogo(avatarUrl)) {
        notes.Add($"info.avatar_url: '{avatarUrl}' does not start with \"https://\", which "
            + "info.logo of lip.yaml must; it was dropped. Set info.logo to the https URL of the "
            + "logo image.");
      }
    }

    document["variants"] = await Variants(context);
    return new Result(document, notes);
  }

  /// Ranges holds the answers for dependency ranges that are not ranges of a package file, by
  /// dependency and range, so each is asked once.
  private sealed record Context(
      PackageManifest Tooth,
      List<string> Notes,
      IUserInteraction Ui,
      Dictionary<(PackageId, string), string> Ranges);

  private static async Task<JsonObject> Variants(Context context) {
    PackageManifest tooth = context.Tooth;
    IUserInteraction ui = context.Ui;

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
        RunTrace.Write("migrate", $"platform pattern '{variant.Platform}' stands for "
            + string.Join(", ", patterns[variant.Platform]));
      }
    }

    List<string> anyRuntime = tooth.Variants.Any(v => v.Platform == "")
        ? await AskRuntimes(
            ui,
            "the runtimes (RIDs, comma-separated, e.g. win-x64) the variants without a platform "
                + "apply to",
            null)
        : [];
    if (anyRuntime.Count > 0) {
      RunTrace.Write("migrate", "variants without a platform apply to "
          + string.Join(", ", anyRuntime));
    }

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
        if (await Content(context, Merge(tooth, label, runtime), runtime,
            $"variants (label '{label}', {runtime})") is not JsonObject content) {
          RunTrace.Write("migrate", $"variant '{label}' has no entry for {runtime}");
          continue;
        }

        RunTrace.Write("migrate", $"variant '{label}': entry for {runtime}");

        AddEntry(label, Entry(McmodPlatform.Server, runtime, content));
        if (label != "" || !hasClient) {
          AddEntry(label, Entry(McmodPlatform.Client, runtime, content));
        }

        // On the client, the variant "" is the one of the label "client"
        // (protocol/compatibility.md §2).
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

  /// The variants of a label merged for a runtime, as protocol/compatibility.md §2 merges them.
  private static PackageManifestVariant Merge(PackageManifest tooth, string label, string runtime) {
    try {
      return tooth.GetVariant(label, runtime);
    }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
        or KeyNotFoundException) {
      throw new InvalidOperationException(
          $"Cannot migrate tooth.json: the variants labeled '{label}' cannot be merged for "
          + $"{runtime}, because {ToothCompat.MergeFailureReason(ex)}; lip v0.37.0 could not "
          + $"install them for {runtime} either. Fix these variants in tooth.json so that each "
          + $"dependency is declared, and each kind of script is set, in only one of the variants "
          + $"that apply to {runtime}, then run 'lip migrate' again.", ex);
    }
  }

  /// The content of an entry for a merged variant, converted as protocol/compatibility.md §2
  /// converts it (ToothCompat.ConvertEntry), without platform and runtime, with each dependency
  /// range that is not a range of a package file asked for; null when the variant has no entry
  /// for the runtime.
  private static async Task<JsonObject?> Content(
      Context context, PackageManifestVariant variant, string runtime, string field) {
    List<string> notes = context.Notes;
    if (ToothCompat.ConvertEntry(variant, McmodPlatform.Server, runtime, notes, field)
        is not JsonObject content) {
      return null;
    }

    content.Remove("platform");
    content.Remove("runtime");

    foreach ((PackageId dependency, SemVersionRange range) in variant.Dependencies) {
      string dependencyField = $"{field}.dependencies[\"{dependency}\"]";
      string text = range.ToString();
      if (!VersionRange.TryParse(text, out _)) {
        if (!context.Ranges.TryGetValue((dependency, text), out string? answer)) {
          answer = await context.Ui.AskValid(
              $"a version range of lip.yaml for {dependencyField}, whose range '{text}' is not one",
              a => VersionRange.TryParse(a, out _) ? null : $"'{a}' is not a version range.");
          context.Ranges[(dependency, text)] = answer;
        }

        content["deps"]![dependency.ToString()] = answer;
      }

      if (dependency.Variant == ToothCompat.ClientLabel) {
        notes.Add($"{dependencyField}: keeps its \"client\" variant. A tooth.json named the "
            + "variant \"\" of a package that has a lip.yaml by it (protocol/compatibility.md §2); "
            + "lip.yaml does not, so drop \"#client\" once that package has moved to lip.yaml.");
      }
    }

    foreach (string pattern in variant.PreserveFiles.Select(GlobListJsonConverter.Format)
        .Where(p => !p.Replace('\\', '/').Contains('/'))) {
      notes.Add($"{field}.preserve_files: '{pattern}' has no '/', so tooth.json also matched it "
          + $"against file names in every directory; it became '**/{pattern}'.");
    }

    return content;
  }

  /// Asks for a non-empty list of runtimes, each matching pattern when it is given.
  private static async Task<List<string>> AskRuntimes(
      IUserInteraction ui, string question, string? pattern) {
    Glob? glob = pattern is null ? null : Glob.Parse(pattern);
    string answer = await ui.AskValid(question, a => {
      List<string> runtimes = SplitList(a);
      if (runtimes.Count == 0) {
        return "enter at least one RID, e.g. win-x64.";
      }

      if (runtimes.FirstOrDefault(r => !PackageFileParser.IsRuntime(r)) is string invalid) {
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


  /// Whether a tooth.json platform or path is a pattern.
  private static bool IsPattern(string s) => s.IndexOfAny(['*', '?', '[']) >= 0;

  /// Notes for the fields of a raw tooth.json that its format 3 form (tooth, read from root)
  /// does not keep and that therefore cannot reach the migrated lip.yaml: dependencies of
  /// format 1 and 2 whose range the migration to format 3 could not read, prerequisites of
  /// format 2, and scripts of format 3 other than the six install and uninstall hooks (e.g.
  /// pre_pack, or custom scripts).
  public static IReadOnlyList<string> DroppedFields(JsonElement root, PackageManifest tooth) {
    List<string> notes = [];
    switch (ToothCompat.FormatVersion(root)) {
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
          notes.Add("prerequisites: has no equivalent in lip.yaml and was dropped; declare the "
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
                notes.Add($"variants[{i}].scripts.{script.Name}: lip.yaml only has install and "
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

  /// The UTF-8 bytes of a package file as lip writes it: a YAML file (YamlJson), ending with a
  /// line break.
  public static byte[] Serialize(JsonObject document) =>
      Encoding.UTF8.GetBytes(YamlJson.Serialize(document));
}
