using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Flurl;
using Lip.Core.Infrastructure;
using Lip.Core.Services;
using Lip.Core.Sources;
using Moq;

namespace Lip.Core.Tests.Services;

public class SourceServiceTests {
  private readonly Mock<ICacheService> _mockCacheService;
  private readonly Mock<IFileDownloader> _mockFileDownloader;
  private readonly SourceService _service;

  public SourceServiceTests() {
    _mockCacheService = new Mock<ICacheService>();
    _mockFileDownloader = new Mock<IFileDownloader>();
    _service = new SourceService(
        _mockFileDownloader.Object,
        _mockCacheService.Object,
        githubProxy: null);
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
