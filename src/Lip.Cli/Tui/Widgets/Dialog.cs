using Lip.Cli.Tui.I18n;

namespace Lip.Cli.Tui.Widgets;

/// A window above the page that takes every key until it is closed. AppContext.Show opens one;
/// the dialogs form a stack, and Esc closes the top one.
public abstract class Dialog {
  /// The interface that shows the dialog, from when it is shown.
  internal AppContext? Owner { get; set; }

  /// The services of the interface that shows the dialog.
  protected AppContext Context =>
      Owner ?? throw new InvalidOperationException("The dialog is not shown.");

  /// The keys that the dialog takes, for the bottom bar.
  public abstract IReadOnlyList<Hint> Hints { get; }

  public abstract void Draw(ScreenBuffer screen);

  public abstract void OnKey(ConsoleKeyInfo key);

  /// Called four times a second while the dialog is shown.
  public virtual void OnTick() {
  }

  /// Closes the dialog; the one below it, or the page, takes the keys again.
  public void Close() => Owner?.Close(this);
}

/// Shows lines, which scroll, above buttons: a question, a plan, a result, a failure. Enter
/// presses the chosen button; Esc presses the last one, which is the one that changes nothing.
public sealed class ConfirmDialog : Dialog {
  private readonly string _title;
  private readonly string[] _buttons;
  private readonly Action<int>? _chosen;
  private readonly Style? _border;
  private readonly TextViewer _viewer = new();
  private int _button;

  /// chosen gets the index of the button pressed, once the dialog is closed. fromEnd shows the
  /// end of the lines first, as for a log whose end says how it went.
  public ConfirmDialog(
      string title,
      IReadOnlyList<StyledLine> lines,
      string[] buttons,
      Action<int>? chosen = null,
      Style? border = null,
      bool fromEnd = false) {
    _title = title;
    _buttons = buttons;
    _chosen = chosen;
    _border = border;
    _viewer.Lines = lines;
    if (fromEnd) {
      _viewer.ScrollToEnd();
    }
  }

  /// The index of the chosen button.
  public int Selected {
    get => _button;
    init => _button = value;
  }

  public override IReadOnlyList<Hint> Hints => [
    .. _buttons.Length > 1 ? [CommonText.HintSwitchButtons] : (Hint[])[],
    CommonText.HintSelect,
    .. _viewer.Hints,
    _buttons.Length > 1 ? CommonText.HintCancel : CommonText.HintClose,
  ];

  public override void Draw(ScreenBuffer screen) {
    int width = Ui.DialogWidth(screen);
    int widest = Math.Max(
        _viewer.Lines.Count == 0 ? 0 : _viewer.Lines.Max(l => TextWidth.Of(l.PlainText)),
        Math.Max(_buttons.Sum(b => TextWidth.Of(b) + 5), TextWidth.Of(_title) + 4));
    width = Math.Min(width, Math.Max(36, widest + 4));
    Rect inner = Ui.DialogFrame(screen, _title, width, _viewer.RowsFor(width - 4) + 4, _border);
    _viewer.Draw(screen, inner.Shrink(bottom: 2));
    int x = inner.X;
    for (int i = 0; i < _buttons.Length; i++) {
      x = screen.Write(
          x, inner.Bottom - 1, $"[ {_buttons[i]} ]", i == _button ? Theme.Selected : default,
          inner.Right) + 1;
    }
  }

  public override void OnKey(ConsoleKeyInfo key) {
    switch (key.Key) {
      case ConsoleKey.Escape:
        Close();
        _chosen?.Invoke(_buttons.Length - 1);
        return;
      case ConsoleKey.Enter or ConsoleKey.Spacebar:
        Close();
        _chosen?.Invoke(_button);
        return;
      case ConsoleKey.LeftArrow:
        _button = Math.Max(0, _button - 1);
        return;
      case ConsoleKey.RightArrow:
        _button = Math.Min(_buttons.Length - 1, _button + 1);
        return;
      case ConsoleKey.Tab:
        int step = Ui.IsBackTab(key) ? _buttons.Length - 1 : 1;
        _button = (_button + step) % _buttons.Length;
        return;
    }

    _viewer.OnKey(key);
  }
}

/// A field of a PromptDialog: its label, the text it starts with, and a placeholder that gives
/// one example.
public sealed record PromptField(string Label, string Value = "", string Placeholder = "");

/// Asks for the values of text fields. Enter confirms, Esc cancels; with several fields, ↑, ↓,
/// Tab and Shift+Tab go from field to field. A label that is longer than the dialog is wide,
/// such as a question of a command, is wrapped.
public sealed class PromptDialog : Dialog {
  private readonly string _title;
  private readonly Action<string[]> _submit;
  private readonly Action? _cancel;
  private readonly Func<string[], string?>? _validate;
  private readonly TextRow[] _rows;
  private readonly Form _form;
  private string? _error;

  /// submit gets the values, without the spaces around them, once the dialog is closed. validate
  /// returns why the values cannot be taken, in the language of the interface, or null: the
  /// dialog shows it and stays open.
  public PromptDialog(
      string title,
      PromptField[] fields,
      Action<string[]> submit,
      Action? cancel = null,
      Func<string[], string?>? validate = null) {
    _title = title;
    _submit = submit;
    _cancel = cancel;
    _validate = validate;
    _rows = [.. fields.Select(f => new TextRow(() => f.Label, f.Value, () => f.Placeholder))];
    _form = new Form([.. _rows]) { Stacked = true };
  }

  public override IReadOnlyList<Hint> Hints =>
      _rows.Length > 1
          ? [CommonText.HintMoveFields, CommonText.HintConfirm, CommonText.HintCancel]
          : [CommonText.HintConfirm, CommonText.HintCancel];

  public override void Draw(ScreenBuffer screen) {
    int width = Ui.DialogWidth(screen, 76);
    List<string> error = _error is null ? [] : TextWidth.Wrap(_error, width - 4);
    // A label that is longer than the dialog is wide, such as a question of a command, goes
    // on in the rows below.
    Rect inner = Ui.DialogFrame(
        screen, _title, width,
        _form.HeightFor(width - 4) + 2 + (error.Count > 0 ? error.Count + 1 : 0));
    _form.Draw(screen, inner.Shrink(bottom: error.Count > 0 ? error.Count + 1 : 0), true);
    for (int i = 0; i < error.Count; i++) {
      screen.Write(inner.X, inner.Bottom - error.Count + i, error[i], Theme.Bad, inner.Right);
    }
  }

  public override void OnKey(ConsoleKeyInfo key) {
    switch (key.Key) {
      case ConsoleKey.Escape:
        Close();
        _cancel?.Invoke();
        return;
      case ConsoleKey.Enter:
        string[] values = [.. _rows.Select(r => r.Value)];
        _error = _validate?.Invoke(values);
        if (_error is null) {
          Close();
          _submit(values);
        }

        return;
    }

    if (_form.OnKey(key)) {
      _error = null;
    } else if (key.Key is ConsoleKey.Tab or ConsoleKey.DownArrow or ConsoleKey.UpArrow
        && _rows.Length > 1) {
      // Round, from the last field to the first and back.
      _form.Focus = _form.Focus == 0 ? _rows.Length - 1 : 0;
    }
  }
}

/// An option of a ChoiceDialog: its label, and one sentence about it that the details area
/// shows while it is selected.
public sealed record ChoiceItem(StyledLine Label, string? Detail = null) {
  public ChoiceItem(string label, string? detail = null) : this(new StyledLine(label), detail) {
  }
}

/// Lets the user choose one of several options: ↑ and ↓ move, Enter chooses, Esc cancels.
public sealed class ChoiceDialog : Dialog {
  private readonly string _title;
  private readonly IReadOnlyList<ChoiceItem> _items;
  private readonly Action<int> _chosen;
  private readonly Action? _cancel;
  private readonly string? _note;
  private readonly ListView<int> _list;

  /// chosen gets the index of the option, once the dialog is closed. note is one sentence
  /// that the user must know before they choose, whatever is selected: it is shown above the
  /// options.
  public ChoiceDialog(
      string title,
      IReadOnlyList<ChoiceItem> items,
      Action<int> chosen,
      Action? cancel = null,
      int selected = 0,
      string? note = null) {
    _title = title;
    _items = items;
    _chosen = chosen;
    _cancel = cancel;
    _note = note;
    _list = new ListView<int>(i => items[i].Label) {
      Wrap = true,
      Empty = () => CommonText.Empty,
      Items = [.. Enumerable.Range(0, items.Count)],
    };
    _list.Selected = selected;
  }

  public override IReadOnlyList<Hint> Hints =>
      [CommonText.HintMove, CommonText.HintSelect, .. _list.PageHints, CommonText.HintCancel];

  public override void Draw(ScreenBuffer screen) {
    int width = Ui.DialogWidth(screen, 76);
    bool details = _items.Any(i => i.Detail is not null);
    int widest = Math.Max(
        TextWidth.Of(_title) + 4,
        _items.Count == 0 ? 0 : _items.Max(i => TextWidth.Of(i.Label.PlainText)));
    if (!details) {
      width = Math.Min(
          width, Math.Max(36, Math.Max(widest, TextWidth.Of(_note ?? "")) + 4));
    }

    List<string> detail = !details || !_list.HasCurrent ? []
        : TextWidth.Wrap(_items[_list.Current].Detail ?? "", width - 4);
    List<string> note = _note is null ? [] : TextWidth.Wrap(_note, width - 4);
    int below = details ? Math.Max(2, detail.Count) + 1 : 0;
    // The note and the line below it.
    int above = note.Count == 0 ? 0 : note.Count + 1;
    Rect inner = Ui.DialogFrame(
        screen, _title, width, Math.Max(1, _items.Count) + 2 + above + below);
    // On a small screen the note is cut before the options are, and says that it was.
    above = Math.Min(above, Math.Max(0, inner.Height - below - Math.Min(_items.Count, 3)));
    for (int i = 0; i < above - 1; i++) {
      bool cut = i == above - 2 && above - 1 < note.Count;
      screen.Write(
          inner.X, inner.Y + i, cut ? TextWidth.Fit(note[i] + " …", inner.Width) : note[i],
          Theme.Warning, inner.Right);
    }

    _list.Draw(screen, inner.Shrink(top: above, bottom: below), true);
    if (details) {
      int y = inner.Bottom - below;
      screen.Write(inner.X, y, new string('─', inner.Width), Theme.Border);
      for (int i = 0; i < detail.Count && y + 1 + i < inner.Bottom; i++) {
        screen.Write(inner.X, y + 1 + i, detail[i], Theme.Muted, inner.Right);
      }
    }
  }

  public override void OnKey(ConsoleKeyInfo key) {
    switch (key.Key) {
      case ConsoleKey.Escape:
        Close();
        _cancel?.Invoke();
        return;
      case ConsoleKey.Enter or ConsoleKey.Spacebar:
        if (_list.HasCurrent) {
          Close();
          _chosen(_list.Current);
        }

        return;
    }

    _list.OnKey(key);
  }
}

/// An action of a Menu: its label, what it does, and whether it can be chosen now. Reason: for
/// one that cannot, one short sentence saying why, such as CommonText.StopServerFirst; the
/// menu shows it while the action is highlighted.
public sealed record MenuItem(
    string Label, Action Run, bool Enabled = true, string? Reason = null);

/// The action menu of a row: what Enter on the row offers. ↑ and ↓ move, Enter carries the
/// action out, Esc closes the menu. An action that cannot be chosen is shown muted: ↑ and ↓
/// skip it, unless it says why, and then they stop at it, the menu shows its reason below the
/// actions, and Enter does nothing.
public sealed class Menu : Dialog {
  private readonly string _title;
  private readonly IReadOnlyList<MenuItem> _items;
  private int _selected;

  public Menu(string title, IReadOnlyList<MenuItem> items) {
    _title = title;
    _items = items;
    int first = items.ToList().FindIndex(i => i.Enabled);
    _selected = Math.Max(0, first >= 0 ? first : items.ToList().FindIndex(Reachable));
  }

  /// The action that is highlighted, or null when none can be.
  private MenuItem? Current =>
      _selected < _items.Count && Reachable(_items[_selected]) ? _items[_selected] : null;

  /// Whether the menu has an action that says why it cannot be chosen.
  private bool HasReasons => _items.Any(i => !i.Enabled && i.Reason is not null);

  public override IReadOnlyList<Hint> Hints => [
    CommonText.HintMove,
    .. Current is { Enabled: true } ? [CommonText.HintSelect] : (Hint[])[],
    CommonText.HintClose,
  ];

  public override void Draw(ScreenBuffer screen) {
    int widest = Math.Max(
        TextWidth.Of(_title) + 4,
        _items.Count == 0 ? 0 : _items.Max(i => TextWidth.Of(i.Label)));
    if (HasReasons) {
      widest = Math.Max(
          widest, _items.Max(i => i.Enabled ? 0 : TextWidth.Of(i.Reason ?? "")));
    }

    int width = Math.Min(Ui.DialogWidth(screen, 76), Math.Max(28, widest + 4));
    // The reason of the highlighted action, below a rule: room for it is kept while any
    // action has one, so that the menu does not change its size as the user moves.
    List<string> reason = Current is { Enabled: false, Reason: { } why }
        ? TextWidth.Wrap(why, width - 4)
        : [];
    int below = HasReasons ? Math.Max(1, reason.Count) + 1 : 0;
    Rect inner = Ui.DialogFrame(screen, _title, width, Math.Max(1, _items.Count) + 2 + below);
    int rows = inner.Height - below;
    for (int i = 0; i < _items.Count && i < rows; i++) {
      screen.WriteFit(
          inner.X, inner.Y + i, inner.Width, _items[i].Label,
          _items[i].Enabled ? default : Theme.Muted);
      if (i == _selected && Current is { } current) {
        screen.Restyle(
            inner.X - 1, inner.Y + i, inner.Width + 2,
            current.Enabled ? Theme.Selected : Theme.SelectedInactive);
      }
    }

    if (below > 0) {
      int y = inner.Bottom - below;
      screen.Write(inner.X, y, new string('─', inner.Width), Theme.Border);
      for (int i = 0; i < reason.Count && y + 1 + i < inner.Bottom; i++) {
        screen.Write(inner.X, y + 1 + i, reason[i], Theme.Warning, inner.Right);
      }
    }
  }

  public override void OnKey(ConsoleKeyInfo key) {
    switch (key.Key) {
      case ConsoleKey.Escape:
        Close();
        return;
      case ConsoleKey.Enter or ConsoleKey.Spacebar:
        if (Current is { Enabled: true } current) {
          Close();
          current.Run();
        }

        return;
      case ConsoleKey.UpArrow:
        Move(-1);
        return;
      case ConsoleKey.DownArrow:
        Move(1);
        return;
    }
  }

  /// Whether ↑ and ↓ stop at the action: it can be chosen, or it says why it cannot.
  private static bool Reachable(MenuItem item) => item.Enabled || item.Reason is not null;

  /// To the action before or after that ↑ and ↓ stop at, round.
  private void Move(int step) {
    for (int i = 1; i <= _items.Count; i++) {
      int at = ((_selected + (i * step)) % _items.Count + _items.Count) % _items.Count;
      if (Reachable(_items[at])) {
        _selected = at;
        return;
      }
    }
  }
}

/// Shows text that scrolls, such as a README or a JSON text, in a large window. Esc or Enter
/// closes it.
public sealed class ViewerDialog : Dialog {
  private readonly string _title;
  private readonly TextViewer _viewer = new();

  public ViewerDialog(string title, IReadOnlyList<StyledLine> lines) {
    _title = title;
    _viewer.Lines = lines;
  }

  public ViewerDialog(string title, string text) {
    _title = title;
    _viewer.SetText(text);
  }

  public override IReadOnlyList<Hint> Hints => [.. _viewer.Hints, CommonText.HintClose];

  public override void Draw(ScreenBuffer screen) {
    Rect inner = Ui.DialogFrame(screen, _title, screen.Width - 4, screen.Height - 2);
    _viewer.Draw(screen, inner);
  }

  public override void OnKey(ConsoleKeyInfo key) {
    if (key.Key is ConsoleKey.Escape or ConsoleKey.Enter) {
      Close();
      return;
    }

    _viewer.OnKey(key);
  }
}

/// Shows the operation that runs: what it downloads, and its messages. Esc cancels an
/// operation that can be cancelled.
public sealed class ProgressDialog(Operation operation) : Dialog {
  private const string Spinner = "|/-\\";

  public override IReadOnlyList<Hint> Hints =>
      operation.Cancellable && !operation.Cancel.IsCancellationRequested
          ? [CommonText.HintCancel]
          : [];

  public override void Draw(ScreenBuffer screen) {
    List<ProgressItem> items = [.. operation.Progress.TakeLast(4)];
    Rect inner = Ui.DialogFrame(
        screen, operation.Title, Ui.DialogWidth(screen), Math.Min(screen.Height - 4, 18));
    char spin = Spinner[(int)(Environment.TickCount64 / 150 % Spinner.Length)];
    int y = inner.Y;
    foreach (ProgressItem item in items) {
      int bar = Math.Min(24, inner.Width / 3);
      int filled = item.Done ? bar : bar * item.Percent / 100;
      int x = screen.Write(
          inner.X, y, item.Done ? "✓ " : spin + " ", item.Done ? Theme.Good : Theme.Accent);
      screen.WriteFit(x, y, inner.Width - bar - 10, item.Label);
      x = inner.Right - bar - 5;
      screen.Write(x, y, new string('█', filled), Theme.Accent);
      screen.Write(x + filled, y, new string('░', bar - filled), Theme.Border);
      screen.Write(x + bar + 1, y, item.Done ? "100%" : $"{item.Percent,3}%", Theme.Muted);
      y++;
    }

    if (items.Count == 0) {
      screen.Write(inner.X, y++, $"{spin} {CommonText.Working}", Theme.Accent, inner.Right);
    }

    y++;
    int rows = inner.Bottom - y;
    if (rows < 1) {
      return;
    }

    List<StyledLine> log = [
      .. operation.Log.TakeLast(rows).SelectMany(line => TextWidth.Wrap(line, inner.Width)),
    ];
    foreach (StyledLine line in log.TakeLast(rows)) {
      screen.Write(inner.X, y++, line, inner.Right);
    }
  }

  public override void OnKey(ConsoleKeyInfo key) {
    bool cancel = key.Key == ConsoleKey.Escape || Ui.IsInterrupt(key);
    if (cancel && operation.Cancellable && !operation.Cancel.IsCancellationRequested) {
      operation.Log.Add(new StyledLine(CommonText.Cancelling, Theme.Warning));
      operation.Cancel.Cancel();
    }
  }
}
