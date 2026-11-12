using System.Globalization;
using System.Text.Json;
using Flurl.Http;

namespace Lip.Core.Catalog;

/// A mod of the catalogue (protocol/lip-tui.md, "Catalogue"), as index.json lists it. Id is a
/// package argument that names the mod.
public sealed record CatalogMod(
    string Id,
    string Name,
    string Description,
    string Owner,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Sides,
    IReadOnlyList<string> Platforms,
    IReadOnlyList<string> GameVersions,
    IReadOnlyDictionary<string, string> Dependencies,
    int Stars,
    DateTimeOffset? Updated,
    DateTimeOffset? Created,
    int RequiredBy,
    string Url) {
  /// Whether the latest version of the mod has an entry for the side.
  public bool RunsOn(string side) => Sides.Contains(side, StringComparer.OrdinalIgnoreCase);
}

/// One version of a mod, as its file of the catalogue lists it.
public sealed record CatalogVersion(string Version, bool Prerelease, DateTimeOffset? Released);

/// What the file of a mod adds to its entry in index.json.
public sealed record CatalogDetail(
    string Id,
    string? License,
    string? Repository,
    string? LatestVersion,
    string? LatestVariant,
    IReadOnlyList<CatalogVersion> Versions,
    string? Readme);

/// The catalogue as index.json lists it, in its order.
public sealed record CatalogIndex(IReadOnlyList<CatalogMod> Mods);

/// Thrown when a file of the catalogue cannot be read.
public class CatalogUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);

/// Reads the catalogue (protocol/lip-tui.md, "Catalogue") with HTTPS GETs, whatever github_proxy
/// is, and keeps nothing on disk.
public sealed class ModCatalogClient(Func<string, CancellationToken, Task<string>>? get = null) {
  public const string BaseUrl = "https://levimc.org/api/v4/mods/";

  private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(20);

  private readonly Func<string, CancellationToken, Task<string>> _get = get ?? Get;

  /// The URL of the file of a mod.
  public static string DetailUrl(string id) => $"{BaseUrl}{id.Trim('/')}/index.json";

  public async Task<CatalogIndex> GetIndex(CancellationToken cancellationToken = default) {
    using JsonDocument document = await Read(BaseUrl + "index.json", cancellationToken);
    List<CatalogMod> mods = [];
    HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
    if (document.RootElement.ValueKind == JsonValueKind.Object
        && document.RootElement.TryGetProperty("mods", out JsonElement list)
        && list.ValueKind == JsonValueKind.Array) {
      foreach (JsonElement mod in list.EnumerateArray()) {
        string id = String(mod, "id") ?? "";
        if (id.Length == 0 || !seen.Add(id)) {
          continue;
        }

        mods.Add(new CatalogMod(
            id,
            String(mod, "name") ?? id[(id.LastIndexOf('/') + 1)..],
            String(mod, "description") ?? "",
            String(mod, "owner") ?? "",
            Strings(mod, "tags"),
            Strings(mod, "side"),
            Strings(mod, "platform"),
            Strings(mod, "gameVersions"),
            StringMap(mod, "deps"),
            Number(mod, "stars"),
            Time(mod, "updated"),
            Time(mod, "created"),
            Number(mod, "requiredBy"),
            String(mod, "url") ?? ""));
      }
    }

    return new CatalogIndex(mods);
  }

  public async Task<CatalogDetail> GetDetail(
      string id, CancellationToken cancellationToken = default) {
    using JsonDocument document = await Read(DetailUrl(id), cancellationToken);
    JsonElement root = document.RootElement;
    if (root.ValueKind != JsonValueKind.Object) {
      throw new CatalogUnavailableException($"The catalogue file of {id} is not a JSON object.");
    }

    List<CatalogVersion> versions = [];
    if (root.TryGetProperty("versions", out JsonElement list)
        && list.ValueKind == JsonValueKind.Array) {
      foreach (JsonElement version in list.EnumerateArray()) {
        if (String(version, "version") is { Length: > 0 } name) {
          versions.Add(new CatalogVersion(
              name,
              version.TryGetProperty("prerelease", out JsonElement prerelease)
                  && prerelease.ValueKind == JsonValueKind.True,
              Time(version, "released")));
        }
      }
    }

    JsonElement latest = root.TryGetProperty("latest", out JsonElement l) ? l : default;
    return new CatalogDetail(
        String(root, "id") ?? id,
        String(root, "license"),
        String(root, "repository"),
        latest.ValueKind == JsonValueKind.Object ? String(latest, "version") : null,
        latest.ValueKind == JsonValueKind.Object ? String(latest, "variant") : null,
        versions,
        Readme(root));
  }

  /// The README in English when there is one, otherwise the first.
  private static string? Readme(JsonElement root) {
    if (!root.TryGetProperty("readmes", out JsonElement readmes)
        || readmes.ValueKind != JsonValueKind.Array) {
      return null;
    }

    string? first = null;
    foreach (JsonElement readme in readmes.EnumerateArray()) {
      if (String(readme, "text") is not { Length: > 0 } text) {
        continue;
      }

      if (String(readme, "lang") == "en") {
        return text;
      }

      first ??= text;
    }

    return first;
  }

  private async Task<JsonDocument> Read(string url, CancellationToken cancellationToken) {
    try {
      return JsonDocument.Parse(await _get(url, cancellationToken));
    }
    catch (Exception ex) when (ex is not OperationCanceledException
        || !cancellationToken.IsCancellationRequested) {
      throw new CatalogUnavailableException(
          $"Could not read the mod catalogue at {url}: {ex.Message}", ex);
    }
  }

  private static async Task<string> Get(string url, CancellationToken cancellationToken) =>
      await url.WithTimeout(_timeout).GetStringAsync(cancellationToken: cancellationToken);

  private static string? String(JsonElement element, string name) =>
      element.ValueKind == JsonValueKind.Object
          && element.TryGetProperty(name, out JsonElement value)
          && value.ValueKind == JsonValueKind.String
          ? value.GetString()
          : null;

  private static int Number(JsonElement element, string name) =>
      element.TryGetProperty(name, out JsonElement value)
          && value.ValueKind == JsonValueKind.Number
          && value.TryGetDouble(out double number)
          ? (int)number
          : 0;

  private static DateTimeOffset? Time(JsonElement element, string name) =>
      DateTimeOffset.TryParse(
          String(element, name), CultureInfo.InvariantCulture,
          DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
          out DateTimeOffset time)
          ? time
          : null;

  private static List<string> Strings(JsonElement element, string name) {
    List<string> values = [];
    if (element.TryGetProperty(name, out JsonElement list)
        && list.ValueKind == JsonValueKind.Array) {
      foreach (JsonElement value in list.EnumerateArray()) {
        if (value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } s) {
          values.Add(s);
        }
      }
    }

    return values;
  }

  private static Dictionary<string, string> StringMap(JsonElement element, string name) {
    Dictionary<string, string> values = new(StringComparer.Ordinal);
    if (element.TryGetProperty(name, out JsonElement map)
        && map.ValueKind == JsonValueKind.Object) {
      foreach (JsonProperty property in map.EnumerateObject()) {
        if (property.Value.ValueKind == JsonValueKind.String) {
          values[property.Name] = property.Value.GetString() ?? "";
        }
      }
    }

    return values;
  }
}

/// How the mods of the catalogue are ordered.
public enum CatalogSort {
  /// The order of index.json; with a search, the best match first.
  Relevance,
  Stars,
  Updated,
  Newest,
}

/// Which mods of the catalogue to select: those that have an entry for Side (when not null),
/// carry every tag of IncludeTags and none of ExcludeTags (tags as CatalogQuery.DisplayTags gives
/// them), and run on one of GameVersions (when there are any).
public sealed record CatalogFilter(
    string? Side,
    IReadOnlyCollection<string> IncludeTags,
    IReadOnlyCollection<string> ExcludeTags,
    IReadOnlyCollection<string> GameVersions) {
  public static CatalogFilter For(string? side) => new(side, [], [], []);

  public bool Matches(CatalogMod mod) {
    if (Side is not null && !mod.RunsOn(Side)) {
      return false;
    }

    if (GameVersions.Count > 0 && !mod.GameVersions.Any(GameVersions.Contains)) {
      return false;
    }

    if (IncludeTags.Count == 0 && ExcludeTags.Count == 0) {
      return true;
    }

    IReadOnlyList<string> tags = CatalogQuery.DisplayTags(mod);
    return IncludeTags.All(tags.Contains) && !ExcludeTags.Any(tags.Contains);
  }
}

/// A tag that groups other tags, as levimc.org/mods lists them first.
public sealed record FeaturedTag(string Id, string Label, IReadOnlyList<string> Aliases);

/// Searching, filtering and ordering the mods of the catalogue, as levimc.org/mods does.
public static class CatalogQuery {
  /// The featured tags of levimc.org/mods, in its order, with the tags that count as each.
  public static IReadOnlyList<FeaturedTag> FeaturedTags { get; } = [
    new("library", "Library / API",
        ["api", "modapi", "nativeapi", "event", "export", "form", "pland:library"]),
    new("utility", "Utility",
        ["tools", "essential", "inventory", "waila", "info-display", "tooltip", "recipe-viewer",
          "item-list"]),
    new("management", "Server management",
        ["backup", "ban", "whitelist", "login", "command", "land", "checkbag"]),
    new("game-mechanics", "Game mechanics", ["gameplay", "vanilla-plus", "achievement"]),
    new("adventure", "Content & adventure", ["worldgen", "terrain", "biome"]),
    new("visual", "Visual & HUD", ["hud", "sidebar", "shader", "replay", "recording"]),
    new("economy", "Economy", ["money"]),
    new("teleport", "Teleport", ["tp", "waystones"]),
    new("anticheat", "Anti-cheat", ["antixray"]),
    new("optimization", "Optimization", ["performance", "cleaner"]),
    new("bugfix", "Bug fixes", ["fix"]),
    new("technology", "Technical & redstone", ["redstone", "piston", "automation"]),
    new("protocol", "Protocol & multi-version", ["multiversion"]),
  ];

  private static readonly Dictionary<string, FeaturedTag> _featured = FeaturedTags
      .SelectMany(tag => tag.Aliases.Append(tag.Id).Select(alias => (alias, tag)))
      .ToDictionary(pair => pair.alias, pair => pair.tag, StringComparer.Ordinal);

  // Tags nearly every mod carries, or that say what the side and the platform already do.
  private static readonly HashSet<string> _hidden = new(StringComparer.Ordinal) {
    "levilamina", "ll", "mod", "plugin", "native", "client", "server", "minecraft-bedrock",
  };

  /// The tags of a mod as they are shown and filtered: lower case, without the hidden ones, a
  /// tag of a featured group replaced by the id of the group, each once.
  public static IReadOnlyList<string> DisplayTags(CatalogMod mod) {
    List<string> tags = [];
    foreach (string raw in mod.Tags) {
      string tag = raw.Trim().ToLowerInvariant();
      if (tag.Length == 0 || _hidden.Contains(tag)
          || (!_featured.ContainsKey(tag)
              && (tag.StartsWith("platform:", StringComparison.Ordinal)
                  || tag.StartsWith("type:", StringComparison.Ordinal)))) {
        continue;
      }

      string shown = _featured.TryGetValue(tag, out FeaturedTag? group) ? group.Id : tag;
      if (!tags.Contains(shown)) {
        tags.Add(shown);
      }
    }

    return tags;
  }

  /// The label of a tag as DisplayTags gives it.
  public static string Label(string tag) =>
      _featured.TryGetValue(tag, out FeaturedTag? group) ? group.Label : tag;

  /// Whether a tag, as DisplayTags gives it, is a featured tag.
  public static bool IsFeatured(string tag) => _featured.ContainsKey(tag);

  /// The mods that the filter selects and that match every word of the search, ordered by sort.
  public static IReadOnlyList<CatalogMod> Select(
      CatalogIndex index, string search, CatalogFilter filter, CatalogSort sort) {
    string[] words = search.Split(
        [' ', ',', '，', '、', '\t'], StringSplitOptions.RemoveEmptyEntries);
    List<(CatalogMod Mod, int Score, int Order)> matches = [];
    for (int order = 0; order < index.Mods.Count; order++) {
      CatalogMod mod = index.Mods[order];
      if (!filter.Matches(mod)) {
        continue;
      }

      int score = 0;
      bool all = true;
      foreach (string word in words) {
        int quality = Match(mod, word);
        all &= quality > 0;
        score += quality;
      }

      if (all) {
        matches.Add((mod, score, order));
      }
    }

    IOrderedEnumerable<(CatalogMod Mod, int Score, int Order)> ordered = sort switch {
      CatalogSort.Stars => matches.OrderByDescending(m => m.Mod.Stars),
      CatalogSort.Updated => matches.OrderByDescending(m => m.Mod.Updated),
      CatalogSort.Newest => matches.OrderByDescending(m => m.Mod.Created),
      _ => matches.OrderByDescending(m => m.Score),
    };
    return [.. ordered.ThenBy(m => m.Order).Select(m => m.Mod)];
  }

  /// How well a mod matches a word: its name before its id and owner, before its tags, before
  /// its description; 0 when it does not.
  private static int Match(CatalogMod mod, string word) {
    const StringComparison ignoreCase = StringComparison.OrdinalIgnoreCase;
    if (mod.Name.Equals(word, ignoreCase)) {
      return 100;
    }

    if (mod.Name.StartsWith(word, ignoreCase)) {
      return 60;
    }

    if (mod.Name.Contains(word, ignoreCase)) {
      return 40;
    }

    if (mod.Id.Contains(word, ignoreCase) || mod.Owner.Contains(word, ignoreCase)) {
      return 30;
    }

    if (DisplayTags(mod).Any(tag =>
        tag.Contains(word, ignoreCase) || Label(tag).Contains(word, ignoreCase))) {
      return 20;
    }

    return mod.Description.Contains(word, ignoreCase) ? 10 : 0;
  }
}
