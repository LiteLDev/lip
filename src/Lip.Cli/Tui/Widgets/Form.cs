using Lip.Cli.Tui.I18n;

namespace Lip.Cli.Tui.Widgets;

/// A row of a Form: a label, in the language of the interface when it is drawn, and a value
/// that the user changes.
public abstract class FormField(Func<string> label) {
  public string Label => label();

  /// Called when the user changed the value.
  public Action? Changed { get; init; }

  /// The keys that change the value.
  public abstract IReadOnlyList<Hint> Hints { get; }

  /// Changes the value by a key; false when the key does not change it.
  public abstract bool OnKey(ConsoleKeyInfo key);

  /// Draws the value in width columns from (x, y).
  public abstract void DrawValue(ScreenBuffer screen, int x, int y, int width, bool focused);
}

/// A text that the user types.
public sealed class TextRow(Func<string> label, string value = "", Func<string>? placeholder = null)
    : FormField(label) {
  public TextField Field { get; } = new(value);

  /// What the user typed, without the spaces around it.
  public string Value => Field.Value.Trim();

  public override IReadOnlyList<Hint> Hints => [];

  public override bool OnKey(ConsoleKeyInfo key) {
    string before = Field.Value;
    bool handled = Field.OnKey(key);
    if (Field.Value != before) {
      Changed?.Invoke();
    }

    return handled;
  }

  public override void DrawValue(ScreenBuffer screen, int x, int y, int width, bool focused) =>
      Field.Draw(screen, x, y, width, focused, placeholder?.Invoke() ?? "");
}

/// One of several options: ← and → go to the option before and after, Space to the next one,
/// round.
public sealed class ChoiceRow(Func<string> label, IReadOnlyList<Func<string>> options, int selected = 0)
    : FormField(label) {
  private int _selected = Math.Clamp(selected, 0, Math.Max(0, options.Count - 1));

  /// The index of the chosen option.
  public int Selected {
    get => _selected;
    set => _selected = Math.Clamp(value, 0, Math.Max(0, options.Count - 1));
  }

  public override IReadOnlyList<Hint> Hints => [CommonText.HintChange];

  public override bool OnKey(ConsoleKeyInfo key) {
    int step = key.Key switch {
      ConsoleKey.LeftArrow => options.Count - 1,
      ConsoleKey.RightArrow or ConsoleKey.Spacebar => 1,
      _ => 0,
    };
    if (step == 0 || options.Count == 0) {
      return false;
    }

    _selected = (_selected + step) % options.Count;
    Changed?.Invoke();
    return true;
  }

  public override void DrawValue(ScreenBuffer screen, int x, int y, int width, bool focused) {
    string text = options.Count == 0 ? "" : options[_selected]();
    screen.WriteFit(x, y, width, $"‹ {text} ›", focused ? Theme.Accent : default);
  }
}

/// Something that is on or off: Space switches it.
public sealed class ToggleRow(Func<string> label, bool on = false) : FormField(label) {
  public bool On { get; set; } = on;

  public override IReadOnlyList<Hint> Hints => [CommonText.HintToggle];

  public override bool OnKey(ConsoleKeyInfo key) {
    if (key.Key != ConsoleKey.Spacebar) {
      return false;
    }

    On = !On;
    Changed?.Invoke();
    return true;
  }

  public override void DrawValue(ScreenBuffer screen, int x, int y, int width, bool focused) =>
      screen.WriteFit(x, y, width, On ? "[x]" : "[ ]", On ? Theme.Good : default);
}

/// Rows of fields, one of them focused: ↑ and ↓, or Shift+Tab and Tab, go from field to field;
/// the other keys change the focused field. Enter is left to the owner.
public sealed class Form(params FormField[] fields) {
  private int _top;

  public IReadOnlyList<FormField> Fields => fields;

  /// The index of the focused field.
  public int Focus { get; set; }

  /// Whether each field takes two rows, its label above its value, as in a dialog; otherwise
  /// the labels are a column at the left of the values.
  public bool Stacked { get; init; }

  /// The rows that the form takes.
  public int Height => fields.Length * (Stacked ? 3 : 1) - (Stacked ? 1 : 0);

  public IReadOnlyList<Hint> Hints =>
      fields.Length == 0 ? []
          : fields.Length == 1 ? fields[0].Hints
          : [CommonText.HintMove, .. fields[Math.Clamp(Focus, 0, fields.Length - 1)].Hints];

  /// Moves the focus, or changes the focused field, by a key; false when the key does neither:
  /// ↑ on the first field and ↓ on the last one too.
  public bool OnKey(ConsoleKeyInfo key) {
    if (fields.Length == 0) {
      return false;
    }

    Focus = Math.Clamp(Focus, 0, fields.Length - 1);
    bool back = key.Key == ConsoleKey.UpArrow || Ui.IsBackTab(key);
    if (back || key.Key is ConsoleKey.DownArrow or ConsoleKey.Tab) {
      int next = Focus + (back ? -1 : 1);
      if (next < 0 || next >= fields.Length) {
        return false;
      }

      Focus = next;
      return true;
    }

    return fields[Focus].OnKey(key);
  }

  /// Draws the fields that fit rect, the focused one among them.
  public void Draw(ScreenBuffer screen, Rect rect, bool focused) {
    if (fields.Length == 0 || rect.Width < 4) {
      return;
    }

    Focus = Math.Clamp(Focus, 0, fields.Length - 1);
    int each = Stacked ? 3 : 1;
    int shown = Math.Max(1, (rect.Height + (Stacked ? 1 : 0)) / each);
    _top = Ui.ScrollTo(Focus, Math.Min(_top, Math.Max(0, fields.Length - shown)), shown);
    int labels = Math.Min(
        fields.Max(f => TextWidth.Of(f.Label)) + 2, Math.Max(0, rect.Width / 2));
    for (int i = 0; i < shown && _top + i < fields.Length; i++) {
      FormField field = fields[_top + i];
      bool current = focused && _top + i == Focus;
      int y = rect.Y + (i * each);
      if (Stacked) {
        screen.WriteFit(rect.X, y, rect.Width, field.Label, current ? Theme.Strong : Theme.Muted);
        screen.Write(rect.X, y + 1, current ? "> " : "  ", Theme.Accent);
        field.DrawValue(screen, rect.X + 2, y + 1, rect.Width - 2, current);
      } else {
        screen.Write(rect.X, y, current ? "> " : "  ", Theme.Accent);
        screen.WriteFit(
            rect.X + 2, y, labels - 1, field.Label, current ? Theme.Strong : default);
        field.DrawValue(
            screen, rect.X + 2 + labels, y, rect.Width - 2 - labels, current);
      }
    }

    Ui.ScrollMarks(screen, rect, _top > 0, _top + shown < fields.Length);
  }
}
