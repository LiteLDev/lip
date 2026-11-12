using System.Runtime.InteropServices;
using System.Text;

namespace Lip.Cli.Tui;

/// The terminal the interface draws to and reads keys from.
public interface ITerminal {
  int Width { get; }
  int Height { get; }

  /// Writes text with escape sequences.
  void Write(string text);

  /// Waits for the next key. Throws when there will be none.
  ConsoleKeyInfo ReadKey();
}

/// The console of lip, switched to its alternate screen until disposed.
public sealed class ConsoleTerminal : ITerminal, IDisposable {
  private const int StdOutputHandle = -11;
  private const uint EnableVirtualTerminalProcessing = 0x0004;

  private readonly Encoding _encoding = Console.OutputEncoding;
  private readonly bool _treatControlCAsInput = Console.TreatControlCAsInput;
  private readonly uint? _mode;
  private int _restored;

  public ConsoleTerminal() {
    Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    Console.TreatControlCAsInput = true;
    if (OperatingSystem.IsWindows()
        && GetConsoleMode(GetStdHandle(StdOutputHandle), out uint mode)) {
      _mode = mode;
      SetConsoleMode(GetStdHandle(StdOutputHandle), mode | EnableVirtualTerminalProcessing);
    }

    // The alternate screen, without the cursor.
    Write("\x1b[?1049h\x1b[?25l\x1b[2J");
    AppDomain.CurrentDomain.ProcessExit += (_, _) => Restore();
  }

  public int Width => Console.WindowWidth;
  public int Height => Console.WindowHeight;

  public void Write(string text) {
    Console.Out.Write(text);
    Console.Out.Flush();
  }

  public ConsoleKeyInfo ReadKey() => Console.ReadKey(intercept: true);

  public void Dispose() => Restore();

  private void Restore() {
    if (Interlocked.Exchange(ref _restored, 1) != 0) {
      return;
    }

    Write("\x1b[0m\x1b[?25h\x1b[?1049l");
    if (_mode is uint mode) {
      SetConsoleMode(GetStdHandle(StdOutputHandle), mode);
    }

    Console.TreatControlCAsInput = _treatControlCAsInput;
    Console.OutputEncoding = _encoding;
  }

  [DllImport("kernel32.dll", SetLastError = true)]
  private static extern nint GetStdHandle(int handle);

  [DllImport("kernel32.dll", SetLastError = true)]
  private static extern bool GetConsoleMode(nint handle, out uint mode);

  [DllImport("kernel32.dll", SetLastError = true)]
  private static extern bool SetConsoleMode(nint handle, uint mode);
}
