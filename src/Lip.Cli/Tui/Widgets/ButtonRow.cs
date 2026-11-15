using Lip.Cli.Tui.I18n;

namespace Lip.Cli.Tui.Widgets;

/// A button: its label, in the language of the interface when it is drawn, and what it does.
public sealed class Button(Func<string> label, Action run) {
  public Button(string label, Action run) : this(() => label, run) {
  }

  public string Label => label();

  /// Whether the button can be chosen; one that cannot is drawn muted and skipped.
  public Func<bool>? Enabled { get; init; }

  public bool IsEnabled => Enabled?.Invoke() ?? true;

  public void Run() => run();
}

/// Buttons side by side: ← and → choose one, Enter or Space presses it.
public sealed class ButtonRow(params Button[] buttons) {
  private int _selected;

  public IReadOnlyList<Button> Buttons => buttons;

  /// The index of the chosen button.
  public int Selected {
    get => _selected;
    set => _selected = Math.Clamp(value, 0, Math.Max(0, buttons.Length - 1));
  }

  public IReadOnlyList<Hint> Hints =>
      buttons.Length > 1
          ? [CommonText.HintSwitch, CommonText.HintSelect]
          : [CommonText.HintSelect];

  /// Chooses or presses a button by a key; false when the key does neither: ← on the first
  /// button and → on the last one too, so that the owner can move the focus.
  public bool OnKey(ConsoleKeyInfo key) {
    Settle();
    switch (key.Key) {
      case ConsoleKey.LeftArrow:
        return Move(-1);
      case ConsoleKey.RightArrow:
        return Move(1);
      case ConsoleKey.Enter or ConsoleKey.Spacebar:
        if (_selected < buttons.Length && buttons[_selected].IsEnabled) {
          buttons[_selected].Run();
        }

        return true;
      default:
        return false;
    }
  }

  /// Draws the buttons from (x, y), up to the column limit; the chosen one is highlighted when
  /// the row has the focus. Returns the column after the last one.
  public int Draw(ScreenBuffer screen, int x, int y, bool focused, int limit = int.MaxValue) {
    Settle();
    for (int i = 0; i < buttons.Length; i++) {
      Style style = !buttons[i].IsEnabled ? Theme.Muted
          : i == _selected && focused ? Theme.Selected
          : i == _selected ? Theme.Strong
          : default;
      x = screen.Write(x, y, $"[ {buttons[i].Label} ]", style, limit) + 1;
    }

    return x;
  }

  private bool Move(int step) {
    for (int i = _selected + step; i >= 0 && i < buttons.Length; i += step) {
      if (buttons[i].IsEnabled) {
        _selected = i;
        return true;
      }
    }

    return false;
  }

  /// The chosen button is one that can be chosen, when there is any.
  private void Settle() {
    if (_selected < buttons.Length && buttons[_selected].IsEnabled) {
      return;
    }

    int enabled = Array.FindIndex(buttons, b => b.IsEnabled);
    if (enabled >= 0) {
      _selected = enabled;
    }
  }
}
