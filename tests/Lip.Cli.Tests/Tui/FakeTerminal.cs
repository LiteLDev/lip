using System.Collections.Concurrent;
using Lip.Cli.Tui;

namespace Lip.Cli.Tests.Tui;

/// A terminal whose keys the test presses.
public sealed class FakeTerminal(int width = 120, int height = 36) : ITerminal, IDisposable {
  private readonly BlockingCollection<ConsoleKeyInfo> _keys = new();
  private readonly CancellationTokenSource _closed = new();
  private long _pressed;

  public int Width { get; set; } = width;
  public int Height { get; set; } = height;

  /// How many keys the test has pressed.
  public long Pressed => Interlocked.Read(ref _pressed);

  public void Write(string text) {
  }

  public ConsoleKeyInfo ReadKey() => _keys.Take(_closed.Token);

  public void Press(ConsoleKey key, ConsoleModifiers modifiers = 0, char character = '\0') =>
      Add(new ConsoleKeyInfo(
          character, key,
          shift: modifiers.HasFlag(ConsoleModifiers.Shift),
          alt: modifiers.HasFlag(ConsoleModifiers.Alt),
          control: modifiers.HasFlag(ConsoleModifiers.Control)));

  /// Presses the keys of the characters of a text.
  public void Type(string text) {
    foreach (char c in text) {
      ConsoleKey key = char.IsAsciiLetter(c) ? ConsoleKey.A + (char.ToUpperInvariant(c) - 'A')
          : c == ' ' ? ConsoleKey.Spacebar
          : 0;
      Add(new ConsoleKeyInfo(c, key, shift: char.IsUpper(c), alt: false, control: false));
    }
  }

  /// The terminal is gone: the interface that reads its keys ends.
  public void Dispose() {
    _closed.Cancel();
  }

  private void Add(ConsoleKeyInfo key) {
    Interlocked.Increment(ref _pressed);
    _keys.Add(key);
  }
}
