using Lip.Core.Catalog;

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
            "stars": 12, "updated": "2026-11-03T13:14:02Z", "created": "2023-12-29T13:10:56Z",
            "url": "https://levimc.org/mods/github.com/LiteLDev/LegacyMoney/", "requiredBy": 3
          },
          {
            "id": "github.com/IceBlcokMC/PLand", "name": "PLand", "owner": "IceBlcokMC",
            "description": "基于 LeviLamina 的领地系统", "tags": ["land", "pland", "Server"],
            "side": ["server"], "platform": ["win-x64"], "stars": 35,
            "updated": "2026-11-04T00:00:00Z", "created": "2024-06-01T00:00:00Z"
          },
          {
            "id": "github.com/LiteLDev/Arke", "name": "Arke", "owner": "LiteLDev",
            "description": "Shader packs", "tags": ["shader"], "side": ["client"],
            "platform": ["win-x64"], "stars": 63,
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
              {"version": "0.23.1", "prerelease": false, "released": "2026-11-03T13:14:02Z"},
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
  [InlineData("", "server", null, CatalogSort.Relevance, "LegacyMoney,PLand")]
  [InlineData("", null, null, CatalogSort.Stars, "Arke,PLand,LegacyMoney")]
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

    IReadOnlyList<CatalogMod> mods = CatalogQuery.Select(index, search, side, tag, sort);

    Assert.Equal(expected, string.Join(',', mods.Select(mod => mod.Name)));
  }
}
