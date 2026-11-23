using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Widgets;

namespace Lip.Cli.Tui.Pages.Tools;

public sealed partial class ManifestEditor {
  /// How a value stands: the mark at the right of its row.
  internal enum Mark {
    None,
    Valid,
    Invalid,
    Checking,
    Unknown,
  }

  /// The mark of a row and what is said below it: why it is invalid, or what was found.
  /// Remote: what a check online found, which does not keep the file from being saved.
  internal readonly record struct Status(Mark Mark, string? Message = null, bool Remote = false) {
    public static readonly Status None = new(Mark.None);
    public static readonly Status Valid = new(Mark.Valid);
  }

  /// A row of the form: a heading, a value with its label, an action, or the buttons. Key
  /// names it among the rows that the form is built of again, so that the focus stays on it.
  private abstract class Row(string key, Func<string> label) {
    public string Key { get; } = key;
    public string Label => label();

    /// How deep the row is: its label is indented by two columns for each level.
    public int Indent { get; init; }

    /// One sentence about the value, shown below the row while it has the focus.
    public Func<string>? Help { get; init; }

    /// What Enter does on the row; null when Enter goes on to the next row.
    public Action? Enter { get; init; }

    public virtual bool Focusable => true;

    /// The keys that the row takes beyond ↑↓, Tab and Enter.
    public virtual IReadOnlyList<Hint> Hints => [];

    public virtual Status Status() => ManifestEditor.Status.None;

    /// Changes the value by a key; false when the key does not change it.
    public virtual bool OnKey(ConsoleKeyInfo key) => false;

    public abstract void DrawValue(ScreenBuffer screen, int x, int y, int width, bool focused);
  }

  /// A heading of a section: a line with its title.
  private sealed class HeadingRow(string key, Func<string> label) : Row(key, label) {
    public override bool Focusable => false;

    public override void DrawValue(ScreenBuffer screen, int x, int y, int width, bool focused) {
    }
  }

  /// A text that the user types: set gets every change of it, check says how it stands.
  private sealed class FieldRow : Row {
    private readonly Action<string> _set;
    private readonly Func<Status> _check;
    private readonly Func<string>? _placeholder;

    public FieldRow(
        string key, Func<string> label, string value, Action<string> set, Func<Status> check,
        Func<string>? placeholder = null) : base(key, label) {
      Field = new TextField(value);
      _set = set;
      _check = check;
      _placeholder = placeholder;
    }

    public TextField Field { get; }

    public override Status Status() => _check();

    public override bool OnKey(ConsoleKeyInfo key) {
      string before = Field.Value;
      bool handled = Field.OnKey(key);
      if (Field.Value != before) {
        _set(Field.Value);
      }

      return handled;
    }

    public override void DrawValue(ScreenBuffer screen, int x, int y, int width, bool focused) =>
        Field.Draw(screen, x, y, width, focused, _placeholder?.Invoke() ?? "");
  }

  /// One of several values: ← and → choose another, as Space does.
  private sealed class ChoiceFieldRow(
      string key, Func<string> label, IReadOnlyList<string> options, Func<string> get,
      Action<string> set, Func<Status> check) : Row(key, label) {
    public override IReadOnlyList<Hint> Hints => [CommonText.HintChange];

    public override Status Status() => check();

    public override bool OnKey(ConsoleKeyInfo key) {
      int step = key.Key switch {
        ConsoleKey.LeftArrow => options.Count - 1,
        ConsoleKey.RightArrow or ConsoleKey.Spacebar => 1,
        _ => 0,
      };
      if (step == 0) {
        return false;
      }

      // A value that is none of the options, such as a template, goes to the first one.
      int at = options.ToList().IndexOf(get());
      set(options[at < 0 ? 0 : (at + step) % options.Count]);
      return true;
    }

    public override void DrawValue(ScreenBuffer screen, int x, int y, int width, bool focused) =>
        screen.WriteFit(x, y, width, $"‹ {get()} ›", focused ? Theme.Accent : default);
  }

  /// A row that Enter acts on, showing what it does.
  private sealed class ActionRow(string key, Func<string> label, Func<string> value, Func<Status>? check = null)
      : Row(key, label) {
    public override Status Status() => check?.Invoke() ?? ManifestEditor.Status.None;

    public override void DrawValue(ScreenBuffer screen, int x, int y, int width, bool focused) =>
        screen.WriteFit(x, y, width, value(), focused ? Theme.Accent : Theme.Muted);
  }

  /// The buttons at the bottom of the form.
  private sealed class ButtonsRow(ButtonRow buttons) : Row("buttons", () => "") {
    public ButtonRow Buttons => buttons;

    public override IReadOnlyList<Hint> Hints => buttons.Hints;

    public override bool OnKey(ConsoleKeyInfo key) =>
        key.Key is not (ConsoleKey.Enter or ConsoleKey.Spacebar) && buttons.OnKey(key);

    public override void DrawValue(ScreenBuffer screen, int x, int y, int width, bool focused) =>
        buttons.Draw(screen, x, y, focused, x + width);
  }

  /// The mark of a status, and its style.
  private static (string Text, Style Style) MarkOf(Mark mark) => mark switch {
    Mark.Valid => ("✓", Theme.Good),
    Mark.Invalid => ("✗", Theme.Bad),
    Mark.Checking => ("⋯", Theme.Accent),
    Mark.Unknown => ("?", Theme.Warning),
    _ => ("", default),
  };
}
