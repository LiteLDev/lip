using System.Text;
using System.Threading.Channels;
using Lip.Core.Infrastructure;

namespace Lip.Core.Tests.Infrastructure;

/// lip run with a real process as the server (protocol/lip-run.md, "Standard streams",
/// "Stopping" and EXIT STATUS): a script that appends each line it reads to a file, and exits
/// with code 3 after the line "stop".
public class ProcessProgramStarterTests : IDisposable {
  private readonly string _root =
      Path.Combine(Path.GetTempPath(), "lip-server-test-" + Guid.NewGuid().ToString("N"));

  public ProcessProgramStarterTests() {
    Directory.CreateDirectory(_root);
  }

  public void Dispose() {
    if (Directory.Exists(_root)) {
      Directory.Delete(_root, recursive: true);
    }

    GC.SuppressFinalize(this);
  }

  [Fact]
  public async Task Run_RealServer_ReadsInputThenStopAfterInputEndsAndReturnsItsExitCode() {
    // Arrange
    ProgramStartInfo start = OperatingSystem.IsWindows()
        ? new(
            "powershell.exe",
            [
              "-NoProfile", "-NonInteractive", "-Command",
              "while ($null -ne ($l = [Console]::In.ReadLine())) {"
                  + " Add-Content -LiteralPath lines.txt -Value $l;"
                  + " if ($l -eq 'stop') { exit 3 } }",
            ],
            VerbatimArguments: false, _root, new Dictionary<string, string>())
        : new(
            "sh",
            [
              "-c",
              "while IFS= read -r l; do printf '%s\\n' \"$l\" >> lines.txt;"
                  + " if [ \"$l\" = stop ]; then exit 3; fi; done",
            ],
            VerbatimArguments: false, _root, new Dictionary<string, string>());
    EndingInput input = new("say hello\n");
    Action? stop = null;

    // Act
    Task<int> run = ServerSession.Run(
        new ProcessProgramStarter(), start, input, inputIsConsole: false, s => {
          stop = s;
          return new MemoryStream();
        });
    await input.Ended.Task.WaitAsync(TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);
    stop!();
    stop();
    int code = await run.WaitAsync(TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);

    // Assert
    Assert.Equal(3, code);
    Assert.Equal(
        ["say hello", "stop"],
        await File.ReadAllLinesAsync(Path.Join(_root, "lines.txt"), TestContext.Current.CancellationToken));
  }

  /// The server of the interface (protocol/lip-tui.md, "Server"): lip reads what it writes to
  /// its standard output and its standard error.
  [Fact]
  public async Task Run_PipedServer_GivesItsOutputAndErrorToLip() {
    // Arrange
    ProgramStartInfo start = OperatingSystem.IsWindows()
        ? new(
            "powershell.exe",
            [
              "-NoProfile", "-NonInteractive", "-Command",
              "while ($null -ne ($l = [Console]::In.ReadLine())) {"
                  + " if ($l -eq 'stop') { [Console]::Error.WriteLine('bye'); exit 5 }"
                  + " [Console]::Out.WriteLine('got ' + $l) }",
            ],
            VerbatimArguments: false, _root, new Dictionary<string, string>())
        : new(
            "sh",
            [
              "-c",
              "while IFS= read -r l; do if [ \"$l\" = stop ]; then echo bye >&2; exit 5; fi;"
                  + " echo \"got $l\"; done",
            ],
            VerbatimArguments: false, _root, new Dictionary<string, string>());
    EndingInput input = new("say hello\n");
    MemoryStream output = new();
    Action? stop = null;

    // Act
    Task<int> run = ServerSession.Run(
        new PipedProgramStarter(bytes => output.Write(bytes)), start, input,
        inputIsConsole: false, s => {
          stop = s;
          return new MemoryStream();
        });
    await input.Ended.Task.WaitAsync(TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);
    stop!();
    int code = await run.WaitAsync(TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);

    // Assert
    Assert.Equal(5, code);
    string text = Encoding.UTF8.GetString(output.ToArray());
    Assert.Contains("got say hello", text);
    Assert.Contains("bye", text);
  }

  /// Gives the text, then ends.
  private sealed class EndingInput(string text) : Stream {
    private readonly Channel<byte[]> _chunks = CreateChunks(text);

    public TaskCompletionSource Ended { get; } = new();

    private static Channel<byte[]> CreateChunks(string text) {
      Channel<byte[]> chunks = Channel.CreateUnbounded<byte[]>();
      chunks.Writer.TryWrite(Encoding.UTF8.GetBytes(text));
      chunks.Writer.Complete();
      return chunks;
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer, CancellationToken cancellationToken = default) {
      if (!await _chunks.Reader.WaitToReadAsync(cancellationToken)) {
        // The text was written before this read.
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
