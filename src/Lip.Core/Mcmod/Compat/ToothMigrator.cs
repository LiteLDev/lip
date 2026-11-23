using System.Text;

using System.Text.Json;
using System.Text.Json.Nodes;
using DotNet.Globbing;
using Lip.Core.Entities;
using Lip.Core.Infrastructure;
using Lip.Core.Json;
using Semver;

namespace Lip.Core.Mcmod.Compat;

/// Converts a tooth.json of format 1–3 (already read as format 3) into a package file (lip pkg
/// migrate, protocol/lip-compatibility.md, "lip pkg migrate"), agreeing with how
/// protocol/lip-compatibility.md, "tooth.json" reads it for every environment it has an entry for.
/// The notes list what is dropped or converted with another meaning, each naming the field of
/// format 3 it comes from. Missing values, and dependency ranges that are not ranges of a package
/// file, are asked for through IUserInteraction.Ask.
///
/// - The platform of a tooth.json variant is a RID pattern, while that of an entry of a package
///   file is a RID. A package file has no RID patterns and no "any platform": the RIDs a platform
///   pattern stands for, those a platform that is not a runtime identifier (e.g. "windows/amd64")
///   stands for, and those the variants without a platform apply to, are asked for.
/// - Each label becomes a variant. It has an entry for each RID, holding the variants of the
///   label merged for the RID and converted exactly as Section 2 converts them
///   (ToothCompat.ConvertEntry); a RID whose merged variants have an asset of type tar, tgz
///   or uncompressed has no entry.
/// - The side of an entry follows the dependency of its merged variants on
///   github.com/LiteLDev/LeviLamina: the client for its variant "client" (or "client_y"), the
///   server for any other variant, and both, as two entries, without such a dependency.
/// - Then the client variants take the form LeviLamina has had since its first package file
///   (MoveToLeviLaminaPackageFile), so the migrated file can be installed on a client together
///   with current versions of LeviLamina. Where it does, the file does not agree with the
///   tooth.json for the client: the tooth.json has the variant "client" (client_y), whose
///   entries become ones of the variant "" (y).
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
            + "info.logo of lip.toml must; it was dropped. Set info.logo to the https URL of the "
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

    // The RIDs every platform pattern stands for, asked once per pattern, and the RIDs the
    // variants without a platform apply to, asked once.
    Dictionary<string, List<string>> patterns = new(StringComparer.Ordinal);
    foreach (PackageManifestVariant variant in tooth.Variants) {
      if (IsPattern(variant.Platform) && !patterns.ContainsKey(variant.Platform)) {
        patterns[variant.Platform] = await AskRids(
            ui,
            $"the platforms (RIDs, comma-separated) the platform pattern '{variant.Platform}' "
                + "stands for",
            variant.Platform);
        RunTrace.Write("migrate", $"platform pattern '{variant.Platform}' stands for "
            + string.Join(", ", patterns[variant.Platform]));
      }
    }

    // The RIDs every platform that is neither a pattern nor a runtime identifier stands for,
    // asked once per platform; its variants become one variant for each of them.
    Dictionary<string, List<string>> literals = new(StringComparer.Ordinal);
    foreach (PackageManifestVariant variant in tooth.Variants) {
      if (variant.Platform != "" && !IsPattern(variant.Platform)
          && !PackageFileParser.IsPlatform(variant.Platform)
          && !literals.ContainsKey(variant.Platform)) {
        literals[variant.Platform] = await AskRids(
            ui,
            $"the platforms (RIDs, comma-separated, e.g. win-x64) the platform '{variant.Platform}', "
                + "which is not a runtime identifier, stands for",
            null);
        RunTrace.Write("migrate", $"platform '{variant.Platform}' stands for "
            + string.Join(", ", literals[variant.Platform]));
      }
    }

    if (literals.Count > 0) {
      tooth = tooth with {
        Variants = [
          .. tooth.Variants.SelectMany(v => literals.TryGetValue(v.Platform, out List<string>? rs)
              ? rs.Select(r => v with { Platform = r })
              : [v]),
        ],
      };
    }

    List<string> anyRid = tooth.Variants.Any(v => v.Platform == "")
        ? await AskRids(
            ui,
            "the platforms (RIDs, comma-separated, e.g. win-x64) the variants without a platform "
                + "apply to",
            null)
        : [];
    if (anyRid.Count > 0) {
      RunTrace.Write("migrate", "variants without a platform apply to "
          + string.Join(", ", anyRid));
    }

    JsonObject variants = [];
    void AddEntry(string variant, JsonObject entry) {
      if (variants[variant] is not JsonArray entries) {
        entries = [];
        variants[variant] = entries;
      }

      entries.Add((JsonNode)entry);
    }

    foreach (string label in tooth.Variants.Select(v => v.Label).Distinct()) {
      List<PackageManifestVariant> labeled = [.. tooth.Variants.Where(v => v.Label == label)];
      List<string> rids = [
        .. labeled
            .SelectMany(v => v.Platform == "" ? anyRid
                : IsPattern(v.Platform) ? patterns[v.Platform]
                : [v.Platform])
            .Distinct(StringComparer.Ordinal),
      ];

      foreach (string rid in rids) {
        PackageManifestVariant merged = Merge(tooth, label, rid);
        if (await Content(context, merged, rid,
            $"variants (label '{label}', {rid})") is not JsonObject content) {
          RunTrace.Write("migrate", $"variant '{label}' has no entry for {rid}");
          continue;
        }

        foreach (McmodSide side in SidesOf(merged)) {
          RunTrace.Write("migrate",
              $"variant '{label}': entry for {side.ToSpecString()} {rid}");
          AddEntry(label, Entry(side, rid, content));
        }
      }
    }

    MoveToLeviLaminaPackageFile(variants, context.Notes);
    return variants;

    static JsonObject Entry(McmodSide side, string rid, JsonObject content) {
      JsonObject entry = new() {
        ["side"] = side.ToSpecString(),
        ["platform"] = rid,
      };
      foreach ((string key, JsonNode? value) in content) {
        entry[key] = value?.DeepClone();
      }

      return entry;
    }
  }

  /// The sides of the entries for merged variants: the client when they depend on a client
  /// variant of LeviLamina ("client", or "client_" followed by a name), the server when they
  /// depend on another variant of it, and both when they do not depend on it.
  private static McmodSide[] SidesOf(PackageManifestVariant merged) {
    List<PackageId> leviLamina = [
      .. merged.Dependencies.Keys.Where(id =>
          string.Equals(id.Path, LeviLaminaPath, StringComparison.OrdinalIgnoreCase)),
    ];
    return leviLamina.Count == 0 ? [McmodSide.Server, McmodSide.Client]
        : leviLamina.Any(id => ServerVariantOf(id.Variant) is not null) ? [McmodSide.Client]
        : [McmodSide.Server];
  }

  private const string LeviLaminaPath = "github.com/LiteLDev/LeviLamina";
  private const string ClientVariant = "client";

  /// The first version of LeviLamina with a package file. It has no variant "client": its
  /// variant "" has an entry for each side. Older versions have a tooth.json, whose label
  /// "client" holds the client build and whose label "" the server build.
  private static readonly SemVersion _firstLeviLaminaPackageFile = new(26, 60, 8);

  /// For a client variant of the tooth.json era ("client", or "client_" followed by y), the
  /// variant of a package file that holds its client entries ("", or y); null for any other
  /// variant.
  private static string? ServerVariantOf(string variant) =>
      variant == ClientVariant ? ""
      : variant.StartsWith(ClientVariant + "_", StringComparison.Ordinal)
          && variant.Length > ClientVariant.Length + 1
          ? variant[(ClientVariant.Length + 1)..]
      : null;

  /// Moves the migrated variants from the client variants of the tooth.json era to the form
  /// LeviLamina has had since its first package file, which has no variant "client" and whose
  /// variant "" has an entry for each side:
  ///
  /// - A dependency on github.com/LiteLDev/LeviLamina#client (#client_y) whose range admits a
  ///   version with a package file becomes one on github.com/LiteLDev/LeviLamina (#y), its range
  ///   raised to such versions: on a client, the variant "" of an older version, read from its
  ///   tooth.json, is the server build. A range that admits only older versions is kept, as
  ///   those versions still have the variant "client".
  /// - Then each client entry of the variant "client" (client_y) with such a dependency moves to
  ///   the variant "" (y), unless that has an entry for the client and the same platform.
  ///
  /// Dependencies on client variants of other packages are kept, with a note: whether a package
  /// still has such a variant cannot be known without fetching it.
  private static void MoveToLeviLaminaPackageFile(JsonObject variants, List<string> notes) {
    // The entries whose dependency on LeviLamina was moved, with the variant of LeviLamina it
    // was moved to.
    Dictionary<JsonObject, string> moved = new(ReferenceEqualityComparer.Instance);
    HashSet<(string, string)> noted = [];
    foreach ((string label, JsonNode? node) in variants) {
      foreach (JsonObject entry in node!.AsArray().Cast<JsonObject>()) {
        if (entry["deps"] is not JsonObject deps) {
          continue;
        }

        string field = $"variants.{(label == "" ? "\"\"" : label)} ({entry["side"]}, "
            + $"{entry["platform"]}).deps";
        foreach ((string key, JsonNode? value) in deps.ToList()) {
          int hash = key.LastIndexOf('#');
          string path = hash < 0 ? key : key[..hash];
          string variant = hash < 0 ? "" : key[(hash + 1)..];
          if (ServerVariantOf(variant) is not string target) {
            continue;
          }

          if (!string.Equals(path, LeviLaminaPath, StringComparison.OrdinalIgnoreCase)) {
            if (noted.Add((label, key))) {
              notes.Add($"{field}[\"{key}\"]: was kept. If {path} has moved to lip.toml "
                  + $"without the variant \"{variant}\", depend on the variant of it that has a "
                  + "client entry instead.");
            }

            continue;
          }

          string text = value!.GetValue<string>();
          string? raised = RaiseToPackageFile(text);
          if (raised is null) {
            continue;
          }

          string newKey = target == "" ? path : $"{path}#{target}";
          if (deps.Any(d => SameDependency(d.Key, newKey))) {
            notes.Add($"{field}[\"{key}\"]: was kept, as the entry also depends on {newKey}.");
            continue;
          }

          deps.Remove(key);
          deps[newKey] = raised;
          moved[entry] = target;
          notes.Add($"{field}[\"{key}\"]: became \"{newKey}\" = \"{raised}\". Since "
              + $"{_firstLeviLaminaPackageFile}, LeviLamina has no variant \"{variant}\"; its "
              + $"variant \"{target}\" has the client build."
              + (raised == text ? ""
                  : $" The range '{text}' was raised to '{raised}', as on a client the variant "
                      + $"\"{target}\" of earlier versions is the server build; this entry no "
                      + "longer installs with those."));
        }
      }
    }

    foreach (string label in variants.Select(v => v.Key).ToList()) {
      if (ServerVariantOf(label) is not string target) {
        continue;
      }

      JsonArray from = variants[label]!.AsArray();
      List<JsonObject> movable = [
        .. from.Cast<JsonObject>().Where(e => moved.TryGetValue(e, out string? to) && to == target
            && e["side"]!.GetValue<string>() == McmodSide.Client.ToSpecString()),
      ];
      if (movable.Count == 0) {
        continue;
      }

      string targetName = target == "" ? "\"\"" : $"\"{target}\"";
      if (variants[target] is not JsonArray to) {
        to = [];
        variants[target] = to;
      }

      foreach (JsonObject entry in movable) {
        string platform = entry["platform"]!.GetValue<string>();
        if (to.Cast<JsonObject>().Any(e =>
            e["side"]!.GetValue<string>() == McmodSide.Client.ToSpecString()
            && e["platform"]!.GetValue<string>() == platform)) {
          notes.Add($"variants.{label} (client, {platform}): was kept, as the variant "
              + $"{targetName} already has an entry for the client and {platform}. Since "
              + $"{_firstLeviLaminaPackageFile}, a client installs the variant {targetName} of "
              + $"LeviLamina, not \"{label}\".");
          continue;
        }

        from.Remove(entry);
        to.Add((JsonNode)entry);
        notes.Add($"variants.{label} (client, {platform}): became an entry of the variant "
            + $"{targetName}, as LeviLamina has the client build in its variant {targetName} "
            + $"since {_firstLeviLaminaPackageFile}.");
      }

      if (to.Count == 0) {
        variants.Remove(target);
      }

      if (from.Count == 0) {
        variants.Remove(label);
      }
    }
  }

  /// Whether two dependency keys name the same package and variant: package ids and variants are
  /// compared ignoring ASCII case (protocol/lip-package.md, "Terms").
  private static bool SameDependency(string a, string b) => PackageNames.AreEqual(a, b);

  /// The range of the versions of a range from the first one of LeviLamina with a package file
  /// on, written as Semver writes it, or the range as written when it admits no earlier version;
  /// null when it admits no version from that one on.
  private static string? RaiseToPackageFile(string text) {
    SemVersionRange range = SemVersionRange.Parse(text);
    SemVersion first = _firstLeviLaminaPackageFile;
    List<UnbrokenSemVersionRange> raised = [];
    bool changed = false;
    foreach (UnbrokenSemVersionRange part in range) {
      if (part.Start is SemVersion start && start.ComparePrecedenceTo(first) >= 0) {
        raised.Add(part);
        continue;
      }

      if (part.End is SemVersion end) {
        int c = end.ComparePrecedenceTo(first);
        if (c < 0 || (c == 0 && !part.EndInclusive)) {
          changed = true;
          continue;
        }
      }

      changed = true;
      raised.Add(part.End is SemVersion e
          ? part.EndInclusive
              ? UnbrokenSemVersionRange.Inclusive(first, e, part.IncludeAllPrerelease)
              : UnbrokenSemVersionRange.InclusiveOfStart(first, e, part.IncludeAllPrerelease)
          : UnbrokenSemVersionRange.AtLeast(first, part.IncludeAllPrerelease));
    }

    return raised.Count == 0 ? null
        : changed ? SemVersionRange.Create(raised).ToString()
        : text;
  }

  /// The variants of a label merged for a RID, as protocol/lip-compatibility.md, "tooth.json"
  /// merges them.
  private static PackageManifestVariant Merge(PackageManifest tooth, string label, string rid) {
    try {
      return tooth.GetVariant(label, rid);
    }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
        or KeyNotFoundException) {
      throw new InvalidOperationException(
          $"Cannot migrate tooth.json: the variants labeled '{label}' cannot be merged for "
          + $"{rid}, because {ToothCompat.MergeFailureReason(ex)}; lip v0.37.0 could not "
          + $"install them for {rid} either. Fix these variants in tooth.json so that each "
          + $"dependency is declared, and each kind of script is set, in only one of the variants "
          + $"that apply to {rid}, then run 'lip pkg migrate' again.", ex);
    }
  }

  /// The content of an entry for a merged variant, converted as protocol/lip-compatibility.md,
  /// "tooth.json" converts it (ToothCompat.ConvertEntry), without side and platform, with each
  /// dependency range that is not a range of a package file asked for; null when the variant has no
  /// entry for the RID.
  private static async Task<JsonObject?> Content(
      Context context, PackageManifestVariant variant, string rid, string field) {
    List<string> notes = context.Notes;
    if (ToothCompat.ConvertEntry(variant, McmodSide.Server, rid, notes, field)
        is not JsonObject content) {
      return null;
    }

    content.Remove("side");
    content.Remove("platform");

    foreach ((PackageId dependency, SemVersionRange range) in variant.Dependencies) {
      string dependencyField = $"{field}.dependencies[\"{dependency}\"]";
      string text = range.ToString();
      if (!VersionRange.TryParse(text, out _)) {
        if (!context.Ranges.TryGetValue((dependency, text), out string? answer)) {
          answer = await context.Ui.AskValid(
              $"a version range of lip.toml for {dependencyField}, whose range '{text}' is not one",
              a => VersionRange.TryParse(a, out _) ? null : $"'{a}' is not a version range.");
          context.Ranges[(dependency, text)] = answer;
        }

        content["deps"]![dependency.ToString()] = answer;
      }

    }

    foreach (string pattern in variant.PreserveFiles.Select(GlobListJsonConverter.Format)
        .Where(p => !p.Replace('\\', '/').Contains('/'))) {
      notes.Add($"{field}.preserve_files: '{pattern}' has no '/', so tooth.json also matched it "
          + $"against file names in every directory; it became '**/{pattern}'.");
    }

    return content;
  }

  /// Asks for a non-empty list of RIDs, each matching pattern when it is given.
  private static async Task<List<string>> AskRids(
      IUserInteraction ui, string question, string? pattern) {
    Glob? glob = pattern is null ? null : Glob.Parse(pattern);
    string answer = await ui.AskValid(question, a => {
      List<string> rids = SplitList(a);
      if (rids.Count == 0) {
        return "enter at least one RID, e.g. win-x64.";
      }

      if (rids.FirstOrDefault(r => !PackageFileParser.IsPlatform(r)) is string invalid) {
        return $"'{invalid}' is not a runtime identifier.";
      }

      return glob is not null && rids.FirstOrDefault(r => !glob.IsMatch(r)) is string other
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
  /// does not keep and that therefore cannot reach the migrated lip.toml: dependencies of
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
          notes.Add("prerequisites: has no equivalent in lip.toml and was dropped; declare the "
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
                notes.Add($"variants[{i}].scripts.{script.Name}: lip.toml only has install and "
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

  /// The UTF-8 bytes of a package file as lip writes it: a TOML file (TomlJson), ending with a
  /// line break. Throws PackageFormatException when the document holds a null, which TOML
  /// cannot hold.
  public static byte[] Serialize(JsonObject document) {
    try {
      return Encoding.UTF8.GetBytes(TomlJson.Serialize(document));
    }
    catch (ArgumentException ex) {
      throw new PackageFormatException(
          "The converted file holds a null value, which a TOML file cannot hold.", ex);
    }
  }
}
