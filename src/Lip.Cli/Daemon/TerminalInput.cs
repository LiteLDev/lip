namespace Lip.Cli.Daemon;

/// The client end of the interface that the daemon carries out (protocol/lip-daemon.md,
/// "Carrying out a command"): passes on the keys that the user presses on the console of the
/// client as Key frames, and the size of the console as Resize frames, at once and whenever it
/// changes, until disposed. The console is polled, so that disposing stops the reading without
/// taking a key that the user presses later.
public sealed class TerminalInput : IDisposable {
  private static readonly TimeSpan s_poll = TimeSpan.FromMilliseconds(15);
  private static readonly TimeSpan s_sizePoll = TimeSpan.FromMilliseconds(200);

  private readonly FrameWriter _writer;
  private readonly CancellationTokenSource _stopped = new();
  private readonly Thread _thread;

  private TerminalInput(FrameWriter writer) {
    _writer = writer;
    _thread = new Thread(Pump) { IsBackground = true, Name = "lip terminal input" };
  }

  /// Starts passing on the keys and the size of the console with writer.
  public static IDisposable Start(FrameWriter writer) {
    TerminalInput input = new(writer);
    input._thread.Start();
    return input;
  }

  public void Dispose() {
    _stopped.Cancel();
    if (Thread.CurrentThread != _thread) {
      _thread.Join(TimeSpan.FromSeconds(1));
    }
  }

  private void Pump() {
    (int, int) size = (-1, -1);
    long sizeCheckedAt = long.MinValue;
    try {
      while (!_stopped.IsCancellationRequested) {
        if (Environment.TickCount64 - sizeCheckedAt >= s_sizePoll.TotalMilliseconds) {
          sizeCheckedAt = Environment.TickCount64;
          (int, int) now = (Console.WindowWidth, Console.WindowHeight);
          if (now != size) {
            size = now;
            if (!_writer.Send(FrameType.Resize, ResizeMessage.Encode(now.Item1, now.Item2))) {
              return;
            }
          }
        }

        if (!Console.KeyAvailable) {
          _stopped.Token.WaitHandle.WaitOne(s_poll);
          continue;
        }

        ConsoleKeyInfo key = Console.ReadKey(intercept: true);
        if (!_writer.Send(FrameType.Key, KeyMessage.Encode(key))) {
          return;
        }
      }
    }
    catch (Exception ex) when (ex is IOException or InvalidOperationException) {
      // The console is gone; the daemon finds the client gone when it exits.
    }
  }
}
