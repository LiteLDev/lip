using System.Text.Json;
using Lip.Core.Mcmod;
using Semver;

namespace Lip.Core.Tests.Mcmod;

public class McmodPackageTests {
  private static PackageDocument CreateDocument() => new() {
    Id = "LegacyMoney",
    Version = SemVersion.Parse("0.22.0"),
    IsMod = true,
    Dependencies = [
      new Dep { Id = "iListenAttentively", Refs = [Ref.Parse("github.com/MiracleForest/iListenAttentively")] },
      new Dep { Id = "NoSource" },
    ],
    InstallDependencies = [
      new InstallDep { Refs = [Ref.Parse("github.com/LiteLDev/LeviLamina")] },
    ],
  };

  [Fact]
  public void InstallEdges_IncludeDependenciesWithSourceAndToothDependencies() {
    // Arrange
    McmodPackage package = new(CreateDocument(), JsonDocument.Parse("{}").RootElement);

    // Act
    IReadOnlyList<InstallEdge> edges = package.InstallEdges;

    // Assert
    Assert.Equal(2, edges.Count);
    Assert.Equal("iListenAttentively", edges[0].Name);
    Assert.Null(edges[1].Name);
    Assert.Equal("github.com/LiteLDev/LeviLamina", edges[1].Refs[0].Target);
  }

  [Fact]
  public void MetaTarget_Matches_ChecksWrittenSelectorsOnly() {
    // Arrange
    MetaTarget target = new() { Runtimes = ["win-x64"], Source = ["./a/"] };

    // Act & Assert
    Assert.True(target.Matches(new(McmodPlatform.Server, "win-x64")));
    Assert.False(target.Matches(new(McmodPlatform.Server, "linux-x64")));
    Assert.Equal(1, target.SelectorCount);
  }
}
