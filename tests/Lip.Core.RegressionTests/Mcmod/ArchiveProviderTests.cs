using System.IO.Abstractions;
using Lip.Core.Git;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod;
using Lip.Core.Services;
using Moq;

namespace Lip.Core.RegressionTests.Mcmod;

/// Runs the archive provider against GitHub: version tags, git archives and a release ZIP.
public class ArchiveProviderTests {
  private const string _releaseZipUrl =
      "https://github.com/LiteLDev/LegacyMoney/releases/download/v0.22.0/"
      + "LegacyMoney-server-windows-x64.zip";

  private static ArchiveProvider CreateProvider() {
    FileSystem fileSystem = new();
    Mock<IUserInteraction> userInteractionMock = new();
    userInteractionMock
        .Setup(u => u.RunWithProgress(
            It.IsAny<string>(), It.IsAny<Func<IProgress<double>, Task>>()))
        .Returns((string _, Func<IProgress<double>, Task> action) =>
            action(new Progress<double>()));
    IUserInteraction userInteraction = userInteractionMock.Object;
    return new ArchiveProvider(
        new GitRemoteClient(githubProxy: null),
        new CacheService(fileSystem, userInteraction),
        fileSystem,
        new FileDownloader(userInteraction));
  }

  [WinX64Theory]
  [InlineData("github.com/LiteLDev/LeviLamina")]
  [InlineData("github.com/LiteLDev/LegacyMoney")]
  public async Task ListVersionsOpenVersion_NewestVersion_ReadsPackage(string spec) {
    // Arrange
    ArchiveProvider provider = CreateProvider();

    // Act
    IReadOnlyList<SpecVersion> versions = await provider.ListVersions(spec);
    ArchiveHandle archive = await provider.OpenVersion(spec, versions[0]);
    PackageDocument document = await new ArchiveReader().Read(archive);

    // Assert
    TestContext.Current.TestOutputHelper?.WriteLine(
        $"{spec}: {versions.Count} versions, newest {versions[0].Tag}, read from {(document.IsLegacy ? "tooth.json" : "lip.yaml")}");
    Assert.True(versions.Count > 10);
    Assert.Equal($"{spec}@{versions[0].Version}", archive.Key);
    Assert.Matches("^[0-9a-f]{40}$", archive.Commit);
    Assert.Equal(spec, document.Id, ignoreCase: true);
  }

  [WinX64Theory]
  [InlineData(_releaseZipUrl)]
  public async Task Open_ReleaseZip_ReadsItsFiles(string url) {
    // Arrange
    ArchiveProvider provider = CreateProvider();

    // Act
    ArchiveHandle archive = await provider.Open(Ref.Parse(url, allowFile: false));
    ArchiveHandle again = await CreateProvider().Open(Ref.Parse(url, allowFile: false));

    // Assert
    Assert.Equal(url, archive.Key);
    Assert.Null(archive.Commit);
    Assert.NotEmpty(archive.Source.Keys);
    Assert.Equal(archive.Source.Keys.Order(), again.Source.Keys.Order());
  }
}
