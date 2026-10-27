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
  public async Task ListVersionsOpenVersion_NewestVersion_ReadsRootDocument(string spec) {
    // Arrange
    ArchiveProvider provider = CreateProvider();
    Mock<IDocumentParser> parser = new();
    parser.Setup(p => p.Parse(It.IsAny<byte[]>())).Returns(new RoutingDocument {
      IsMarked = true,
      Artifacts = [],
    });

    // Act
    IReadOnlyList<SpecVersion> versions = await provider.ListVersions(spec);
    ArchiveHandle archive = await provider.OpenVersion(spec, versions[0]);
    ArchiveDocument document = await new ArchiveReader(parser.Object).Read(archive);

    // Assert
    TestContext.Current.TestOutputHelper?.WriteLine(
        $"{spec}: {versions.Count} versions, newest {versions[0].Tag}, {document.GetType().Name}");
    Assert.True(versions.Count > 10);
    Assert.Equal($"{spec}@{versions[0].Version}", archive.Key);
    GitArchiveIdentity identity = Assert.IsType<GitArchiveIdentity>(archive.Identity);
    Assert.Equal($"https://{spec}.git", identity.RepoUrl);
    Assert.Equal(40, identity.Commit.Length);
    if (document is ToothArchiveDocument tooth) {
      Assert.Equal(spec, tooth.Manifest.Path, ignoreCase: true);
    } else {
      Assert.IsType<RoutingArchiveDocument>(document);
    }
  }

  [WinX64Theory]
  [InlineData(_releaseZipUrl)]
  public async Task OpenOpenLocked_ReleaseZip_VerifiesSha256(string url) {
    // Arrange
    ArchiveProvider provider = CreateProvider();

    // Act
    ArchiveHandle archive = await provider.Open(Ref.Parse(url), null!);
    ArchiveHandle locked = await CreateProvider().OpenLocked(archive.Key, archive.Identity);

    // Assert
    ZipArchiveIdentity identity = Assert.IsType<ZipArchiveIdentity>(archive.Identity);
    Assert.Equal(url, archive.Key);
    Assert.Equal(url, identity.Url);
    Assert.Matches("^[0-9a-f]{64}$", identity.Sha256);
    Assert.NotEmpty(archive.Source.Keys);
    Assert.Equal(archive.Source.Keys.Order(), locked.Source.Keys.Order());
  }
}
