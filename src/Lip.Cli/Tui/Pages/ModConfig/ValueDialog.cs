using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Widgets;

namespace Lip.Cli.Tui.Pages.ModConfig;

/// Asks for the text of a value, which it gives as it was typed, the spaces around it
/// included: they are part of a string. Enter confirms, Esc cancels.
internal sealed class ValueDialog : Dialog {
  private readonly string _title;
  private readonly Action<string> _submit;
  private readonly Func<string, string?> _validate;
  private readonly TextRow _row;
  private readonly Form _form;
  private string? _error;

  /// submit gets the text once the dialog is closed. validate returns why the text cannot be
  /// taken, in the language of the interface, or null: the dialog shows it and stays open.
  public ValueDialog(
      string title,
      string label,
      string value,
      string placeholder,
      Action<string> submit,
      Func<string, string?> validate) {
    _title = title;
    _submit = submit;
    _validate = validate;
    _row = new TextRow(() => label, value, () => placeholder);
    _form = new Form(_row) { Stacked = true };
  }

  public override IReadOnlyList<Hint> Hints => [CommonText.HintConfirm, CommonText.HintCancel];

  public override void Draw(ScreenBuffer screen) {
    int width = Ui.DialogWidth(screen, 76);
    List<string> error = _error is null ? [] : TextWidth.Wrap(_error, width - 4);
    int below = error.Count > 0 ? error.Count + 1 : 0;
    Rect inner = Ui.DialogFrame(
        screen, _title, width, _form.HeightFor(width - 4) + 2 + below);
    _form.Draw(screen, inner.Shrink(bottom: below), true);
    for (int i = 0; i < error.Count; i++) {
      screen.Write(inner.X, inner.Bottom - error.Count + i, error[i], Theme.Bad, inner.Right);
    }
  }

  public override void OnKey(ConsoleKeyInfo key) {
    switch (key.Key) {
      case ConsoleKey.Escape:
        Close();
        return;
      case ConsoleKey.Enter:
        string text = _row.Field.Value;
        _error = _validate(text);
        if (_error is null) {
          Close();
          _submit(text);
        }

        return;
    }

    if (_form.OnKey(key)) {
      _error = null;
    }
  }
}
