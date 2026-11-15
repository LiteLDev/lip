using Lip.Core.Infrastructure;

namespace Lip.Core.Tests.Infrastructure;

/// The reachability check of GitHub (protocol/lip-tui.md, "GitHub reachability").
public class GithubProbeTests {
  [Fact]
  public async Task Check_Status200_Succeeds() {
    // Arrange
    List<string> sent = [];
    GithubProbe probe = new((url, _) => {
      sent.Add(url.ToString());
      return Task.FromResult(200);
    });

    // Act
    string? failure = await probe.Check(null, TestContext.Current.CancellationToken);

    // Assert
    Assert.Null(failure);
    Assert.Equal([GithubProbe.ProbeUrl], sent);
  }

  [Fact]
  public async Task Check_ThroughAProxy_SendsTheRequestToTheProxyOnly() {
    // Arrange
    List<string> sent = [];
    GithubProbe probe = new((url, _) => {
      sent.Add(url.ToString());
      return Task.FromResult(204);
    });

    // Act
    string? failure = await probe.Check(
        "https://proxy.example/gh/", TestContext.Current.CancellationToken);

    // Assert
    Assert.Null(failure);
    Assert.Equal(
        ["https://proxy.example/gh/LiteLDev/LeviLamina.git/info/refs?service=git-upload-pack"],
        sent);
  }

  [Theory]
  [InlineData(404, "HTTP status 404")]
  [InlineData(502, "HTTP status 502")]
  public async Task Check_StatusOtherThan2xx_Fails(int status, string expected) {
    GithubProbe probe = new((_, _) => Task.FromResult(status));

    string? failure = await probe.Check(null, TestContext.Current.CancellationToken);

    Assert.Contains(expected, failure);
  }

  [Fact]
  public async Task Check_ConnectionFails_FailsWithTheReason() {
    GithubProbe probe = new((_, _) => throw new HttpRequestException("connection refused"));

    string? failure = await probe.Check(null, TestContext.Current.CancellationToken);

    Assert.Contains("connection refused", failure);
  }

  [Fact]
  public async Task Check_NoAnswerInTime_FailsAfterTheTimeout() {
    // Arrange: a request that waits until it is cancelled.
    GithubProbe probe = new(
        async (_, token) => {
          await Task.Delay(Timeout.Infinite, token);
          return 200;
        },
        TimeSpan.FromMilliseconds(50));

    // Act
    string? failure = await probe.Check(null, TestContext.Current.CancellationToken);

    // Assert
    Assert.Contains("No answer from GitHub within", failure);
  }

  [Fact]
  public async Task Check_Cancelled_Throws() {
    GithubProbe probe = new(async (_, token) => {
      await Task.Delay(Timeout.Infinite, token);
      return 200;
    });
    using CancellationTokenSource cancel = new();
    cancel.CancelAfter(20);

    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => probe.Check(null, cancel.Token));
  }

  [Fact]
  public async Task Check_ProxyThatIsNotAnHttpUrl_FailsWithoutARequest() {
    bool sent = false;
    GithubProbe probe = new((_, _) => {
      sent = true;
      return Task.FromResult(200);
    });

    string? failure = await probe.Check("not a url", TestContext.Current.CancellationToken);

    Assert.Contains("is not an http:// or https:// URL", failure);
    Assert.False(sent);
  }
}
