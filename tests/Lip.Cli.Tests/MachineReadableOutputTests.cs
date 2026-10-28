using System.Text.Json;
using Lip.Cli.Commands;
using Lip.Core.PublicApi;
using Moq;

namespace Lip.Cli.Tests;

/// The output of --json and of lip view (whose output is redirected under the test runner) is
/// written as is: Spectre.Console would wrap lines longer than the console width (80 columns when
/// redirected), inserting line breaks into JSON strings.
public sealed class MachineReadableOutputTests : IDisposable {
  private static readonly string s_long =
      "https://a-very-long-proxy-hostname-for-testing-line-wrapping.example.com/some/deep/path/";

  private readonly Mock<ILipClient> _client = new();
  private readonly TextWriter _originalOut = Console.Out;
  private readonly StringWriter _out = new();

  public MachineReadableOutputTests() {
    Console.SetOut(_out);
  }

  public void Dispose() {
    Console.SetOut(_originalOut);
  }

  [Fact]
  public async Task View_Redirected_WritesTheDocumentUnchanged() {
    string document = $$"""
        {
          "format_version": 3,
          "info": { "description": "{{s_long}}{{s_long}}" }
        }
        """;
    _client.Setup(c => c.View("pkg")).ReturnsAsync(document);

    await new ViewCommand(_client.Object).ExecuteAsync(
        null!, new ViewCommand.Settings { Package = "pkg" }, CancellationToken.None);

    Assert.Equal(document, _out.ToString().TrimEnd('\r', '\n'));
  }

  [Fact]
  public async Task ListJson_IsValidJson() {
    _client.Setup(c => c.List()).ReturnsAsync(
        ((IEnumerable<string>)[$"{s_long}@1.0.0"], (IEnumerable<string>)["b@2.0.0"]));

    await new ListCommand(_client.Object).ExecuteAsync(
        null!, new ListCommand.Settings { Json = true }, CancellationToken.None);

    using JsonDocument json = JsonDocument.Parse(_out.ToString());
    Assert.Equal(
        $"{s_long}@1.0.0",
        json.RootElement.GetProperty("ExplicitInstalled")[0].GetString());
    Assert.Equal("b@2.0.0", json.RootElement.GetProperty("ImplicitInstalled")[0].GetString());
  }

  [Fact]
  public async Task VersionsJson_IsValidJson() {
    _client.Setup(c => c.Versions("pkg")).ReturnsAsync([s_long + s_long]);

    await new VersionsCommand(_client.Object).ExecuteAsync(
        null!, new VersionsCommand.Settings { Package = "pkg", Json = true },
        CancellationToken.None);

    Assert.Equal(
        [s_long + s_long],
        JsonSerializer.Deserialize<string[]>(_out.ToString())!);
  }

  [Fact]
  public async Task ConfigListJson_IsValidJson() {
    _client.Setup(c => c.ConfigList()).ReturnsAsync(
        new Dictionary<string, string> { ["github_proxy"] = s_long });

    await new ConfigListCommand(_client.Object).ExecuteAsync(
        null!, new ConfigListCommand.Settings { Json = true }, CancellationToken.None);

    Assert.Equal(
        s_long,
        JsonSerializer.Deserialize<Dictionary<string, string>>(_out.ToString())!["github_proxy"]);
  }

  [Fact]
  public async Task ConfigGetJson_WritesTheBareValueOnOneLine() {
    _client.Setup(c => c.ConfigGet("github_proxy")).ReturnsAsync(s_long);

    await new ConfigGetCommand(_client.Object).ExecuteAsync(
        null!, new ConfigGetCommand.Settings { Key = "github_proxy", Json = true },
        CancellationToken.None);

    Assert.Equal(s_long, _out.ToString().TrimEnd('\r', '\n'));
  }
}
