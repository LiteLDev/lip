namespace Lip.Cli.Tui;

/// A line of text that the user edits.
public sealed class TextField(string value = "") {
  public string Value { get; private set; } = value;
  public int Cursor { get; private set; } = value.Length;

  public void Set(string value) {
    Value = value;
    Cursor = value.Length;
  }

  /// Edits the text by a key; false when the key does not edit it.
  public bool OnKey(ConsoleKeyInfo key) {
    switch (key.Key) {
      case ConsoleKey.LeftArrow:
        Cursor = Math.Max(0, Cursor - 1);
        return true;
      case ConsoleKey.RightArrow:
        Cursor = Math.Min(Value.Length, Cursor + 1);
        return true;
      case ConsoleKey.Home:
        Cursor = 0;
        return true;
      case ConsoleKey.End:
        Cursor = Value.Length;
        return true;
      case ConsoleKey.Backspace:
        if (Cursor > 0) {
          Value = Value.Remove(Cursor - 1, 1);
          Cursor--;
        }
        return true;
      case ConsoleKey.Delete:
        if (Cursor < Value.Length) {
          Value = Value.Remove(Cursor, 1);
        }
        return true;
    }

    if (key.Modifiers.HasFlag(ConsoleModifiers.Control)) {
      if (key.Key == ConsoleKey.U) {
        Set("");
        return true;
      }

      return false;
    }

    if (key.KeyChar >= ' ' && key.KeyChar != '\x7f') {
      Value = Value.Insert(Cursor, key.KeyChar.ToString());
      Cursor++;
      return true;
    }

    return false;
  }

  /// Draws the text in width columns, the part around the cursor when it is longer, with the
  /// cursor when focused, or the placeholder when it is empty and not focused.
  public void Draw(
      ScreenBuffer screen, int x, int y, int width, bool focused, string placeholder = "") {
    if (Value.Length == 0 && !focused) {
      screen.WriteFit(x, y, width, placeholder, Theme.Muted);
      return;
    }

    int start = 0;
    while (TextWidth.Of(Value[start..Cursor]) > width - 1) {
      start++;
    }

    screen.Write(x, y, Value[start..], default, x + width);
    if (focused) {
      int cursor = x + TextWidth.Of(Value[start..Cursor]);
      screen.Restyle(cursor, y, 1, Style.Of(inverse: true));
    }
  }
}

/// A field of a PromptDialog.
public sealed record PromptField(string Label, string Value = "", string Placeholder = "");

public sealed partial class TuiApp {
  /// A window above the tabs that takes every key until it is closed.
  private abstract class Dialog {
    public abstract (string Key, string Text)[] Hints { get; }

    public abstract void Draw(ScreenBuffer screen);

    public abstract void OnKey(ConsoleKeyInfo key);

    /// A rectangle of at most the given size in the middle of the screen, drawn as a panel.
    protected static Rect Frame(
        ScreenBuffer screen, string title, int width, int height, Style? border = null) {
      width = Math.Min(width, screen.Width - 4);
      height = Math.Min(height, screen.Height - 4);
      Rect rect = new((screen.Width - width) / 2, (screen.Height - height) / 2, width, height);
      screen.Fill(rect, default);
      screen.Box(rect, title, border ?? Theme.Accent with { Bold = false }, border ?? Theme.Accent);
      return rect.Inner;
    }

    protected static void Buttons(
        ScreenBuffer screen, Rect inner, IReadOnlyList<string> buttons, int selected) {
      int x = inner.X;
      for (int i = 0; i < buttons.Count; i++) {
        x = screen.Write(
            x, inner.Bottom - 1, $"[ {buttons[i]} ]", i == selected ? Theme.Selected : default) + 1;
      }
    }
  }

  /// Asks for the values of fields.
  private sealed class PromptDialog(
      TuiApp app,
      string title,
      PromptField[] fields,
      Action<string[]> submit,
      Action? cancel = null) : Dialog {
    private readonly TextField[] _fields = [.. fields.Select(f => new TextField(f.Value))];
    private int _focus;

    public override (string, string)[] Hints =>
        fields.Length > 1
            ? [("Enter", "confirm"), ("Tab", "next field"), ("Esc", "cancel")]
            : [("Enter", "confirm"), ("Esc", "cancel")];

    public override void Draw(ScreenBuffer screen) {
      int width = Math.Min(screen.Width - 4, 78);
      List<List<string>> labels = [.. fields.Select(f => TextWidth.Wrap(f.Label, width - 4))];
      Rect inner = Frame(screen, title, width, labels.Sum(l => l.Count + 2) + 2);
      int y = inner.Y;
      for (int i = 0; i < fields.Length; i++) {
        foreach (string line in labels[i]) {
          screen.Write(inner.X, y++, line, Theme.Muted, inner.Right);
        }

        screen.Write(inner.X, y, i == _focus ? "> " : "  ", Theme.Accent);
        _fields[i].Draw(
            screen, inner.X + 2, y, inner.Width - 2, i == _focus, fields[i].Placeholder);
        y += 2;
      }
    }

    public override void OnKey(ConsoleKeyInfo key) {
      switch (key.Key) {
        case ConsoleKey.Escape:
          app.Close(this);
          cancel?.Invoke();
          return;
        case ConsoleKey.Enter:
          app.Close(this);
          submit([.. _fields.Select(f => f.Value.Trim())]);
          return;
        case ConsoleKey.Tab or ConsoleKey.DownArrow or ConsoleKey.UpArrow:
          bool back = key.Key == ConsoleKey.UpArrow
              || key.Modifiers.HasFlag(ConsoleModifiers.Shift);
          _focus = (_focus + (back ? fields.Length - 1 : 1)) % fields.Length;
          return;
      }

      _fields[_focus].OnKey(key);
    }
  }

  /// Shows lines, which scroll, above buttons: from the last line when fromEnd, as for a log
  /// whose end says how it went. Esc chooses the last button.
  private sealed class MessageDialog(
      TuiApp app,
      string title,
      IReadOnlyList<StyledLine> lines,
      string[] buttons,
      Action<int>? chosen = null,
      Style? border = null,
      bool fromEnd = false) : Dialog {
    private int _button;
    private int _top = fromEnd ? int.MaxValue : 0;
    private int _rows = 1;
    private int _total;

    public override (string, string)[] Hints =>
        _total > _rows
            ? [("Enter", "choose"), ("←/→", "button"), ("↑/↓", "scroll"), ("Esc", "close")]
            : [("Enter", "choose"), ("←/→", "button"), ("Esc", "close")];

    public override void Draw(ScreenBuffer screen) {
      int width = Math.Min(screen.Width - 4, 100);
      List<StyledLine> rows = [.. lines.SelectMany(line => TextWidth.Wrap(line, width - 4))];
      Rect inner = Frame(screen, title, width, rows.Count + 4, border);
      _total = rows.Count;
      _rows = Math.Max(1, inner.Height - 2);
      _top = Math.Clamp(_top, 0, Math.Max(0, _total - _rows));
      for (int i = 0; i < _rows && _top + i < rows.Count; i++) {
        screen.Write(inner.X, inner.Y + i, rows[_top + i], inner.Right);
      }

      if (_total > _rows) {
        string position = $" {_top + 1}-{Math.Min(_total, _top + _rows)} of {_total} ";
        screen.Write(inner.Right - position.Length, inner.Bottom - 1, position, Theme.Muted);
      }

      Buttons(screen, inner, buttons, _button);
    }

    public override void OnKey(ConsoleKeyInfo key) {
      switch (key.Key) {
        case ConsoleKey.Escape:
          app.Close(this);
          chosen?.Invoke(buttons.Length - 1);
          return;
        case ConsoleKey.Enter or ConsoleKey.Spacebar:
          app.Close(this);
          chosen?.Invoke(_button);
          return;
        case ConsoleKey.LeftArrow:
          _button = (_button + buttons.Length - 1) % buttons.Length;
          return;
        case ConsoleKey.RightArrow or ConsoleKey.Tab:
          _button = (_button + 1) % buttons.Length;
          return;
      }

      MoveSelection(key, ref _top, Math.Max(1, _total - _rows + 1), _rows);
    }
  }

  /// Lets the user choose one of the items.
  private sealed class ListDialog(
      TuiApp app, string title, IReadOnlyList<StyledLine> items, Action<int> chosen) : Dialog {
    private int _selected;
    private int _top;
    private int _rows = 1;

    public override (string, string)[] Hints =>
        [("Enter", "choose"), ("↑/↓", "select"), ("Esc", "cancel")];

    public override void Draw(ScreenBuffer screen) {
      Rect inner = Frame(screen, title, Math.Min(screen.Width - 4, 70), items.Count + 2);
      _rows = Math.Max(1, inner.Height);
      _top = ScrollTo(_selected, _top, _rows);
      for (int i = 0; i < _rows && _top + i < items.Count; i++) {
        screen.Write(inner.X, inner.Y + i, items[_top + i], inner.Right);
        if (_top + i == _selected) {
          screen.Restyle(inner.X - 1, inner.Y + i, inner.Width + 2, Theme.Selected);
        }
      }
    }

    public override void OnKey(ConsoleKeyInfo key) {
      switch (key.Key) {
        case ConsoleKey.Escape:
          app.Close(this);
          return;
        case ConsoleKey.Enter or ConsoleKey.Spacebar:
          app.Close(this);
          if (items.Count > 0) {
            chosen(_selected);
          }
          return;
      }

      MoveSelection(key, ref _selected, items.Count, _rows, wrap: true);
    }
  }

  /// Shows the operation that runs: what it downloads, and its messages.
  private sealed class ProgressDialog(Operation operation) : Dialog {
    private static readonly string _spinner = "|/-\\";

    public override (string, string)[] Hints =>
        operation.Cancellable ? [("Ctrl+C", "cancel and undo")] : [];

    public override void Draw(ScreenBuffer screen) {
      int width = Math.Min(screen.Width - 4, 100);
      List<ProgressItem> items = [.. operation.Progress.TakeLast(4)];
      Rect inner = Frame(screen, operation.Title, width, Math.Min(screen.Height - 4, 20));
      char spin = _spinner[(int)(Environment.TickCount64 / 150 % _spinner.Length)];
      int y = inner.Y;
      foreach (ProgressItem item in items) {
        int bar = Math.Min(24, inner.Width / 3);
        int filled = item.Done ? bar : bar * item.Percent / 100;
        int x = screen.Write(
            inner.X, y, item.Done ? "✓ " : spin + " ", item.Done ? Theme.Good : Theme.Accent);
        x = screen.WriteFit(x, y, inner.Width - bar - 9, item.Label);
        x = inner.Right - bar - 5;
        screen.Write(x, y, new string('█', filled), Theme.Accent);
        screen.Write(x + filled, y, new string('░', bar - filled), Theme.Border);
        screen.Write(x + bar + 1, y, item.Done ? "done" : $"{item.Percent,3}%", Theme.Muted);
        y++;
      }

      if (items.Count == 0) {
        screen.Write(inner.X, y++, $"{spin} Working...", Theme.Accent);
      }

      y++;
      int rows = inner.Bottom - y;
      List<StyledLine> log = [
        .. operation.Log.TakeLast(rows).SelectMany(line => TextWidth.Wrap(line, inner.Width)),
      ];
      foreach (StyledLine line in log.TakeLast(rows)) {
        screen.Write(inner.X, y++, line, inner.Right);
      }
    }

    public override void OnKey(ConsoleKeyInfo key) {
      bool cancel = key.Key == ConsoleKey.Escape
          || (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control));
      if (cancel && operation.Cancellable && !operation.Cancel.IsCancellationRequested) {
        operation.Log.Add(new StyledLine("Cancelling...", Theme.Warning));
        operation.Cancel.Cancel();
      }
    }
  }
}
