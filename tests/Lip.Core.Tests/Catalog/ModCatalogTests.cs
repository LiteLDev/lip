using Lip.Core.Catalog;
using Lip.Core.Mcmod;
using Semver;

namespace Lip.Core.Tests.Catalog;

/// The catalogue (protocol/lip-tui.md, "Catalogue").
public class ModCatalogTests {
  private const string Index = """
      {
        "url": "https://levimc.org/mods/",
        "stats": {"projects": 4},
        "mods": [
          {
            "id": "github.com/LiteLDev/LegacyMoney", "name": "LegacyMoney", "owner": "LiteLDev",
            "description": "LLMoney, but adapted to LeviLamina",
            "tags": ["type:mod", "platform:levilamina", "library", "economy"],
            "side": ["server", "client"], "platform": ["win-x64"],
            "gameVersions": ["1.26.60", "1.26.51"],
            "deps": {"github.com/LiteLDev/LeviLamina": "26.60.*"},
            "stars": 12, "downloads": 55912,
            "updated": "2026-11-03T13:14:02Z", "created": "2023-12-29T13:10:56Z",
            "license": "MIT",
            "url": "https://levimc.org/mods/github.com/LiteLDev/LegacyMoney/", "requiredBy": 3
          },
          {
            "id": "github.com/IceBlcokMC/PLand", "name": "PLand", "owner": "IceBlcokMC",
            "description": "基于 LeviLamina 的领地系统", "tags": ["land", "pland", "Server"],
            "side": ["server"], "platform": ["win-x64"], "stars": 35, "license": "LGPL-3.0",
            "deps": {
              "github.com/LiteLDev/LeviLamina": "26.60.*",
              "github.com/LiteLDev/LegacyMoney#lib": "0.23.*"
            },
            "updated": "2026-11-04T00:00:00Z", "created": "2024-06-01T00:00:00Z"
          },
          {
            "id": "github.com/LiteLDev/Arke", "name": "Arke", "owner": "LiteLDev",
            "description": "Shader packs", "tags": ["shader"], "side": ["client"],
            "platform": ["win-x64"], "stars": 63, "downloads": 0,
            "updated": "2026-11-08T03:02:57Z", "created": "2026-11-07T05:55:46Z"
          },
          {"id": "github.com/LiteLDev/Arke", "name": "Duplicate"},
          {"name": "No id"}
        ]
      }
      """;

  private static async Task<CatalogIndex> ReadIndex() =>
      await new ModCatalogClient((_, _) => Task.FromResult(Index)).GetIndex(
          TestContext.Current.CancellationToken);

  [Fact]
  public async Task GetIndex_ReadsTheModsOfIndexJson_OnceEach() {
    List<string> urls = [];
    ModCatalogClient client = new((url, _) => {
      urls.Add(url);
      return Task.FromResult(Index);
    });

    CatalogIndex index = await client.GetIndex(TestContext.Current.CancellationToken);

    Assert.Equal(["https://levimc.org/api/v4/mods/index.json"], urls);
    Assert.Equal(["LegacyMoney", "PLand", "Arke"], index.Mods.Select(mod => mod.Name));
    CatalogMod money = index.Mods[0];
    Assert.Equal("github.com/LiteLDev/LegacyMoney", money.Id);
    Assert.Equal(["server", "client"], money.Sides);
    Assert.Equal("26.60.*", money.Dependencies["github.com/LiteLDev/LeviLamina"]);
    Assert.Equal(12, money.Stars);
    // The downloads are a count, or unknown when the file leaves them out: not none.
    Assert.Equal(55912, money.Downloads);
    Assert.Null(index.Mods[1].Downloads);
    Assert.Equal(0, index.Mods[2].Downloads);
    Assert.Equal(3, money.RequiredBy);
    Assert.Equal(new DateTimeOffset(2026, 11, 3, 13, 14, 2, TimeSpan.Zero), money.Updated);
    Assert.True(money.RunsOn("server"));
    Assert.False(index.Mods[2].RunsOn("server"));
  }

  [Fact]
  public async Task GetDetail_ReadsTheFileOfTheMod() {
    List<string> urls = [];
    ModCatalogClient client = new((url, _) => {
      urls.Add(url);
      return Task.FromResult("""
          {
            "id": "github.com/LiteLDev/LegacyMoney", "license": "MIT",
            "repository": "https://github.com/LiteLDev/LegacyMoney",
            "latest": {"variant": "", "version": "0.23.1"},
            "readmes": [
              {"file": "README.zh.md", "lang": "zh", "text": "中文"},
              {"file": "README.md", "lang": "en", "text": "English"}
            ],
            "versions": [
              {
                "version": "0.23.1", "prerelease": false, "released": "2026-11-03T13:14:02Z",
                "variants": {
                  "lua": [{
                    "side": "server", "platform": "win-x64",
                    "deps": {"github.com/LiteLDev/LeviLamina": "26.60.*"}
                  }],
                  "client": [{"side": "client", "platform": "win-x64"}],
                  "": [{"side": "client"}, {"side": "server"}]
                }
              },
              {"version": "0.23.0-rc.1", "prerelease": true}
            ]
          }
          """);
    });

    CatalogDetail detail = await client.GetDetail(
        "github.com/LiteLDev/LegacyMoney", TestContext.Current.CancellationToken);

    Assert.Equal(
        ["https://levimc.org/api/v4/mods/github.com/LiteLDev/LegacyMoney/index.json"], urls);
    Assert.Equal("MIT", detail.License);
    Assert.Equal("0.23.1", detail.LatestVersion);
    Assert.Equal("English", detail.Readme);
    Assert.Equal(["0.23.1", "0.23.0-rc.1"], detail.Versions.Select(v => v.Version));
    Assert.True(detail.Versions[1].Prerelease);
    // The variants with a build for a server, the default one first; the default one alone
    // for a version that does not say.
    Assert.Equal(["", "lua"], detail.Versions[0].Variants);
    Assert.Equal([""], detail.Versions[1].Variants);
    Assert.Equal(["", "lua"], detail.ServerVariants());
    // Every entry of every variant is kept, with what it is for and what it depends on.
    Assert.Equal(
        ["lua:server", "client:client", ":client", ":server"],
        detail.Versions[0].Builds.Select(build => $"{build.Variant}:{build.Side}"));
    CatalogBuild lua = Assert.Single(detail.Versions[0].ServerBuilds("lua"));
    Assert.Equal("win-x64", lua.Platform);
    Assert.Equal("26.60.*", lua.Dependencies["github.com/LiteLDev/LeviLamina"]);
    Assert.Empty(detail.Versions[0].ServerBuilds("").Single().Dependencies);
    Assert.Empty(detail.Versions[1].Builds);
  }

  [Theory]
  [InlineData("not json")]
  [InlineData("[]")]
  public async Task GetDetail_NotTheFileOfAMod_IsUnavailable(string content) {
    ModCatalogClient client = new((_, _) => Task.FromResult(content));

    await Assert.ThrowsAsync<CatalogUnavailableException>(() =>
        client.GetDetail("github.com/a/b", TestContext.Current.CancellationToken));
  }

  [Fact]
  public async Task GetIndex_RequestFails_IsUnavailable() {
    ModCatalogClient client = new((_, _) => throw new HttpRequestException("no network"));

    CatalogUnavailableException exception =
        await Assert.ThrowsAsync<CatalogUnavailableException>(() =>
            client.GetIndex(TestContext.Current.CancellationToken));

    Assert.Contains("no network", exception.Message);
  }

  [Fact]
  public async Task DisplayTags_HidesCommonTagsAndGroupsFeaturedOnes() {
    CatalogIndex index = await ReadIndex();

    Assert.Equal(["library", "economy"], CatalogQuery.DisplayTags(index.Mods[0]));
    // "land" counts as the featured tag "management"; "server" says what the side does.
    Assert.Equal(["management", "pland"], CatalogQuery.DisplayTags(index.Mods[1]));
    Assert.Equal("Server management", CatalogQuery.Label("management"));
    Assert.Equal("pland", CatalogQuery.Label("pland"));
  }

  [Theory]
  // Every included tag, none of the excluded ones, one of the game versions.
  [InlineData("library,economy", "", "", "LegacyMoney")]
  [InlineData("library,management", "", "", "")]
  [InlineData("", "economy", "", "PLand,Arke")]
  [InlineData("", "economy,visual", "", "PLand")]
  [InlineData("library", "economy", "", "")]
  [InlineData("", "", "1.26.51", "LegacyMoney")]
  [InlineData("", "", "1.21.0,1.26.60", "LegacyMoney")]
  [InlineData("", "", "1.21.0", "")]
  [InlineData("", "pland", "1.26.60", "LegacyMoney")]
  public async Task Select_TagsAndGameVersions_Filter(
      string include, string exclude, string versions, string expected) {
    CatalogIndex index = await ReadIndex();
    static string[] Split(string list) => list.Split(',', StringSplitOptions.RemoveEmptyEntries);

    IReadOnlyList<CatalogMod> mods = CatalogQuery.Select(
        index, "", new CatalogFilter(null, Split(include), Split(exclude), Split(versions)),
        CatalogSort.Relevance);

    Assert.Equal(expected, string.Join(',', mods.Select(mod => mod.Name)));
  }

  [Theory]
  // Under one of the license families.
  [InlineData("permissive", "", "", "LegacyMoney")]
  [InlineData("permissive,weak-copyleft", "", "", "LegacyMoney,PLand")]
  [InlineData("none", "", "", "Arke")]
  [InlineData("copyleft", "", "", "")]
  // Depending on every included package, in any case and variant, and on no excluded one.
  [InlineData("", "github.com/liteldev/levilamina", "", "LegacyMoney,PLand")]
  [InlineData("", "github.com/liteldev/levilamina,github.com/liteldev/legacymoney", "", "PLand")]
  [InlineData("", "", "github.com/liteldev/legacymoney", "LegacyMoney,Arke")]
  [InlineData("", "github.com/liteldev/levilamina", "github.com/liteldev/legacymoney", "LegacyMoney")]
  [InlineData("weak-copyleft", "github.com/liteldev/levilamina", "", "PLand")]
  public async Task Select_LicensesAndDependencies_Filter(
      string licenses, string include, string exclude, string expected) {
    CatalogIndex index = await ReadIndex();
    static string[] Split(string list) => list.Split(',', StringSplitOptions.RemoveEmptyEntries);

    IReadOnlyList<CatalogMod> mods = CatalogQuery.Select(
        index, "",
        new CatalogFilter(null, [], [], []) {
          Licenses = Split(licenses),
          IncludeDependencies = Split(include),
          ExcludeDependencies = Split(exclude),
        },
        CatalogSort.Relevance);

    Assert.Equal(expected, string.Join(',', mods.Select(mod => mod.Name)));
  }

  [Theory]
  [InlineData("github.com/a/b", "MIT", "MIT", "permissive")]
  [InlineData("github.com/a/b", "BSD-3-Clause", "BSD-3-Clause", "permissive")]
  [InlineData("github.com/a/b", "LGPL-3.0", "LGPL-3.0", "weak-copyleft")]
  [InlineData("github.com/a/b", "gpl-3.0", "gpl-3.0", "copyleft")]
  [InlineData("github.com/a/b", "AGPL-3.0", "AGPL-3.0", "copyleft")]
  [InlineData("github.com/a/b", "NOASSERTION", "NOASSERTION", "other")]
  [InlineData("github.com/a/b", null, null, "none")]
  [InlineData("github.com/a/b", "", null, "none")]
  // A license that GitHub cannot tell and levimc.org/mods overrides.
  [InlineData("github.com/Ieckseyr/HologramLib", "NOASSERTION", "MIT", "permissive")]
  public void LicenseFamily_SortsTheLicenseOfAModIntoAFamily(
      string id, string? license, string? shown, string family) {
    CatalogMod mod = new(id, "x", "", "", [], [], [], [], new Dictionary<string, string>(), 0,
        null, null, 0, "", license);

    Assert.Equal(shown, CatalogQuery.LicenseOf(mod));
    Assert.Equal(family, CatalogQuery.LicenseFamily(mod));
  }

  [Fact]
  public void IsFeatured_TellsTheFeaturedTagsFromTheOthers() {
    Assert.True(CatalogQuery.IsFeatured("management"));
    Assert.False(CatalogQuery.IsFeatured("pland"));
  }

  [Theory]
  [InlineData("", "server", null, CatalogSort.Relevance, "LegacyMoney,PLand")]
  // Arke has the most stars and no download, PLand no count of downloads.
  [InlineData("", null, null, CatalogSort.Popularity, "PLand,Arke,LegacyMoney")]
  [InlineData("", null, null, CatalogSort.Updated, "Arke,PLand,LegacyMoney")]
  [InlineData("", null, null, CatalogSort.Newest, "Arke,PLand,LegacyMoney")]
  [InlineData("", "client", null, CatalogSort.Relevance, "LegacyMoney,Arke")]
  [InlineData("", null, "management", CatalogSort.Relevance, "PLand")]
  [InlineData("领地", null, null, CatalogSort.Relevance, "PLand")]
  [InlineData("liteldev", null, null, CatalogSort.Relevance, "LegacyMoney,Arke")]
  [InlineData("liteldev shader", null, null, CatalogSort.Relevance, "Arke")]
  [InlineData("economy", null, null, CatalogSort.Relevance, "LegacyMoney")]
  // The name before the description.
  [InlineData("l", null, null, CatalogSort.Relevance, "LegacyMoney,PLand,Arke")]
  [InlineData("nothing-matches", null, null, CatalogSort.Relevance, "")]
  public async Task Select_FiltersSearchesAndOrders(
      string search, string? side, string? tag, CatalogSort sort, string expected) {
    CatalogIndex index = await ReadIndex();

    IReadOnlyList<CatalogMod> mods = CatalogQuery.Select(
        index, search, new CatalogFilter(side, tag is null ? [] : [tag], [], []), sort);

    Assert.Equal(expected, string.Join(',', mods.Select(mod => mod.Name)));
  }

  /// A mod with what the orders and the popularity look at.
  private static CatalogMod Mod(
      string id, int stars, long? downloads = null, double popularity = 0, int updated = 0,
      int created = 0) =>
      new(id, id, "", "", [], [], [], [], new Dictionary<string, string>(), stars,
          updated == 0 ? null : new DateTimeOffset(2026, 1, updated, 0, 0, 0, TimeSpan.Zero),
          created == 0 ? null : new DateTimeOffset(2025, 1, created, 0, 0, 0, TimeSpan.Zero),
          0, "") {
        Downloads = downloads,
        Popularity = popularity,
      };

  [Fact]
  public void Popularity_IsTheMeanOfTheZScoresOfTheStarsAndTheDownloads() {
    // ln(1 + x) of the stars is one of two values, three mods each: the mean is halfway and
    // the population deviation is half their distance, so z is +1 or -1. The same holds for
    // the downloads of the four mods that have a count.
    IReadOnlyList<double> scores = CatalogPopularity.Scores([
      Mod("a", 99, 1000),  // (+1 + +1) / 2
      Mod("b", 99, 10),    // (+1 + -1) / 2
      Mod("c", 99),        // no count: z of the stars twice
      Mod("d", 0, 1000),   // (-1 + +1) / 2
      Mod("e", 0, 10),     // (-1 + -1) / 2
      Mod("f", 0),         // no count
    ]);

    Assert.Equal([1, 0, 1, 0, -1, -1], scores);
  }

  [Fact]
  public void Popularity_IsRoundedToThreeDecimals() {
    // ln(1 + x) of 0, 1 and 3 stars is 0, ln 2 and 2 ln 2: the mean is ln 2 and the
    // population deviation ln 2 * sqrt(2 / 3), so z is -sqrt(1.5), 0 and sqrt(1.5) =
    // 1.2247...; no mod has a count of downloads.
    IReadOnlyList<double> scores =
        CatalogPopularity.Scores([Mod("a", 0), Mod("b", 1), Mod("c", 3)]);

    Assert.Equal([-1.225, 0, 1.225], scores);
  }

  [Fact]
  public void Popularity_OfModsThatDoNotDiffer_IsZero() {
    // The deviation of the stars is 0, and so is that of the downloads of the one mod that
    // has a count.
    Assert.Equal([0, 0], CatalogPopularity.Scores([Mod("a", 5, 700), Mod("b", 5)]));
    Assert.Equal([0], CatalogPopularity.Scores([Mod("a", 5)]));
    Assert.Empty(CatalogPopularity.Scores([]));
  }

  [Fact]
  public async Task GetIndex_GivesEveryModItsPopularity() {
    CatalogIndex index = await ReadIndex();

    // Three mods, 12, 35 and 63 stars; the downloads of the two that have a count are 55912
    // and 0, so their z is +1 and -1, and the mod without a count takes z of its stars twice.
    double[] logs = [Math.Log(13), Math.Log(36), Math.Log(64)];
    double mean = logs.Average();
    double deviation = Math.Sqrt(logs.Sum(log => Math.Pow(log - mean, 2)) / 3);
    double[] expected = [
      Math.Round((((logs[0] - mean) / deviation) + 1) / 2, 3),
      Math.Round((logs[1] - mean) / deviation, 3),
      Math.Round((((logs[2] - mean) / deviation) - 1) / 2, 3),
    ];
    Assert.Equal(expected, index.Mods.Select(mod => mod.Popularity));
  }

  [Theory]
  // The most popular, then the most stars, then the last updated, then the id.
  [InlineData(CatalogSort.Popularity, "b,D,c,a,e")]
  // The last updated, then the most popular, then the id, whatever its case.
  [InlineData(CatalogSort.Updated, "e,b,D,a,c")]
  // The last created, then the most popular, then the id.
  [InlineData(CatalogSort.Newest, "c,b,a,D,e")]
  // The order of the catalogue.
  [InlineData(CatalogSort.Relevance, "e,D,c,b,a")]
  public void Select_OrdersAsLevimcOrgDoes(CatalogSort sort, string expected) {
    CatalogIndex index = new([
      Mod("e", stars: 1, popularity: -1, updated: 9, created: 1),
      Mod("D", stars: 5, popularity: 1, updated: 3, created: 2),
      Mod("c", stars: 5, popularity: 1, updated: 2, created: 9),
      Mod("b", stars: 9, popularity: 1, updated: 3, created: 5),
      Mod("a", stars: 5, popularity: 0.5, updated: 3, created: 5),
    ]);

    IReadOnlyList<CatalogMod> mods = CatalogQuery.Select(
        index, "", new CatalogFilter(null, [], [], []), sort);

    Assert.Equal(expected, string.Join(',', mods.Select(mod => mod.Id)));
  }

  [Fact]
  public void CatalogSort_NamesTheOrdersOfLevimcOrg_InItsOrder() =>
      Assert.Equal(
          [CatalogSort.Relevance, CatalogSort.Popularity, CatalogSort.Updated, CatalogSort.Newest],
          Enum.GetValues<CatalogSort>());

  [Theory]
  [InlineData("github.com/LiteLDev/LegacyMoney", "LegacyMoney")]
  [InlineData("GitHub.com/liteldev/legacymoney", "LegacyMoney")]
  [InlineData("github.com/LiteLDev/LegacyMoney#lua", "LegacyMoney")]
  [InlineData("github.com/LiteLDev/LegacyMoney2", null)]
  [InlineData("github.com/LiteLDev", null)]
  public async Task Find_GivesTheModOfAnInstalledPackage(string identity, string? expected) {
    CatalogIndex index = await ReadIndex();

    Assert.Equal(expected, index.Find(identity)?.Name);
  }

  [Fact]
  public void CatalogWorkspace_ReadsTheGameAndTheLoaderOfTheInstalledPackages() {
    CatalogWorkspace workspace = CatalogWorkspace.Of([
      new InstalledPackage("github.com/acme/Alpha", "1.2.0", true),
      new InstalledPackage("github.com/LiteLDev/bds#linux", "1.26.60", true),
      new InstalledPackage("github.com/liteldev/levilamina", "26.60.7", true),
    ]);

    Assert.Equal(new CatalogWorkspace("1.26.60", "26.60.7"), workspace);
    Assert.Equal(new CatalogWorkspace(null, null), CatalogWorkspace.Of([]));
  }

  private const string Loader = "github.com/LiteLDev/LeviLamina";
  private const string Game = "github.com/LiteLDev/bds";

  /// A version with one entry per variant given as "variant|side|id range,id range".
  private static CatalogVersion Version(string name, params string[] builds) =>
      new(name, name.Contains('-'), null, [
        .. builds.Select(build => {
          string[] parts = build.Split('|', 3);
          return new CatalogBuild(
              parts[0], parts[1], "win-x64",
              parts[2].Split(',', StringSplitOptions.RemoveEmptyEntries)
                  .Select(dependency => dependency.Split(' ', 2))
                  .ToDictionary(pair => pair[0], pair => pair[1]));
        }),
      ]);

  [Theory]
  // The range of LeviLamina contains the installed one, or not.
  [InlineData("", "1.26.60", "26.60.7", true)]
  [InlineData("", "1.26.60", "26.50.2", false)]
  // A mod that needs LeviLamina does not fit a workspace without it.
  [InlineData("", "1.26.60", null, false)]
  // A variant that depends on neither fits any workspace.
  [InlineData("plain", "1.26.60", null, true)]
  [InlineData("plain", null, null, true)]
  // A dependency on the game is compared with the game.
  [InlineData("game", "1.26.60", null, true)]
  [InlineData("game", "1.21.0", null, false)]
  [InlineData("game", null, null, false)]
  // One entry that fits is enough.
  [InlineData("two", "1.26.60", "26.50.2", true)]
  // A range that cannot be read rules nothing out.
  [InlineData("odd", "1.26.60", "26.50.2", true)]
  // An entry for another platform than that of lip does not count; one without does.
  [InlineData("linux", "1.26.60", "26.60.7", false)]
  [InlineData("any", "1.26.60", "26.60.7", true)]
  // Only variants with an entry for a server count.
  [InlineData("client", "1.26.60", "26.60.7", false)]
  [InlineData("none", "1.26.60", "26.60.7", false)]
  public void IsCompatible_ComparesTheRangesOfAVariantWithTheWorkspace(
      string variant, string? game, string? loader, bool expected) {
    CatalogVersion version = Version(
        "1.0.0",
        $"|server|{Loader} 26.60.*,github.com/acme/Other 9.*",
        "plain|server|github.com/acme/Other 9.*",
        $"game|server|{Game}#linux 1.26.60",
        $"two|server|{Loader} 26.60.*",
        $"two|server|{Loader} 26.50.*",
        $"odd|server|{Loader} not-a-range",
        $"client|client|{Loader} 26.60.*");
    version = version with {
      Builds = [
        .. version.Builds,
        new CatalogBuild("linux", "server", "linux-x64", new Dictionary<string, string>()),
        new CatalogBuild("any", "server", "", new Dictionary<string, string>()),
      ],
    };

    Assert.Equal(
        expected,
        CatalogCompatibility.IsCompatible(version, variant, new CatalogWorkspace(game, loader)));
  }

  [Fact]
  public void Versions_AreThoseOfTheVariantThatFit_InTheOrderOfTheFile() {
    CatalogDetail detail = new("github.com/acme/Alpha", null, null, "1.3.0", "", [
      Version("1.3.0", $"|server|{Loader} 26.70.*", $"lua|server|{Loader} 26.60.*"),
      Version("1.2.0", $"|server|{Loader} 26.60.*"),
      Version("1.2.0-rc.1", $"|server|{Loader} 26.60.*"),
      Version("1.1.0", $"|server|{Loader} 26.50.*"),
      // A version whose variants the file does not list is of the default variant, and fits.
      Version("1.0.0"),
    ], null);
    CatalogWorkspace workspace = new("1.26.60", "26.60.7");

    Assert.Equal(
        ["1.2.0", "1.2.0-rc.1", "1.0.0"],
        CatalogCompatibility.Versions(detail, "", workspace).Select(version => version.Version));
    Assert.Equal(
        ["1.3.0"],
        CatalogCompatibility.Versions(detail, "lua", workspace).Select(version => version.Version));
    Assert.Empty(
        CatalogCompatibility.Versions(detail, "lua", new CatalogWorkspace("1.26.60", null)));
    Assert.True(CatalogCompatibility.NeedsLoader(detail, "lua"));
    Assert.False(CatalogCompatibility.NeedsLoader(detail, "other"));
  }

  [Fact]
  public void LoaderSupport_TellsWhichGamesHaveALeviLamina() {
    LoaderSupport support = new(new CatalogDetail(Loader, null, null, "26.70.0-rc.1", "", [
      // A prerelease counts only for a game that no other version is for.
      Version("26.70.0-rc.2", $"|server|{Game} 1.26.70"),
      Version("26.70.0-rc.1", $"|server|{Game} 1.26.70"),
      Version("26.60.8-rc.1", $"|server|{Game} 1.26.60"),
      Version("26.60.7", $"|server|{Game} 1.26.60", "client|client|"),
      Version("26.60.6", $"|server|{Game} 1.26.60"),
      Version("26.50.2", $"|server|{Game} 1.26.50 || 1.26.51"),
      // Not the game of lip, an entry for a client, and no entry at all: none counts.
      Version("0.4.2", "|server|github.com/tooth-hub/bds 1.20.50"),
      Version("0.4.1", $"client|client|{Game} 1.20.40"),
      Version("0.4.0"),
    ], null));

    Assert.Equal("26.60.7", support.VersionFor("1.26.60"));
    Assert.Equal("26.50.2", support.VersionFor("1.26.51"));
    Assert.Equal("26.70.0-rc.2", support.VersionFor(SemVersion.Parse("1.26.70")));
    Assert.Null(support.VersionFor("1.20.50"));
    Assert.Null(support.VersionFor("1.20.40"));
    Assert.Null(support.VersionFor("not a version"));
    Assert.True(support.Supports("1.26.50"));
    Assert.False(support.Supports(SemVersion.Parse("1.21.0")));
  }

  [Fact]
  public async Task GetLoaderSupport_ReadsTheFileOfLeviLamina_Once() {
    List<string> urls = [];
    ModCatalogClient client = new((url, _) => {
      urls.Add(url);
      return Task.FromResult("""
          {
            "id": "github.com/LiteLDev/LeviLamina",
            "versions": [ {
              "version": "26.60.7",
              "variants": { "": [ {
                "side": "server", "platform": "win-x64",
                "deps": {"github.com/LiteLDev/bds": "1.26.60"}
              } ] }
            } ]
          }
          """);
    });

    LoaderSupport support = await client.GetLoaderSupport(TestContext.Current.CancellationToken);

    Assert.Equal(
        ["https://levimc.org/api/v4/mods/github.com/LiteLDev/LeviLamina/index.json"], urls);
    Assert.True(support.Supports("1.26.60"));
    Assert.False(support.Supports("1.26.61"));
  }

  /// The names of an interface in Chinese, for the tags and the license families that the
  /// tests search for.
  private static readonly CatalogNames _chinese = new(
      tag => tag switch {
        "economy" => "经济",
        "visual" => "视觉与 HUD",
        _ => CatalogQuery.Label(tag),
      },
      family => family switch {
        "permissive" => "宽松许可",
        "weak-copyleft" => "弱 Copyleft",
        "none" => "无许可证",
        _ => family,
      });

  [Theory]
  // The name of a featured tag as the interface shows it, and its English name all the same.
  [InlineData("经济", "LegacyMoney")]
  [InlineData("视觉", "Arke")]
  [InlineData("Economy", "LegacyMoney")]
  // A license family, by the name shown, by its English name and by its id, and a license
  // by its SPDX id or the start of it.
  [InlineData("宽松许可", "LegacyMoney")]
  [InlineData("弱 Copyleft", "PLand")]
  [InlineData("无许可证", "Arke")]
  [InlineData("Permissive", "LegacyMoney")]
  [InlineData("weak copyleft", "PLand")]
  [InlineData("weak-copyleft", "PLand")]
  [InlineData("No license", "Arke")]
  [InlineData("lgpl", "PLand")]
  [InlineData("MIT", "LegacyMoney")]
  // A part of a name of a license family names none: nearly every mod is of one of them.
  [InlineData("permis", "")]
  [InlineData("per", "")]
  [InlineData("许可", "")]
  [InlineData("弱", "")]
  // Every word must match, whatever it matches.
  [InlineData("经济 宽松许可", "LegacyMoney")]
  [InlineData("经济 弱", "")]
  public async Task Select_MatchesTheNamesThatTheUserSees_AsWellAsTheEnglishOnes(
      string search, string expected) {
    CatalogIndex index = await ReadIndex();

    IReadOnlyList<CatalogMod> mods = CatalogQuery.Select(
        index, search, CatalogFilter.For(null), CatalogSort.Relevance, _chinese);

    Assert.Equal(expected, string.Join(",", mods.Select(mod => mod.Name)));
  }

  [Fact]
  public async Task Select_WithoutNames_MatchesTheEnglishNamesOnly() {
    CatalogIndex index = await ReadIndex();

    Assert.Empty(CatalogQuery.Select(
        index, "经济", CatalogFilter.For(null), CatalogSort.Relevance));
    Assert.Equal(
        ["LegacyMoney"],
        CatalogQuery.Select(index, "permissive", CatalogFilter.For(null), CatalogSort.Relevance)
            .Select(mod => mod.Name));
  }
}
