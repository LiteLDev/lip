using System.Text.Json;
using Lip.Core.Mcmod;
using Semver;

namespace Lip.Core.Tests.Mcmod;

public class McmodPackageTests {
  private static ConcreteDocument CreateDocument(bool isMarked) => new() {
    IsMarked = isMarked,
    Name = "LegacyMoney",
    Version = SemVersion.Parse("0.22.0"),
    Type = "native",
    Dependencies = [
      new Dep { Name = "iListenAttentively", Refs = [Ref.Parse("github.com/MiracleForest/iListenAttentively")] },
      new Dep { Name = "NoRefs" },
    ],
    InstallDependencies = [
      new InstallDep { Refs = [Ref.Parse("github.com/LiteLDev/LeviLamina")] },
    ],
  };

  [Fact]
  public void InstallEdges_Marked_IncludesDependenciesWithRefsAndInstallDependencies() {
    // Arrange
    McmodPackage package = new(CreateDocument(isMarked: true), JsonDocument.Parse("{}").RootElement);

    // Act
    IReadOnlyList<InstallEdge> edges = package.InstallEdges;

    // Assert
    Assert.Equal(2, edges.Count);
    Assert.Equal("iListenAttentively", edges[0].Name);
    Assert.Null(edges[1].Name);
    Assert.Equal("github.com/LiteLDev/LeviLamina", edges[1].Refs[0].Target);
  }

  [Fact]
  public void InstallEdges_Unmarked_IsEmpty() {
    // Arrange
    McmodPackage package = new(CreateDocument(isMarked: false), JsonDocument.Parse("{}").RootElement);

    // Act & Assert
    Assert.Empty(package.InstallEdges);
  }

  [Fact]
  public void Artifact_Matches_ChecksWrittenSelectorsOnly() {
    // Arrange
    Artifact artifact = new() { Rids = ["win-x64"], Refs = ["./a/"] };

    // Act & Assert
    Assert.True(artifact.Matches(new(null, "win-x64")));
    Assert.False(artifact.Matches(new(null, "linux-x64")));
    Assert.Equal(1, artifact.SelectorCount);
  }
}
