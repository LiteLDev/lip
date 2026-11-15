namespace Lip.Cli.Tui.Widgets;

/// A line of text that the user edits. It takes the characters typed, ←, →, Home, End,
/// Backspace and Delete, and nothing else.
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
        Cursor = Before(Cursor);
        return true;
      case ConsoleKey.RightArrow:
        Cursor = After(Cursor);
        return true;
      case ConsoleKey.Home:
        Cursor = 0;
        return true;
      case ConsoleKey.End:
        Cursor = Value.Length;
        return true;
      case ConsoleKey.Backspace:
        if (Cursor > 0) {
          int start = Before(Cursor);
          Value = Value.Remove(start, Cursor - start);
          Cursor = start;
        }
        return true;
      case ConsoleKey.Delete:
        if (Cursor < Value.Length) {
          Value = Value.Remove(Cursor, After(Cursor) - Cursor);
        }
        return true;
      case ConsoleKey.Enter or ConsoleKey.Tab or ConsoleKey.Escape:
        return false;
    }

    if (key.Modifiers.HasFlag(ConsoleModifiers.Control)
        || key.Modifiers.HasFlag(ConsoleModifiers.Alt)) {
      return false;
    }

    if (key.KeyChar >= ' ' && key.KeyChar != '\x7f') {
      Value = Value.Insert(Cursor, key.KeyChar.ToString());
      Cursor++;
      return true;
    }

    return false;
  }

  /// The index of the character before the one at index: two code units back over a
  /// surrogate pair, which is one character.
  private int Before(int index) =>
      index >= 2 && char.IsSurrogatePair(Value[index - 2], Value[index - 1])
          ? index - 2
          : Math.Max(0, index - 1);

  /// The index of the character after the one at index.
  private int After(int index) =>
      index + 1 < Value.Length && char.IsSurrogatePair(Value[index], Value[index + 1])
          ? index + 2
          : Math.Min(Value.Length, index + 1);

  /// Draws the text in width columns, the part around the cursor when it is longer, with the
  /// cursor when focused, or the placeholder when it is empty and not focused.
  public void Draw(
      ScreenBuffer screen, int x, int y, int width, bool focused, string placeholder = "") {
    if (width <= 0) {
      return;
    }

    if (Value.Length == 0) {
      screen.WriteFit(x, y, width, placeholder, Theme.Muted);
      if (focused) {
        screen.Restyle(x, y, 1, Style.Of(inverse: true));
      }

      return;
    }

    int start = 0;
    while (start < Cursor && TextWidth.Of(Value[start..Cursor]) > width - 1) {
      start = After(start);
    }

    screen.Write(x, y, Value[start..], default, x + width);
    if (focused) {
      int cursor = x + TextWidth.Of(Value[start..Cursor]);
      screen.Restyle(cursor, y, 1, Style.Of(inverse: true));
    }
  }
}
