namespace Lip.Cli.Daemon;

/// The client end of the interface that the daemon carries out (protocol/lip-daemon.md,
/// "Carrying out a command"): passes on the keys that the user presses on the console of the
/// client as Key frames, and the size of the console as Resize frames, at once and whenever it
/// changes, until disposed. The console is polled, so that disposing stops the reading without
/// taking a key that the user presses later.
public sealed class TerminalInput : IDisposable {
  private static readonly TimeSpan s_poll = TimeSpan.FromMilliseconds(15);

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
    TerminalSize size = new(
        () => Environment.TickCount64, () => (Console.WindowWidth, Console.WindowHeight));
    try {
      while (!_stopped.IsCancellationRequested) {
        if (size.Changed() is var (width, height)
            && !_writer.Send(FrameType.Resize, ResizeMessage.Encode(width, height))) {
          return;
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

/// The size of a terminal, read every now and then: at once, and then no sooner than 200 ms
/// after it was last read. clock gives the time in milliseconds.
public sealed class TerminalSize(Func<long> clock, Func<(int Width, int Height)> read) {
  private const long PollMilliseconds = 200;

  private (int, int)? _size;
  private long _readAt;

  /// The size, when it is read now and is not what it was when it was last read; else null.
  public (int Width, int Height)? Changed() {
    long now = clock();
    if (_size is not null && now - _readAt < PollMilliseconds) {
      return null;
    }

    _readAt = now;
    (int, int) size = read();
    if (size == _size) {
      return null;
    }

    _size = size;
    return size;
  }
}
