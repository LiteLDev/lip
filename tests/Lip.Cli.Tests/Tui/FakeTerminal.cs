using System.Collections.Concurrent;
using Lip.Cli.Tui;

namespace Lip.Cli.Tests.Tui;

/// A terminal whose keys the test presses.
public sealed class FakeTerminal(int width = 120, int height = 36) : ITerminal, IDisposable {
  private readonly BlockingCollection<ConsoleKeyInfo> _keys = new();
  private readonly CancellationTokenSource _closed = new();

  public int Width { get; set; } = width;
  public int Height { get; set; } = height;

  public void Write(string text) {
  }

  public ConsoleKeyInfo ReadKey() => _keys.Take(_closed.Token);

  public void Press(ConsoleKey key, ConsoleModifiers modifiers = 0, char character = '\0') =>
      _keys.Add(new ConsoleKeyInfo(
          character, key,
          shift: modifiers.HasFlag(ConsoleModifiers.Shift),
          alt: modifiers.HasFlag(ConsoleModifiers.Alt),
          control: modifiers.HasFlag(ConsoleModifiers.Control)));

  /// Presses the keys of the characters of a text.
  public void Type(string text) {
    foreach (char c in text) {
      ConsoleKey key = char.IsAsciiLetter(c)
          ? ConsoleKey.A + (char.ToUpperInvariant(c) - 'A')
          : 0;
      _keys.Add(new ConsoleKeyInfo(c, key, shift: char.IsUpper(c), alt: false, control: false));
    }
  }

  public void Dispose() {
    _closed.Cancel();
  }
}
