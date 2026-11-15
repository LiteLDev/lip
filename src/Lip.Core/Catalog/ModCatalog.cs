using System.Globalization;
using System.Text.Json;
using Flurl.Http;
using Lip.Core.Mcmod;
using Semver;

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
    string Url,
    string? License = null) {
  /// How many times the mod was downloaded; null when index.json does not say, which is not
  /// the same as never.
  public long? Downloads { get; init; }

  /// How popular the mod is among the mods of the catalogue, as levimc.org/mods orders them
  /// (CatalogPopularity); 0 for a mod that was not read from index.json.
  public double Popularity { get; init; }

  /// Whether the latest version of the mod has an entry for the side.
  public bool RunsOn(string side) => Sides.Contains(side, StringComparer.OrdinalIgnoreCase);
}

/// One entry of a variant of a version of a mod: what the variant is built for, and the
/// packages that it depends on, by their ids, with the range of each. Variant is "" for the
/// default variant.
public sealed record CatalogBuild(
    string Variant, string Side, string Platform, IReadOnlyDictionary<string, string> Dependencies);

/// One version of a mod, as its file of the catalogue lists it, with the entries of all of its
/// variants; there is none when the file does not say which variants the version has.
public sealed record CatalogVersion(
    string Version, bool Prerelease, DateTimeOffset? Released, IReadOnlyList<CatalogBuild> Builds) {
  /// The variants of the version that have an entry for a server, the default variant ("")
  /// first; the default variant alone when the file does not say.
  public IReadOnlyList<string> Variants => Builds.Count == 0
      ? [""]
      : [
        .. Builds.Where(build => build.Side == "server").Select(build => build.Variant)
            .Distinct(StringComparer.Ordinal).OrderBy(variant => variant.Length == 0 ? 0 : 1),
      ];

  /// The entries of a variant for a server.
  public IEnumerable<CatalogBuild> ServerBuilds(string variant) =>
      Builds.Where(build => build.Side == "server" && build.Variant == variant);
}

/// What the file of a mod adds to its entry in index.json.
public sealed record CatalogDetail(
    string Id,
    string? License,
    string? Repository,
    string? LatestVersion,
    string? LatestVariant,
    IReadOnlyList<CatalogVersion> Versions,
    string? Readme) {
  /// Every variant that a version of the mod has an entry for a server in, the default variant
  /// ("") first, the others as the file first names them; the default variant alone when the
  /// file lists no version.
  public IReadOnlyList<string> ServerVariants() {
    List<string> variants = [
      .. Versions.SelectMany(version => version.Variants).Distinct(StringComparer.Ordinal)
          .OrderBy(variant => variant.Length == 0 ? 0 : 1),
    ];
    return variants.Count == 0 ? [""] : variants;
  }
}

/// The catalogue as index.json lists it, in its order.
public sealed record CatalogIndex(IReadOnlyList<CatalogMod> Mods) {
  /// The mod of the catalogue that an installed package is of: the one whose id is the
  /// identity of the package without its variant (what follows #), whatever the case of the
  /// ASCII letters; null when the catalogue has no such mod.
  public CatalogMod? Find(string identity) {
    string id = identity.Split('#')[0];
    return Mods.FirstOrDefault(mod => PackageNames.AreEqual(mod.Id, id));
  }
}

/// Thrown when a file of the catalogue cannot be read.
public class CatalogUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);

/// Reads the catalogue (protocol/lip-tui.md, "Catalogue") with HTTPS GETs, whatever github_proxy
/// is, and keeps nothing on disk.
///
/// What was read is kept in memory, so that everything that shows the catalogue shares it:
/// index.json for ten minutes (StaleAfter), and the file of a mod as long, or until
/// index.json is read again. What could not be read is not kept: the next call reads again.
/// Calls that ask for a file while it is read share the one request. A call that is
/// cancelled stops waiting, and the request is abandoned once no call waits for it.
///
/// get reads the text at a URL: an HTTPS GET when not given. clock tells the time in
/// milliseconds: that of the system when not given.
public sealed class ModCatalogClient(
    Func<string, CancellationToken, Task<string>>? get = null, Func<long>? clock = null) {
  public const string BaseUrl = "https://levimc.org/api/v4/mods/";

  /// How long a file of the catalogue that was read is taken to be what the catalogue holds.
  public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);

  private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(20);

  /// A file of the catalogue that is read, or was: the one request for it, what abandons the
  /// request, how many calls wait for it, and when it was read, null until then.
  private sealed class Entry<T>(CancellationTokenSource cancel) {
    public Task<T> Task { get; set; } = null!;
    public CancellationTokenSource Cancel { get; } = cancel;
    public int Waiters { get; set; }
    public long? ReadAt { get; set; }
  }

  private readonly Func<string, CancellationToken, Task<string>> _get = get ?? Get;
  private readonly Func<long> _clock = clock ?? (() => Environment.TickCount64);
  private readonly Lock _gate = new();
  private readonly Dictionary<string, Entry<CatalogDetail>> _details =
      new(StringComparer.OrdinalIgnoreCase);
  private Entry<CatalogIndex>? _index;

  /// The URL of the file of a mod.
  public static string DetailUrl(string id) => $"{BaseUrl}{id.Trim('/')}/index.json";

  /// The mods of the catalogue: what was read less than ten minutes ago, otherwise index.json
  /// as it is now. The same object is returned until index.json is read again.
  public Task<CatalogIndex> GetIndex(CancellationToken cancellationToken = default) =>
      Shared(
          () => _index,
          entry => _index = entry,
          async token => {
            CatalogIndex index = await ReadIndex(token);
            lock (_gate) {
              // The files of the mods are those of the index that names them: once it is
              // read again, they are too. An index that cannot be read drops nothing.
              _details.Clear();
            }

            return index;
          },
          cancellationToken);

  /// Whether a file that was read, or is being read, answers a call now.
  private bool Usable<T>(Entry<T> entry) =>
      !entry.Cancel.IsCancellationRequested
      && (!entry.Task.IsCompleted
          || (entry.Task.IsCompletedSuccessfully && entry.ReadAt is { } at
              && _clock() - at <= StaleAfter.TotalMilliseconds));

  /// What find gives when it answers a call now, otherwise what read reads, which store then
  /// keeps; store gets null to drop what it kept. find and store are called under the lock.
  private async Task<T> Shared<T>(
      Func<Entry<T>?> find,
      Action<Entry<T>?> store,
      Func<CancellationToken, Task<T>> read,
      CancellationToken cancellationToken) {
    Entry<T> entry;
    lock (_gate) {
      if (find() is { } kept && Usable(kept)) {
        entry = kept;
      } else {
        Entry<T> started = new(new CancellationTokenSource());
        started.Task = Task.Run(async () => {
          T value = await read(started.Cancel.Token);
          started.ReadAt = _clock();
          return value;
        });
        store(started);
        entry = started;
      }

      entry.Waiters++;
    }

    try {
      return await entry.Task.WaitAsync(cancellationToken);
    }
    finally {
      lock (_gate) {
        entry.Waiters--;
        if (!entry.Task.IsCompletedSuccessfully) {
          if (entry.Waiters == 0 && !entry.Task.IsCompleted) {
            // No call waits for the request any more: it is abandoned.
            entry.Cancel.Cancel();
          }

          // What was not read, or is abandoned, answers no later call.
          if ((entry.Task.IsCompleted || entry.Waiters == 0) && ReferenceEquals(find(), entry)) {
            store(null);
          }
        }
      }
    }
  }

  private async Task<CatalogIndex> ReadIndex(CancellationToken cancellationToken) {
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
            String(mod, "url") ?? "",
            String(mod, "license")) {
          Downloads = Count(mod, "downloads"),
        });
      }
    }

    IReadOnlyList<double> popularity = CatalogPopularity.Scores(mods);
    return new CatalogIndex(
        [.. mods.Select((mod, i) => mod with { Popularity = popularity[i] })]);
  }

  /// Which versions of the game have a version of LeviLamina, as the file of LeviLamina in the
  /// catalogue tells: one request.
  public async Task<LoaderSupport> GetLoaderSupport(
      CancellationToken cancellationToken = default) =>
      new(await GetDetail(GameSetup.LoaderRepository, cancellationToken));

  /// The file of a mod: what was read less than ten minutes ago, and since index.json was
  /// last read, otherwise the file as it is now.
  public Task<CatalogDetail> GetDetail(string id, CancellationToken cancellationToken = default) {
    string key = id.Trim('/');
    return Shared(
        () => _details.GetValueOrDefault(key),
        entry => {
          if (entry is null) {
            _details.Remove(key);
          } else {
            // What is too old to answer a call is not kept for nothing.
            foreach (string stale in _details.Where(pair => !Usable(pair.Value))
                .Select(pair => pair.Key).ToList()) {
              _details.Remove(stale);
            }

            _details[key] = entry;
          }
        },
        token => ReadDetail(id, token), cancellationToken);
  }

  private async Task<CatalogDetail> ReadDetail(string id, CancellationToken cancellationToken) {
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
              Time(version, "released"),
              Builds(version)));
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

  /// The entries of the variants of a version, in the order of the file.
  private static List<CatalogBuild> Builds(JsonElement version) {
    List<CatalogBuild> builds = [];
    if (!version.TryGetProperty("variants", out JsonElement variants)
        || variants.ValueKind != JsonValueKind.Object) {
      return builds;
    }

    foreach (JsonProperty variant in variants.EnumerateObject()) {
      if (variant.Value.ValueKind != JsonValueKind.Array) {
        continue;
      }

      foreach (JsonElement entry in variant.Value.EnumerateArray()) {
        if (entry.ValueKind == JsonValueKind.Object) {
          builds.Add(new CatalogBuild(
              variant.Name, String(entry, "side") ?? "", String(entry, "platform") ?? "",
              StringMap(entry, "deps")));
        }
      }
    }

    return builds;
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

  /// A count that the file may leave out: null then, and for what is not a count.
  private static long? Count(JsonElement element, string name) =>
      element.TryGetProperty(name, out JsonElement value)
          && value.ValueKind == JsonValueKind.Number
          && value.TryGetDouble(out double number)
          && number >= 0
          ? (long)number
          : null;

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

/// How the mods of the catalogue are ordered: the orders of levimc.org/mods, in its order.
public enum CatalogSort {
  /// The order of index.json; with a search, the best match first.
  Relevance,

  /// The most popular first (CatalogPopularity), then the most stars, then the last updated.
  Popularity,

  /// The last updated first, then the most popular.
  Updated,

  /// The last created first, then the most popular.
  Newest,
}

/// How popular the mods of the catalogue are, as levimc.org/mods computes it from index.json.
public static class CatalogPopularity {
  /// The mean and the population standard deviation of ln(1 + value) over values.
  private sealed class Fit {
    private readonly double _mean;
    private readonly double _deviation;

    public Fit(IReadOnlyCollection<long> values) {
      if (values.Count == 0) {
        return;
      }

      double mean = values.Average(value => Math.Log(1 + value));
      _mean = mean;
      _deviation = Math.Sqrt(
          values.Sum(value => Math.Pow(Math.Log(1 + value) - mean, 2)) / values.Count);
    }

    /// How many deviations ln(1 + value) is above the mean; 0 when the values do not differ.
    public double Z(long value) =>
        _deviation > 0 ? (Math.Log(1 + value) - _mean) / _deviation : 0;
  }

  /// The popularity of each of the mods, in their order. With z(x) = (ln(1 + x) - mean) / sd,
  /// where the mean and the population standard deviation are those of ln(1 + x) over the
  /// mods (z is 0 when sd is 0): the mean of z of the stars, fitted on the stars of every mod,
  /// and z of the downloads, fitted on the downloads of the mods that have a count; a mod
  /// without a count of downloads takes z of its stars for both. Rounded to three decimals.
  public static IReadOnlyList<double> Scores(IReadOnlyList<CatalogMod> mods) {
    Fit stars = new([.. mods.Select(mod => (long)mod.Stars)]);
    Fit downloads = new([
      .. mods.Where(mod => mod.Downloads is not null).Select(mod => mod.Downloads!.Value),
    ]);
    return [
      .. mods.Select(mod => {
        double ofStars = stars.Z(mod.Stars);
        double ofDownloads = mod.Downloads is { } count ? downloads.Z(count) : ofStars;
        // Half up, as levimc.org/mods rounds.
        return Math.Floor(((ofStars + ofDownloads) / 2 * 1000) + 0.5) / 1000;
      }),
    ];
  }
}

/// The versions of the game and of LeviLamina that a workspace has; null for what it lacks.
public sealed record CatalogWorkspace(string? Game, string? Loader) {
  /// What the installed packages of a workspace (LipClient.ListPackages) say.
  public static CatalogWorkspace Of(IEnumerable<InstalledPackage> packages) {
    string? game = null;
    string? loader = null;
    foreach (InstalledPackage package in packages) {
      string id = package.Identity.Split('#')[0];
      if (BedrockServer.IsRepository(id)) {
        game ??= package.Version;
      } else if (GameSetup.IsLoaderRepository(id)) {
        loader ??= package.Version;
      }
    }

    return new CatalogWorkspace(game, loader);
  }
}

/// Whether the versions of a mod of the catalogue fit a workspace. This is what the catalogue
/// tells before anything is installed; lip install decides.
public static class CatalogCompatibility {
  /// The platform of every environment (protocol/lip.md, "Environment"): an entry for another
  /// platform is never installed. An entry that names no platform is taken to be for it.
  public const string Platform = "win-x64";

  /// Whether an entry is one that lip can install for a server.
  internal static bool IsForServer(CatalogBuild build) =>
      build.Side == "server" && (build.Platform.Length == 0 || build.Platform == Platform);

  /// Whether a variant of a version of a mod fits the game and the LeviLamina of a workspace.
  /// The version must have an entry of the variant for a server, and in one such entry, of
  /// the platform of lip, every range of a dependency on LeviLamina or on the game must
  /// contain the installed version of it. So a variant that depends on neither fits any
  /// workspace, and one that depends on LeviLamina fits no workspace without LeviLamina. A
  /// version whose variants the file does not list fits, and a range that lip cannot read
  /// rules nothing out.
  public static bool IsCompatible(
      CatalogVersion version, string variant, CatalogWorkspace workspace) {
    if (!version.Variants.Contains(variant)) {
      return false;
    }

    if (version.Builds.Count == 0) {
      return true;
    }

    return version.ServerBuilds(variant).Where(IsForServer).Any(
        build => build.Dependencies.All(dependency => {
          string id = dependency.Key.Split('#')[0];
          return GameSetup.IsLoaderRepository(id) ? Contains(dependency.Value, workspace.Loader)
              : !BedrockServer.IsRepository(id) || Contains(dependency.Value, workspace.Game);
        }));
  }

  /// Whether an entry for a server of a variant of a version of the mod depends on
  /// LeviLamina: what to tell the user when no version fits.
  public static bool NeedsLoader(CatalogDetail detail, string variant) =>
      detail.Versions.Any(version => version.ServerBuilds(variant).Any(
          build => build.Dependencies.Keys.Any(
              id => GameSetup.IsLoaderRepository(id.Split('#')[0]))));

  /// The versions of a mod that fit a workspace in a variant, in the order of the file, which
  /// lists the newest first.
  public static IReadOnlyList<CatalogVersion> Versions(
      CatalogDetail detail, string variant, CatalogWorkspace workspace) =>
      [.. detail.Versions.Where(version => IsCompatible(version, variant, workspace))];

  /// Whether a range contains an installed version; false for nothing installed.
  private static bool Contains(string range, string? installed) {
    if (installed is null) {
      return false;
    }

    return !VersionRange.TryParse(range, out VersionRange? parsed)
        || !SemVersion.TryParse(installed, SemVersionStyles.Any, out SemVersion? version)
        || parsed!.IsSatisfiedBy(version);
  }
}

/// Which versions of the game have a version of LeviLamina, as the file of LeviLamina in the
/// catalogue tells: a version of LeviLamina is for the versions of the game that the range of
/// its dependency on the game contains, in an entry for a server of the platform of lip.
public sealed class LoaderSupport {
  // The versions of LeviLamina in the order of the file, the newest first, each with the
  // ranges of the game that it is for.
  private readonly List<(string Version, bool Prerelease, List<VersionRange> Games)> _versions =
      [];

  /// loader is the file of LeviLamina (GameSetup.LoaderRepository) in the catalogue.
  public LoaderSupport(CatalogDetail loader) {
    foreach (CatalogVersion version in loader.Versions) {
      List<VersionRange> games = [];
      foreach (CatalogBuild build in version.Builds.Where(CatalogCompatibility.IsForServer)) {
        foreach (KeyValuePair<string, string> dependency in build.Dependencies) {
          if (BedrockServer.IsRepository(dependency.Key.Split('#')[0])
              && VersionRange.TryParse(dependency.Value, out VersionRange? range)) {
            games.Add(range!);
          }
        }
      }

      if (games.Count > 0) {
        _versions.Add((version.Version, version.Prerelease, games));
      }
    }
  }

  /// The newest version of LeviLamina for a version of the game, or null when there is none.
  /// Prereleases of LeviLamina do not count, unless no other version is for the game: then the
  /// newest prerelease is the answer.
  public string? VersionFor(SemVersion game) =>
      Newest(game, prerelease: false) ?? Newest(game, prerelease: true);

  /// The same for a version of the game as text; null for what is not a version.
  public string? VersionFor(string game) =>
      SemVersion.TryParse(game, SemVersionStyles.Any, out SemVersion? version)
          ? VersionFor(version)
          : null;

  /// Whether a version of the game has a version of LeviLamina.
  public bool Supports(SemVersion game) => VersionFor(game) is not null;

  /// The same for a version of the game as text.
  public bool Supports(string game) => VersionFor(game) is not null;

  private string? Newest(SemVersion game, bool prerelease) {
    foreach ((string version, bool pre, List<VersionRange> games) in _versions) {
      if (pre == prerelease && games.Any(range => range.IsSatisfiedBy(game))) {
        return version;
      }
    }

    return null;
  }
}

/// Which mods of the catalogue to select: those that have an entry for Side (when not null),
/// carry every tag of IncludeTags and none of ExcludeTags (tags as CatalogQuery.DisplayTags gives
/// them), and run on one of GameVersions (when there are any).
public sealed record CatalogFilter(
    string? Side,
    IReadOnlyCollection<string> IncludeTags,
    IReadOnlyCollection<string> ExcludeTags,
    IReadOnlyCollection<string> GameVersions) {
  /// The license families (CatalogQuery.LicenseFamily) a mod must be under one of, when any.
  public IReadOnlyCollection<string> Licenses { get; init; } = [];

  /// The ids (CatalogQuery.DependencyKey) of the packages that the latest version of a mod must
  /// depend on, every one of them.
  public IReadOnlyCollection<string> IncludeDependencies { get; init; } = [];

  /// The ids of the packages that the latest version of a mod must not depend on.
  public IReadOnlyCollection<string> ExcludeDependencies { get; init; } = [];

  public static CatalogFilter For(string? side) => new(side, [], [], []);

  public bool Matches(CatalogMod mod) {
    if (Side is not null && !mod.RunsOn(Side)) {
      return false;
    }

    if (GameVersions.Count > 0 && !mod.GameVersions.Any(GameVersions.Contains)) {
      return false;
    }

    if (Licenses.Count > 0 && !Licenses.Contains(CatalogQuery.LicenseFamily(mod))) {
      return false;
    }

    if (IncludeDependencies.Count > 0 || ExcludeDependencies.Count > 0) {
      HashSet<string> dependencies = [.. mod.Dependencies.Keys.Select(CatalogQuery.DependencyKey)];
      if (!IncludeDependencies.All(dependencies.Contains)
          || ExcludeDependencies.Any(dependencies.Contains)) {
        return false;
      }
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

/// The names that an interface shows in its language in place of the English ones of
/// CatalogQuery: of a tag, as CatalogQuery.DisplayTags gives it, and of a license family, by its
/// id. A search matches them as well as the English names.
public sealed record CatalogNames(Func<string, string> Tag, Func<string, string> LicenseFamily);

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

  /// The license families of levimc.org/mods, in its order, with their labels.
  public static IReadOnlyList<(string Id, string Label)> LicenseFamilies { get; } = [
    ("permissive", "Permissive"),
    ("weak-copyleft", "Weak copyleft"),
    ("copyleft", "Copyleft"),
    ("other", "Other"),
    ("none", "No license"),
  ];

  // Licenses GitHub cannot tell, read from the license files of the repositories, by
  // lower-case owner/repo, as levimc.org/mods overrides them.
  private static readonly Dictionary<string, string> _licenseOverrides = new(StringComparer.Ordinal) {
    ["copper-lamp/bschat"] = "AGPL-3.0",
    ["ieckseyr/hologramlib"] = "MIT",
  };

  /// The SPDX id of the license of a mod as levimc.org/mods shows it, or null without one.
  public static string? LicenseOf(CatalogMod mod) {
    string[] parts = mod.Id.ToLowerInvariant().Split('/');
    return parts.Length >= 2
        && _licenseOverrides.TryGetValue($"{parts[^2]}/{parts[^1]}", out string? license)
        ? license
        : mod.License is { Length: > 0 } ? mod.License : null;
  }

  /// The id of the license family of a mod (LicenseFamilies).
  public static string LicenseFamily(CatalogMod mod) {
    if (LicenseOf(mod) is not { } license) {
      return "none";
    }

    string id = license.ToUpperInvariant();
    if (id is "MIT" or "APACHE-2.0" or "ISC" or "ZLIB" or "CC0-1.0" or "UNLICENSE" or "0BSD"
        || id.StartsWith("BSD-", StringComparison.Ordinal)) {
      return "permissive";
    }

    if (id is "MPL-2.0" or "EPL-2.0" || id.StartsWith("LGPL-", StringComparison.Ordinal)) {
      return "weak-copyleft";
    }

    return id.StartsWith("GPL-", StringComparison.Ordinal)
        || id.StartsWith("AGPL-", StringComparison.Ordinal)
        ? "copyleft"
        : "other";
  }

  /// What dependencies are compared by: the id of the package, in lower case, without a variant.
  public static string DependencyKey(string id) => id.Split('#')[0].ToLowerInvariant();

  /// Whether a tag, as DisplayTags gives it, is a featured tag.
  public static bool IsFeatured(string tag) => _featured.ContainsKey(tag);

  /// The label of a license family, by its id; the id for what is none.
  public static string LicenseFamilyLabel(string family) {
    foreach ((string id, string label) in LicenseFamilies) {
      if (id == family) {
        return label;
      }
    }

    return family;
  }

  /// The mods that the filter selects and that match every word of the search, ordered by sort.
  /// names gives the names of the tags and of the license families as the user sees them, when
  /// that is not in English: a word matches those too.
  public static IReadOnlyList<CatalogMod> Select(
      CatalogIndex index,
      string search,
      CatalogFilter filter,
      CatalogSort sort,
      CatalogNames? names = null) {
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
        int quality = Match(mod, word, search.Trim(), names);
        all &= quality > 0;
        score += quality;
      }

      if (all) {
        matches.Add((mod, score, order));
      }
    }

    // The orders of levimc.org/mods; what they leave equal is ordered by the id in lower case.
    IOrderedEnumerable<(CatalogMod Mod, int Score, int Order)> ordered = sort switch {
      CatalogSort.Popularity => matches.OrderByDescending(m => m.Mod.Popularity)
          .ThenByDescending(m => m.Mod.Stars).ThenByDescending(m => m.Mod.Updated),
      CatalogSort.Updated => matches.OrderByDescending(m => m.Mod.Updated)
          .ThenByDescending(m => m.Mod.Popularity),
      CatalogSort.Newest => matches.OrderByDescending(m => m.Mod.Created)
          .ThenByDescending(m => m.Mod.Popularity),
      _ => matches.OrderByDescending(m => m.Score).ThenBy(m => m.Order),
    };
    return [
      .. ordered.ThenBy(m => m.Mod.Id.ToLowerInvariant(), StringComparer.Ordinal)
          .Select(m => m.Mod),
    ];
  }

  /// How well a mod matches a word of the search, which is phrase as a whole: its name before
  /// its id and owner, before its tags, before its description, before its license; 0 when
  /// it does not. A tag matches by its id, its English name and the name that the user sees.
  private static int Match(CatalogMod mod, string word, string phrase, CatalogNames? names) {
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
        tag.Contains(word, ignoreCase) || Label(tag).Contains(word, ignoreCase)
        || names?.Tag(tag).Contains(word, ignoreCase) == true)) {
      return 20;
    }

    if (mod.Description.Contains(word, ignoreCase)) {
      return 10;
    }

    return NamesLicense(mod, word, phrase, names) ? 5 : 0;
  }

  /// Whether a word of a search, or the search as a whole, names the license of a mod. Nearly
  /// every mod is of one of a few license families, so a part of a word names none: the
  /// search is the id, the English name or the name that the user sees of the family, or the
  /// word is one of the words of such a name, of three characters or more, or two when they
  /// are Chinese; or the word is the SPDX id of the license, or its first three characters or
  /// more.
  private static bool NamesLicense(
      CatalogMod mod, string word, string phrase, CatalogNames? names) {
    const StringComparison ignoreCase = StringComparison.OrdinalIgnoreCase;
    string family = LicenseFamily(mod);
    bool whole = word.Length >= 3 || (word.Length == 2 && word.Any(c => c >= 0x2E80));
    foreach (string? name in (string?[])[
        family, LicenseFamilyLabel(family), names?.LicenseFamily(family)]) {
      if (name is not null && (name.Equals(phrase, ignoreCase)
          || (whole && name.Split(' ').Any(part => part.Equals(word, ignoreCase))))) {
        return true;
      }
    }

    return LicenseOf(mod) is { } license
        && (license.Equals(word, ignoreCase)
            || (word.Length >= 3 && license.StartsWith(word, ignoreCase)));
  }
}
