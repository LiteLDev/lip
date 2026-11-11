using System.Text;
using System.Threading.Channels;
using Lip.Core.Infrastructure;

namespace Lip.Core.Tests.Infrastructure;

/// protocol/lip-run.md, "Standard streams", "Stopping" and EXIT STATUS, with a fake server.
public class ServerSessionTests {
  private static readonly ProgramStartInfo _start = new(
      "server", [], VerbatimArguments: false, "/ws", new Dictionary<string, string>());

  private readonly FakeInput _input = new();
  private readonly FakeServer _server = new();
  private readonly FakeStopListener _stop = new();

  private static CancellationToken Token => TestContext.Current.CancellationToken;

  private Task<int> Run(bool inputIsConsole = false) =>
      ServerSession.Run(new FakeStarter(_server), _start, _input, inputIsConsole, _stop.Listen);

  [Fact]
  public async Task Run_ServerExits_ReturnsItsExitCode() {
    // Arrange
    Task<int> run = Run();

    // Act
    _server.Exit(42);

    // Assert
    Assert.Equal(42, await run);
  }

  [Fact]
  public async Task Run_Input_IsForwardedInOrder() {
    // Arrange
    Task<int> run = Run();

    // Act
    _input.Send("list\n");
    _input.Send("say hi\n");

    // Assert
    Assert.Equal("list\nsay hi\n", await _server.StandardInputUntil("say hi\n"));
    _server.Exit(0);
    await run;
  }

  [Fact]
  public async Task Run_InputEnds_KeepsTheStandardInputOfTheServerOpen() {
    // Arrange
    Task<int> run = Run();
    _input.Send("list\n");

    // Act
    _input.End();
    await _input.Ended.Task.WaitAsync(Token);
    _stop.Request();

    // Assert: the server still reads, so it reads the line "stop".
    Assert.Equal("list\nstop\n", await _server.StandardInputUntil("stop\n"));
    Assert.False(_server.StandardInputClosed);
    _server.Exit(0);
    await run;
  }

  [Fact]
  public async Task Run_ConsoleReadsNothing_KeepsForwarding() {
    // Arrange: a console read that Ctrl+C interrupts reads nothing.
    Task<int> run = Run(inputIsConsole: true);
    _input.Send("list\n");

    // Act
    _input.SendNothing();
    _input.Send("say hi\n");

    // Assert
    Assert.Equal("list\nsay hi\n", await _server.StandardInputUntil("say hi\n"));
    _server.Exit(0);
    await run;
  }

  [Fact]
  public async Task Run_AskedToStopTwice_WritesStopOnceAndWaitsForTheServer() {
    // Arrange
    Task<int> run = Run();

    // Act
    _stop.Request();
    Assert.Equal("stop\n", await _server.StandardInputUntil("stop\n"));
    _stop.Request();
    _stop.Request();

    // Assert
    Assert.False(run.IsCompleted);
    _server.Exit(7);
    Assert.Equal(7, await run);
    Assert.Equal("stop\n", _server.StandardInputText);
    Assert.True(_stop.Disposed);
  }

  [Fact]
  public async Task Run_AskedToStopBeforeTheServerStarted_WritesStopOnceStarted() {
    // Arrange
    FakeStarter starter = new(_server) { OnStart = _stop.Request };

    // Act
    Task<int> run = ServerSession.Run(
        starter, _start, _input, inputIsConsole: false, _stop.Listen);

    // Assert
    Assert.Equal("stop\n", await _server.StandardInputUntil("stop\n"));
    _server.Exit(0);
    await run;
  }

  [Fact]
  public async Task Run_CannotStart_ThrowsAndStopsListening() {
    // Arrange
    FakeStarter starter = new(_server) {
      OnStart = () => throw new System.ComponentModel.Win32Exception(2, "not found"),
    };

    // Act & Assert
    await Assert.ThrowsAsync<System.ComponentModel.Win32Exception>(
        () => ServerSession.Run(starter, _start, _input, inputIsConsole: false, _stop.Listen));
    Assert.True(_stop.Disposed);
  }

  private sealed class FakeStopListener {
    private Action? _stop;

    public bool Disposed { get; private set; }

    public IDisposable Listen(Action stop) {
      _stop = stop;
      return new Registration(this);
    }

    public void Request() => _stop!();

    private sealed class Registration(FakeStopListener owner) : IDisposable {
      public void Dispose() => owner.Disposed = true;
    }
  }

  private sealed class FakeStarter(FakeServer server) : IProgramStarter {
    public Action? OnStart { get; init; }

    public IStartedProgram Start(ProgramStartInfo start) {
      OnStart?.Invoke();
      return server;
    }
  }

  /// A server whose standard input records what it reads.
  internal sealed class FakeServer : IStartedProgram {
    private readonly TaskCompletionSource<int> _exit = new();
    private readonly RecordingStream _stdin = new();

    public Stream StandardInput => _stdin;

    public bool StandardInputClosed => _stdin.Closed;

    public string StandardInputText => _stdin.Text;

    public void Exit(int code) => _exit.SetResult(code);

    public Task<int> WaitForExit() => _exit.Task;

    /// What the server read, once it ends with suffix.
    public async Task<string> StandardInputUntil(string suffix) {
      while (!_stdin.Text.EndsWith(suffix, StringComparison.Ordinal)) {
        await _stdin.Written.Reader.ReadAsync(Token);
      }

      return _stdin.Text;
    }

    public void Dispose() {
    }
  }

  private sealed class RecordingStream : Stream {
    private readonly StringBuilder _text = new();

    public Channel<bool> Written { get; } = Channel.CreateUnbounded<bool>();

    public bool Closed { get; private set; }

    public string Text {
      get {
        lock (_text) {
          return _text.ToString();
        }
      }
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => !Closed;
    public override long Length => throw new NotSupportedException();
    public override long Position {
      get => throw new NotSupportedException();
      set => throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count) {
      ObjectDisposedException.ThrowIf(Closed, this);
      lock (_text) {
        _text.Append(Encoding.UTF8.GetString(buffer, offset, count));
      }

      Written.Writer.TryWrite(true);
    }

    public override void Flush() {
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing) {
      Closed = true;
      base.Dispose(disposing);
    }
  }

  /// The standard input of lip: the chunks sent, then its end.
  private sealed class FakeInput : Stream {
    private readonly Channel<byte[]> _chunks = Channel.CreateUnbounded<byte[]>();

    public TaskCompletionSource Ended { get; } = new();

    public void Send(string text) => _chunks.Writer.TryWrite(Encoding.UTF8.GetBytes(text));

    /// A read that reads nothing, though the input has not ended.
    public void SendNothing() => _chunks.Writer.TryWrite([]);

    public void End() => _chunks.Writer.Complete();

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer, CancellationToken cancellationToken = default) {
      if (!await _chunks.Reader.WaitToReadAsync(cancellationToken)) {
        Ended.TrySetResult();
        return 0;
      }

      byte[] chunk = await _chunks.Reader.ReadAsync(cancellationToken);
      chunk.CopyTo(buffer);
      return chunk.Length;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position {
      get => throw new NotSupportedException();
      set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override void Flush() {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();
  }
}
