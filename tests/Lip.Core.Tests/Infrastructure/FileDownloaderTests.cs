using System.IO.Abstractions.TestingHelpers;
using System.Text;
using Flurl;
using Flurl.Http.Testing;
using Lip.Core.Infrastructure;
using Moq;

namespace Lip.Core.Tests.Infrastructure;

/// github_proxy for every download (protocol/config.toml.md, "User configuration"): a request to
/// GitHub goes through the proxy first, then directly when that fails.
public class FileDownloaderTests {
  private const string GithubUrl = "https://github.com/owner/repo/releases/download/v1/a.zip";
  private const string ProxiedUrl =
      "https://proxy.example/gh/owner/repo/releases/download/v1/a.zip";

  private static readonly Url s_proxy = Url.Parse("https://proxy.example/gh");

  private readonly MockFileSystem _fs = new();

  private static FileDownloader Create(Url? proxy) {
    Mock<IUserInteraction> ui = new();
    ui.Setup(u => u.RunWithProgress(It.IsAny<string>(), It.IsAny<Func<IProgress<double>, Task>>()))
        .Returns((string _, Func<IProgress<double>, Task> action) =>
            action(new Progress<double>()));
    return new(ui.Object, proxy);
  }

  [Fact]
  public async Task DownloadFile_GithubUrlWithProxy_DownloadsThroughTheProxy() {
    using HttpTest http = new();
    http.ForCallsTo(ProxiedUrl).RespondWith("proxied");

    await Create(s_proxy).DownloadFile(GithubUrl, _fs.FileInfo.New("/a.zip"));

    Assert.Equal("proxied", _fs.File.ReadAllText("/a.zip"));
    http.ShouldHaveCalled(ProxiedUrl).Times(1);
    http.ShouldNotHaveCalled(GithubUrl);
  }

  [Fact]
  public async Task DownloadFile_ProxyFails_DownloadsDirectly() {
    using HttpTest http = new();
    http.ForCallsTo(ProxiedUrl).RespondWith("bad gateway", 502);
    http.ForCallsTo(GithubUrl).RespondWith("direct");

    await Create(s_proxy).DownloadFile(GithubUrl, _fs.FileInfo.New("/a.zip"));

    Assert.Equal("direct", _fs.File.ReadAllText("/a.zip"));
  }

  [Fact]
  public async Task DownloadFile_ProxyServesWhatValidateRejects_DownloadsDirectly() {
    using HttpTest http = new();
    http.ForCallsTo(ProxiedUrl).RespondWith("<html>error</html>");
    http.ForCallsTo(GithubUrl).RespondWith("direct");
    List<string> validated = [];

    await Create(s_proxy).DownloadFile(GithubUrl, _fs.FileInfo.New("/a.zip"), url => {
      validated.Add(url.ToString());
      if (_fs.File.ReadAllText("/a.zip") != "direct") {
        throw new InvalidDataException("not the file");
      }
    });

    Assert.Equal("direct", _fs.File.ReadAllText("/a.zip"));
    Assert.Equal([ProxiedUrl, GithubUrl], validated);
  }

  [Fact]
  public async Task DownloadFile_ProxyAndGithubFail_ThrowsWithBothFailures() {
    using HttpTest http = new();
    http.RespondWith("not found", 404);

    HttpRequestException ex = await Assert.ThrowsAsync<HttpRequestException>(
        () => Create(s_proxy).DownloadFile(GithubUrl, _fs.FileInfo.New("/a.zip")));

    Assert.Contains("neither through the GitHub proxy", ex.Message);
    Assert.Equal(2, Assert.IsType<AggregateException>(ex.InnerException).InnerExceptions.Count);
  }

  [Fact]
  public async Task DownloadFile_OtherHostWithProxy_DownloadsDirectly() {
    using HttpTest http = new();
    http.RespondWith("direct");

    await Create(s_proxy).DownloadFile(
        "https://example.com/a.zip", _fs.FileInfo.New("/a.zip"));

    http.ShouldHaveCalled("https://example.com/a.zip").Times(1);
    http.ShouldNotHaveCalled("https://proxy.example/*");
  }

  [Fact]
  public async Task DownloadFile_NoProxy_DownloadsDirectlyOnly() {
    using HttpTest http = new();
    http.RespondWith("not found", 404);

    HttpRequestException ex = await Assert.ThrowsAsync<HttpRequestException>(
        () => Create(null).DownloadFile(GithubUrl, _fs.FileInfo.New("/a.zip")));

    Assert.Equal(System.Net.HttpStatusCode.NotFound, ex.StatusCode);
    http.ShouldHaveCalled(GithubUrl).Times(1);
  }

  [Fact]
  public async Task Download_ProxyFails_ReturnsWhatGithubServes() {
    using HttpTest http = new();
    http.ForCallsTo(ProxiedUrl).RespondWith("bad gateway", 502);
    http.ForCallsTo(GithubUrl).RespondWith("direct");

    byte[] content = await Create(s_proxy).Download(GithubUrl);

    Assert.Equal("direct", Encoding.UTF8.GetString(content));
  }

  [Fact]
  public async Task Download_Direct_NeverUsesTheProxy() {
    using HttpTest http = new();
    http.RespondWith("direct");

    byte[] content = await Create(s_proxy).Download(GithubUrl, direct: true);

    Assert.Equal("direct", Encoding.UTF8.GetString(content));
    http.ShouldNotHaveCalled("https://proxy.example/*");
  }
}
