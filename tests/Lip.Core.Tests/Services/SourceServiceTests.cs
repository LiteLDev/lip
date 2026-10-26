using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Flurl;
using Lip.Core.Entities;
using Lip.Core.Infrastructure;
using Lip.Core.Services;
using Lip.Core.Sources;
using Moq;
using Semver;

namespace Lip.Core.Tests.Services;

public class SourceServiceTests {
  private readonly Mock<IGitRunner> _mockGitRunner;
  private readonly Mock<IUserInteraction> _mockUserInteraction;
  private readonly Mock<ICacheService> _mockCacheService;
  private readonly Mock<IFileDownloader> _mockFileDownloader;
  private readonly SourceService _service;

  public SourceServiceTests() {
    _mockGitRunner = new Mock<IGitRunner>();
    _mockUserInteraction = new Mock<IUserInteraction>();
    _mockCacheService = new Mock<ICacheService>();
    _mockFileDownloader = new Mock<IFileDownloader>();
    _service = new SourceService(
        _mockFileDownloader.Object,
        _mockGitRunner.Object,
        _mockUserInteraction.Object,
        _mockCacheService.Object,
        githubProxy: null);
  }

  [Fact]
  public async Task Get_LocalPackageSpec_FileNotFound_ThrowsFileNotFoundException() {
    string root = Path.GetPathRoot(Environment.CurrentDirectory) ?? "/";
    string testDir = Path.Combine(root, "test");
    MockFileSystem mockFileSystem = new(new Dictionary<string, MockFileData>(), testDir);
    IFileInfo fileInfo = mockFileSystem.FileInfo.New(Path.Combine(root, "nonexistent.zip"));
    LocalPackageSpec localSpec = new(fileInfo, string.Empty);

    await Assert.ThrowsAsync<FileNotFoundException>(() => _service.Get(localSpec));
  }

  [Fact]
  public async Task Get_LocalPackageSpec_FileExists_ReturnsArchiveSource() {
    string root = Path.GetPathRoot(Environment.CurrentDirectory) ?? "/";
    string packagePath = Path.Combine(root, "package.zip");
    MockFileSystem mockFileSystem = new(new Dictionary<string, MockFileData>
    {
            { packagePath, new MockFileData("content") }
        }, root);
    IFileInfo fileInfo = mockFileSystem.FileInfo.New(packagePath);
    LocalPackageSpec localSpec = new(fileInfo, string.Empty);

    ISource result = await _service.Get(localSpec);

    Assert.IsType<ArchiveSource>(result);
  }

  [Fact]
  public async Task Get_PackageSpec_GitFails_Throws() {
    PackageSpec pkgSpec = new(new PackageId("example.com/pkg", string.Empty), new SemVersion(1, 0, 0));

    _mockCacheService.Setup(c => c.GetOrCreateDirectory(It.IsAny<string>(), It.IsAny<Func<IDirectoryInfo, Task>>()))
        .ThrowsAsync(new Exception("Cache failed"));

    Exception ex = await Assert.ThrowsAnyAsync<Exception>(() => _service.Get(pkgSpec));

    Assert.Equal("Cache failed", ex.Message);
  }

  [Fact]
  public async Task Get_PackageSpec_DoesNotDownloadFiles() {
    PackageSpec pkgSpec = new(new PackageId("github.com/test/repo", string.Empty), new SemVersion(1, 0, 0));
    string root = Path.GetPathRoot(Environment.CurrentDirectory) ?? "/";
    string cacheRepo = Path.Combine(root, "cache", "repo");
    MockFileSystem mockFs = new();
    mockFs.AddDirectory(cacheRepo);
    IDirectoryInfo mockDir = mockFs.DirectoryInfo.New(cacheRepo);

    _mockCacheService.Setup(c => c.GetOrCreateDirectory(It.IsAny<string>(), It.IsAny<Func<IDirectoryInfo, Task>>()))
        .ReturnsAsync(mockDir);

    await _service.Get(pkgSpec);

    _mockCacheService.Verify(
        c => c.GetOrCreateFile(It.IsAny<string>(), It.IsAny<Func<IFileInfo, Task>>()),
        Times.Never);
    _mockFileDownloader.Verify(
        d => d.DownloadFile(It.IsAny<Url>(), It.IsAny<IFileInfo>()),
        Times.Never);
  }

  [Fact]
  public async Task Get_PackageSpec_GitSucceeds_ReturnsDirectorySource() {
    PackageSpec pkgSpec = new(new PackageId("github.com/test/repo", string.Empty), new SemVersion(1, 0, 0));
    string root = Path.GetPathRoot(Environment.CurrentDirectory) ?? "/";
    string cacheRepo = Path.Combine(root, "cache", "repo");
    MockFileSystem mockFs = new();
    mockFs.AddDirectory(cacheRepo);
    IDirectoryInfo mockDir = mockFs.DirectoryInfo.New(cacheRepo);

    _mockCacheService.Setup(c => c.GetOrCreateDirectory(It.IsAny<string>(), It.IsAny<Func<IDirectoryInfo, Task>>()))
        .ReturnsAsync(mockDir);

    ISource result = await _service.Get(pkgSpec);

    Assert.IsType<DirectorySource>(result);
  }

  [Fact]
  public async Task Get_PackageSpec_ViaGit_ClonesTagShallowly() {
    PackageSpec pkgSpec = new(new PackageId("github.com/test/repo", string.Empty), new SemVersion(1, 2, 3));
    string root = Path.GetPathRoot(Environment.CurrentDirectory) ?? "/";
    string cacheRepo = Path.Combine(root, "cache", "repo");
    MockFileSystem mockFs = new();
    mockFs.AddDirectory(cacheRepo);
    IDirectoryInfo mockDir = mockFs.DirectoryInfo.New(cacheRepo);

    _mockCacheService.Setup(c => c.GetOrCreateDirectory(It.IsAny<string>(), It.IsAny<Func<IDirectoryInfo, Task>>()))
        .Returns(async (string _, Func<IDirectoryInfo, Task> createFunc) => {
          await createFunc(mockDir);
          return mockDir;
        });

    await _service.Get(pkgSpec);

    _mockGitRunner.Verify(g => g.Clone(
        "https://github.com/test/repo.git",
        mockDir.FullName,
        "v1.2.3",
        1), Times.Once);
  }

  [Fact]
  public async Task Get_PackageSpec_WithGithubProxy_UsesProxy() {
    SourceService serviceWithProxy = new(
        _mockFileDownloader.Object,
        _mockGitRunner.Object,
        _mockUserInteraction.Object,
        _mockCacheService.Object,
        githubProxy: new Url("https://ghproxy.com"));

    PackageSpec pkgSpec = new(new PackageId("github.com/test/repo", string.Empty), new SemVersion(1, 0, 0));
    string root = Path.GetPathRoot(Environment.CurrentDirectory) ?? "/";
    string cacheRepo = Path.Combine(root, "cache", "repo");
    MockFileSystem mockFs = new();
    mockFs.AddDirectory(cacheRepo);
    IDirectoryInfo mockDir = mockFs.DirectoryInfo.New(cacheRepo);

    _mockCacheService.Setup(c => c.GetOrCreateDirectory(It.Is<string>(k => k.Contains("ghproxy.com")), It.IsAny<Func<IDirectoryInfo, Task>>()))
        .ReturnsAsync(mockDir);

    ISource result = await serviceWithProxy.Get(pkgSpec);

    Assert.IsType<DirectorySource>(result);
  }

  [Fact]
  public async Task Get_RemotePackageSpec_ReturnsArchiveSource() {
    string root = Path.GetPathRoot(Environment.CurrentDirectory) ?? "/";
    string remotePath = Path.Combine(root, "cache", "remote.zip");
    MockFileSystem mockFs = new();
    mockFs.AddFile(remotePath, new MockFileData("content"));
    IFileInfo mockFile = mockFs.FileInfo.New(remotePath);

    _mockCacheService.Setup(c => c.GetOrCreateFile(It.IsAny<string>(), It.IsAny<Func<IFileInfo, Task>>()))
        .ReturnsAsync(mockFile);

    RemotePackageSpec remoteSpec = new(new Url("https://example.com/package.zip"), string.Empty);

    ISource result = await _service.Get(remoteSpec);

    Assert.IsType<ArchiveSource>(result);
  }

  [Fact]
  public async Task Get_Url_IsArchiveTrue_ReturnsArchiveSource() {
    string root = Path.GetPathRoot(Environment.CurrentDirectory) ?? "/";
    string filePath = Path.Combine(root, "cache", "file.zip");
    MockFileSystem mockFs = new();
    mockFs.AddFile(filePath, new MockFileData("content"));
    IFileInfo mockFile = mockFs.FileInfo.New(filePath);

    _mockCacheService.Setup(c => c.GetOrCreateFile(It.IsAny<string>(), It.IsAny<Func<IFileInfo, Task>>()))
        .ReturnsAsync(mockFile);

    ISource result = await _service.Get(new Url("https://example.com/file.zip"), isArchive: true);

    Assert.IsType<ArchiveSource>(result);
  }

  [Fact]
  public async Task Get_Url_IsArchiveFalse_ReturnsSingleFileSource() {
    string root = Path.GetPathRoot(Environment.CurrentDirectory) ?? "/";
    string filePath = Path.Combine(root, "cache", "file.txt");
    MockFileSystem mockFs = new();
    mockFs.AddFile(filePath, new MockFileData("content"));
    IFileInfo mockFile = mockFs.FileInfo.New(filePath);

    _mockCacheService.Setup(c => c.GetOrCreateFile(It.IsAny<string>(), It.IsAny<Func<IFileInfo, Task>>()))
        .ReturnsAsync(mockFile);

    ISource result = await _service.Get(new Url("https://example.com/file.txt"), isArchive: false);

    Assert.IsType<SingleFileSource>(result);
  }

  [Fact]
  public async Task Get_Url_GithubProxyConfigured_ProxyFails_FallsBackToOriginalUrl() {
    SourceService serviceWithProxy = new(
        _mockFileDownloader.Object,
        _mockGitRunner.Object,
        _mockUserInteraction.Object,
        _mockCacheService.Object,
        githubProxy: new Url("https://github.bibk.top"));

    string root = Path.GetPathRoot(Environment.CurrentDirectory) ?? "/";
    string filePath = Path.Combine(root, "cache", "file.zip");
    MockFileSystem mockFs = new();
    mockFs.AddFile(filePath, new MockFileData("content"));
    IFileInfo mockFile = mockFs.FileInfo.New(filePath);

    _mockCacheService.Setup(c => c.GetOrCreateFile(It.IsAny<string>(), It.IsAny<Func<IFileInfo, Task>>()))
        .Returns(async (string key, Func<IFileInfo, Task> factory) => {
          await factory(mockFile);
          return mockFile;
        });

    MockSequence sequence = new();

    _mockFileDownloader.InSequence(sequence)
        .Setup(d => d.DownloadFile(
            It.Is<Url>(u => u.Host == "github.bibk.top"),
            It.IsAny<IFileInfo>()))
        .ThrowsAsync(new HttpRequestException("proxy failed"));

    _mockFileDownloader.InSequence(sequence)
        .Setup(d => d.DownloadFile(
            It.Is<Url>(u => u.Host == "github.com"),
            It.IsAny<IFileInfo>()))
        .Returns(Task.CompletedTask);

    ISource result = await serviceWithProxy.Get(
        new Url("https://github.com/LiteLDev/LeviLamina/releases/download/v1.9.6/levilamina.zip"),
        isArchive: true);

    Assert.IsType<ArchiveSource>(result);
    _mockFileDownloader.Verify(d => d.DownloadFile(
        It.Is<Url>(u => u.Host == "github.bibk.top"),
        It.IsAny<IFileInfo>()), Times.Once);
    _mockFileDownloader.Verify(d => d.DownloadFile(
        It.Is<Url>(u => u.Host == "github.com"),
        It.IsAny<IFileInfo>()), Times.Once);
  }

  [Fact]
  public async Task Get_Url_WithProxyAndNonGithubUrl_DoesNotRewriteUrl() {
    SourceService serviceWithProxy = new(
        _mockFileDownloader.Object,
        _mockGitRunner.Object,
        _mockUserInteraction.Object,
        _mockCacheService.Object,
        githubProxy: new Url("https://github.bibk.top"));

    string root = Path.GetPathRoot(Environment.CurrentDirectory) ?? "/";
    string filePath = Path.Combine(root, "cache", "file.zip");
    MockFileSystem mockFs = new();
    mockFs.AddFile(filePath, new MockFileData("content"));
    IFileInfo mockFile = mockFs.FileInfo.New(filePath);

    _mockCacheService.Setup(c => c.GetOrCreateFile(It.IsAny<string>(), It.IsAny<Func<IFileInfo, Task>>()))
        .Returns(async (string key, Func<IFileInfo, Task> factory) => {
          await factory(mockFile);
          return mockFile;
        });

    _mockFileDownloader.Setup(d => d.DownloadFile(It.IsAny<Url>(), It.IsAny<IFileInfo>()))
        .Returns(Task.CompletedTask);

    Url originalUrl = new("https://example.com/file.zip");

    ISource result = await serviceWithProxy.Get(originalUrl, isArchive: true);

    Assert.IsType<ArchiveSource>(result);
    _mockFileDownloader.Verify(d => d.DownloadFile(
        It.Is<Url>(u => u.ToString() == originalUrl.ToString()),
        It.IsAny<IFileInfo>()), Times.Once);
    _mockFileDownloader.Verify(d => d.DownloadFile(
        It.Is<Url>(u => u.Host == "github.bibk.top"),
        It.IsAny<IFileInfo>()), Times.Never);
  }
}
