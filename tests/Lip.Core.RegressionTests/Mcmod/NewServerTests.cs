using Lip.Core.Infrastructure;
using Lip.Core.Mcmod;
using Lip.Core.PublicApi;
using Semver;

namespace Lip.Core.RegressionTests.Mcmod;

/// What the interface reads from the network for a new server and for the reachability of GitHub
/// (protocol/lip-tui.md, "New server", "GitHub reachability"), against the real services.
public class NewServerTests(ITestOutputHelper output) {
  [Fact]
  public async Task BuildsList_NamesReleaseVersionsWithWindowsBuilds() {
    // Act
    BedrockServer server = BedrockServer.Parse(await BedrockServer.Download());
    List<SemVersion> releases = [.. server.ReleaseVersions];

    // Assert
    Assert.NotEmpty(releases);
    Assert.True(server.HasBuild(GameBuild.Windows, releases[0]));
    Assert.Contains(releases, v => server.HasBuild(GameBuild.Linux, v));
    output.WriteLine($"{releases.Count} release versions, newest {releases[0]}");
  }

  [Fact]
  public async Task LoaderVersions_OfARecentGame_NameLeviLamina() {
    // Arrange: a workspace that does not exist, as for a new server.
    string directory = Path.Join(Path.GetTempPath(), "lip-new-server-" + Guid.NewGuid());
    LipClient client = LipClient.Create(
        new System.IO.Abstractions.FileSystem(), Moq.Mock.Of<IUserInteraction>(), null,
        directory);
    IReadOnlyList<GameVersion> games = await client.GameVersions();
    long started = Environment.TickCount64;

    // Act: the newest of the release versions that LeviLamina supports.
    IReadOnlyList<SemVersion> loaders = [];
    foreach (GameVersion game in games.Where(g => !g.Version.IsPrerelease).Take(10)) {
      loaders = await client.LoaderVersions(game.Version, TestContext.Current.CancellationToken);
      if (loaders.Count > 0) {
        output.WriteLine($"Game {game.Version}: LeviLamina {string.Join(", ", loaders)}, after"
            + $" {Environment.TickCount64 - started} ms");
        break;
      }
    }

    // Assert
    Assert.NotEmpty(loaders);
    Assert.False(Directory.Exists(directory));
  }

  [Fact]
  public async Task GithubProbe_ReachesGithubDirectly() {
    long started = Environment.TickCount64;

    string? failure = await new GithubProbe().Check(null, TestContext.Current.CancellationToken);

    output.WriteLine(
        $"{failure ?? "reached"} after {Environment.TickCount64 - started} ms");
    Assert.Null(failure);
  }
}
