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

  /// Whether each field has its label above its value, as in a dialog, where a label longer
  /// than the form is wide goes on in the rows below; otherwise the labels are a column at the
  /// left of the values.
  public bool Stacked { get; init; }

  /// The rows that the form takes in width columns.
  public int HeightFor(int width) =>
      !Stacked ? fields.Length
          : Math.Max(0, fields.Sum(field => LabelRows(field, width).Count + 2) - 1);

  /// The keys of the form: with several fields ↑↓, and those of the focused field. Tab moves
  /// between the fields too; the owner names it, since it goes on to what follows the form.
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
    if (Stacked) {
      DrawStacked(screen, rect, focused);
      return;
    }

    int shown = Math.Max(1, rect.Height);
    _top = Ui.ScrollTo(Focus, Math.Min(_top, Math.Max(0, fields.Length - shown)), shown);
    int labels = Math.Min(
        fields.Max(f => TextWidth.Of(f.Label)) + 2, Math.Max(0, rect.Width / 2));
    for (int i = 0; i < shown && _top + i < fields.Length; i++) {
      FormField field = fields[_top + i];
      bool current = focused && _top + i == Focus;
      int y = rect.Y + i;
      screen.Write(rect.X, y, current ? "> " : "  ", Theme.Accent);
      screen.WriteFit(rect.X + 2, y, labels - 1, field.Label, current ? Theme.Strong : default);
      field.DrawValue(screen, rect.X + 2 + labels, y, rect.Width - 2 - labels, current);
    }

    Ui.ScrollMarks(screen, rect, _top > 0, _top + shown < fields.Length);
  }

  /// The label of a field as the rows that it takes above the value in width columns.
  private static List<string> LabelRows(FormField field, int width) =>
      TextWidth.Wrap(field.Label, width);

  /// Draws the fields one below the other, each as its label, its value and an empty row, from
  /// the first field with which the focused one still fits rect.
  private void DrawStacked(ScreenBuffer screen, Rect rect, bool focused) {
    List<List<string>> labels = [.. fields.Select(field => LabelRows(field, rect.Width))];
    _top = Math.Min(_top, Focus);
    while (_top < Focus
        && Enumerable.Range(_top, Focus - _top + 1).Sum(i => labels[i].Count + 2) - 1
            > rect.Height) {
      _top++;
    }

    int y = rect.Y;
    int last = _top - 1;
    for (int i = _top; i < fields.Length; i++) {
      // The first field shown keeps the row of its value, however long its label is; the
      // fields after it are shown whole or not at all.
      int rows = Math.Min(labels[i].Count, Math.Max(0, rect.Bottom - y - 1));
      if (y + rows >= rect.Bottom || (i > _top && rows < labels[i].Count)) {
        break;
      }

      bool current = focused && i == Focus;
      for (int row = 0; row < rows; row++) {
        // A label that has more rows than there is room for ends with an ellipsis.
        bool cut = row == rows - 1 && rows < labels[i].Count;
        screen.WriteFit(
            rect.X, y++, rect.Width, labels[i][row] + (cut ? " …" : ""),
            current ? Theme.Strong : Theme.Muted);
      }

      screen.Write(rect.X, y, current ? "> " : "  ", Theme.Accent);
      fields[i].DrawValue(screen, rect.X + 2, y, rect.Width - 2, current);
      y += 2;
      last = i;
    }

    Ui.ScrollMarks(screen, rect, _top > 0, last < fields.Length - 1);
  }
}
