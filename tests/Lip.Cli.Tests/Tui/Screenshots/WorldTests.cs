using System.Text.Json;
using Lip.Cli.Tui.I18n;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod;
using Lip.Core.PublicApi;
using Moq;

namespace Lip.Cli.Tests.Tui.Screenshots;

/// What the interface of a screenshot finds (World) is what lip itself would have written and
/// would say, so that the pictures show the interface as it is.
public sealed class WorldTests {
  /// The state file records the source of a package with its variant
  /// (protocol/state.json.md), so lip update of a package of a variant asks for that variant.
  [Fact]
  public async Task Install_RecordsTheSourceWithTheVariant() {
    // Arrange
    World world = new World(Language.En).Install(
        new Installed("github.com/acme/Engine", "0.13.1", Variant: "lua"),
        new Installed("github.com/acme/Plain", "1.0.0"));

    // Act
    using JsonDocument state = JsonDocument.Parse(
        world.Fs.File.ReadAllText(Path.Join(world.Workspace, ".lip", "state.json")));
    LipClient client = world.Client(
        Mock.Of<IUserInteraction>(), () => Stream.Null, world.Workspace);
    IReadOnlyList<InstalledPackage> packages = await client.ListPackages();

    // Assert
    Assert.Equal(
        ["github.com/acme/Engine#lua@0.13.1", "github.com/acme/Plain@1.0.0"],
        state.RootElement.GetProperty("packages").EnumerateArray()
            .Select(package => package.GetProperty("ref").GetString()));
    Assert.Equal(
        [
          ("github.com/acme/Engine#lua", "github.com/acme/Engine#lua"),
          ("github.com/acme/Plain", "github.com/acme/Plain"),
        ],
        packages.Select(package => (package.Identity, package.UpdateArgument)));
  }

  /// A failure is described as lip describes it: its message, then those of its causes, with
  /// the hints of lip; the report is named and not written.
  [Fact]
  public void DescribeFailure_IsWhatLipSays_WithTheCauses_AndWritesNoReport() {
    // Arrange
    World world = new(Language.En);
    InvalidOperationException failure = new(
        "Cannot migrate tooth.json.", new FormatException("The version is missing."));

    // Act
    Failure described = world.DescribeFailure(failure, ["(interface)"], false, world.Workspace);
    Failure cancelled = world.DescribeFailure(
        new OperationCanceledException(), ["(interface)"], true, world.Workspace);

    // Assert
    Assert.Equal(
        "Cannot migrate tooth.json.\n  caused by: The version is missing.", described.Summary);
    Assert.Equal(ErrorReporter.Hints(failure), described.Hints);
    Assert.Equal(Path.Join(".lip", "logs", "20261114T201502Z-4120.log"), described.ReportPath);
    Assert.False(world.Fs.Directory.Exists(Path.Join(world.Workspace, ".lip", "logs")));
    Assert.False(Directory.Exists(Path.Join(world.Workspace, ".lip")));
    Assert.True(cancelled.Cancelled);
  }
}
