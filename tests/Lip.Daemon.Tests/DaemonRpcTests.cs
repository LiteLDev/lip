using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using Lip.Core.PublicApi;
using Moq;
using Nerdbank.Streams;
using StreamJsonRpc;

namespace Lip.Daemon.Tests;

/// Runs the daemon's JSON-RPC server (as RunCommand sets it up) over an in-memory stream and
/// talks to it with raw Content-Length framed messages, so that the tests see the wire format.
public sealed class DaemonRpcTests : IAsyncDisposable {
  private static readonly string[] s_notifications =
      ["PrintInfo", "PrintSuccess", "PrintWarning", "PrintError", "ReportProgress"];

  private readonly Mock<ILipClient> _client = new();
  private readonly JsonRpc _server;
  private readonly RpcUserInteraction _ui;
  private readonly Stream _wire;
  private int _nextId;

  public DaemonRpcTests() {
    (Stream serverStream, Stream clientStream) = FullDuplexStream.CreatePair();
    _server = new JsonRpc(serverStream, serverStream);
    _ui = new RpcUserInteraction(_server);
    _server.AddLocalRpcTarget(new DaemonRpcTarget(_client.Object));
    _server.StartListening();
    _wire = clientStream;
  }

  public async ValueTask DisposeAsync() {
    _server.Dispose();
    await _wire.DisposeAsync();
  }

  [Fact]
  public void Schema_ServerMethods_AreExactlyTheRpcTargetMethods() {
    JsonNode schema = JsonNode.Parse(File.ReadAllText(
        Path.Join(AppContext.BaseDirectory, "lipd.v4.openrpc.json")))!;
    Dictionary<string, JsonArray> schemaMethods = schema["methods"]!.AsArray()
        .Select(m => m!.AsObject())
        .Where(m => !s_notifications.Contains((string)m["name"]!))
        .ToDictionary(m => (string)m["name"]!, m => m["params"]!.AsArray());

    MethodInfo[] targetMethods = typeof(DaemonRpcTarget).GetMethods(
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);

    Assert.Equal(
        schemaMethods.Keys.Order(StringComparer.Ordinal),
        targetMethods.Select(m => m.Name).Order(StringComparer.Ordinal));

    foreach (MethodInfo method in targetMethods) {
      ParameterInfo[] parameters = [
        .. method.GetParameters().Where(p => p.ParameterType != typeof(CancellationToken)),
      ];
      JsonArray schemaParams = schemaMethods[method.Name];

      Assert.Equal(
          schemaParams.Select(p => (string)p!["name"]!),
          parameters.Select(p => p.Name!));
      foreach ((ParameterInfo parameter, JsonNode? schemaParam) in parameters.Zip(schemaParams)) {
        bool required = schemaParam!["required"]?.GetValue<bool>() ?? true;
        Assert.True(
            required != parameter.HasDefaultValue,
            $"{method.Name}.{parameter.Name}: schema required={required}");
      }
    }
  }

  [Fact]
  public async Task List_ReturnsArrayOfExplicitAndDependencyArrays() {
    _client.Setup(c => c.List()).ReturnsAsync(
        ((IEnumerable<string>)["a@1.0.0"], (IEnumerable<string>)["b@2.0.0", "c@3.0.0"]));

    JsonNode response = await Call("List", new JsonArray());

    Assert.Equal(
        """[["a@1.0.0"],["b@2.0.0","c@3.0.0"]]""",
        response["result"]!.ToJsonString());
  }

  [Fact]
  public async Task Output_IsSentAsNotifications_AndNeedsNoAnswer() {
    _client.Setup(c => c.View("pkg")).Returns(async () => {
      await _ui.PrintInfo("info");
      await _ui.PrintSuccess("success");
      await _ui.PrintWarning("warning");
      await _ui.PrintError("error");
      return "{}";
    });

    List<JsonNode> received = [];
    JsonNode response = await Call("View", new JsonObject { ["package"] = "pkg" }, received);

    Assert.Equal("{}", (string)response["result"]!);
    Assert.Equal(
        ["PrintInfo:info", "PrintSuccess:success", "PrintWarning:warning", "PrintError:error"],
        received.Select(m => $"{m["method"]}:{m["params"]![0]}"));
    Assert.All(received, m => Assert.Null(m["id"]));
  }

  [Fact]
  public async Task RunWithProgress_SendsReportProgressNotifications() {
    _client.Setup(c => c.Init()).Returns(() => _ui.RunWithProgress("Downloading x", progress => {
      progress.Report(50);
      return Task.CompletedTask;
    }));

    List<JsonNode> received = [];
    await Call("Init", new JsonArray(), received);
    // Progress<T> reports asynchronously, possibly after the response.
    while (received.Count == 0) {
      received.Add(await Read());
    }

    JsonNode progress = Assert.Single(received);
    Assert.Equal("ReportProgress", (string)progress["method"]!);
    Assert.Null(progress["id"]);
    Assert.Equal("Downloading x", (string)progress["params"]![1]!);
    Assert.Equal(50, (double)progress["params"]![2]!);
  }

  [Theory]
  [InlineData("Create")]
  [InlineData("Dispose")]
  [InlineData("ToString")]
  public async Task MethodsOutsideTheSchema_AreNotFound(string method) {
    JsonNode response = await Call(method, new JsonArray());

    Assert.Equal(-32601, (int)response["error"]!["code"]!);
  }

  [Fact]
  public async Task Install_OptionalRidMayBeOmitted() {
    JsonNode response = await Call("Install", new JsonObject {
      ["packages"] = new JsonArray("pkg"),
      ["dryRun"] = true,
      ["ignoreScripts"] = false,
      ["noDependencies"] = false,
    });

    Assert.Null(response["error"]);
    _client.Verify(c => c.Install(
        It.Is<IEnumerable<string>>(p => p.SequenceEqual(new[] { "pkg" })),
        true, false, false, null, It.IsAny<CancellationToken>()));
  }

  /// Sends a request and reads until its response, collecting the other messages.
  private async Task<JsonNode> Call(string method, JsonNode @params, List<JsonNode>? others = null) {
    int id = ++_nextId;
    byte[] body = Encoding.UTF8.GetBytes(new JsonObject {
      ["jsonrpc"] = "2.0",
      ["id"] = id,
      ["method"] = method,
      ["params"] = @params,
    }.ToJsonString());
    await _wire.WriteAsync(Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n"));
    await _wire.WriteAsync(body);
    await _wire.FlushAsync();

    while (true) {
      JsonNode message = await Read();
      if (message["method"] is null && (int?)message["id"] == id) {
        return message;
      }
      others?.Add(message);
    }
  }

  private async Task<JsonNode> Read() {
    using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
    int length = -1;
    while (true) {
      string line = await ReadLine(timeout.Token);
      if (line.Length == 0) {
        break;
      }
      if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) {
        length = int.Parse(line["Content-Length:".Length..].Trim());
      }
    }

    byte[] body = new byte[length];
    await _wire.ReadExactlyAsync(body, timeout.Token);
    return JsonNode.Parse(body)!;
  }

  private async Task<string> ReadLine(CancellationToken cancellationToken) {
    StringBuilder line = new();
    byte[] one = new byte[1];
    while (true) {
      await _wire.ReadExactlyAsync(one, cancellationToken);
      if (one[0] == '\n') {
        return line.ToString().TrimEnd('\r');
      }
      line.Append((char)one[0]);
    }
  }
}
