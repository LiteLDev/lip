namespace Lip.Cli.Tui.Widgets;

/// What the widgets and the pages draw with.
public static class Ui {
  /// A box with a title, filled; returns the rectangle inside it. The border of the box that
  /// has the focus stands out.
  public static Rect Panel(ScreenBuffer screen, Rect rect, string title, bool focused = false) {
    screen.Fill(rect, default);
    screen.Box(
        rect, title, focused ? Theme.Accent with { Bold = false } : Theme.Border,
        focused ? Theme.Accent : Theme.Strong);
    return rect.Inner;
  }

  /// The box of a dialog: of at most the given size, in the middle of the screen. Returns the
  /// rectangle inside it.
  public static Rect DialogFrame(
      ScreenBuffer screen, string title, int width, int height, Style? border = null) {
    width = Math.Clamp(width, Math.Min(20, screen.Width), Math.Max(1, screen.Width - 4));
    height = Math.Clamp(height, Math.Min(3, screen.Height), Math.Max(1, screen.Height - 2));
    Rect rect = new((screen.Width - width) / 2, (screen.Height - height) / 2, width, height);
    screen.Fill(rect, default);
    screen.Box(rect, title, border ?? Theme.Accent with { Bold = false }, border ?? Theme.Accent);
    return rect.Inner;
  }

  /// The widest a dialog is on the screen: for text that is wrapped.
  public static int DialogWidth(ScreenBuffer screen, int most = 96) =>
      Math.Max(20, Math.Min(screen.Width - 4, most));

  /// The first row of a list to show so that the selected row is among the rows shown.
  public static int ScrollTo(int selected, int top, int rows) {
    if (selected < top) {
      return selected;
    }

    return selected >= top + rows ? selected - rows + 1 : Math.Max(0, top);
  }

  /// Marks, at the right edge of rect, that rows are hidden above or below it.
  public static void ScrollMarks(ScreenBuffer screen, Rect rect, bool above, bool below) {
    if (rect.Width < 1 || rect.Height < 1) {
      return;
    }

    if (above) {
      screen.Write(rect.Right - 1, rect.Y, "▲", Theme.Muted);
    }

    if (below) {
      screen.Write(rect.Right - 1, rect.Bottom - 1, "▼", Theme.Muted);
    }
  }

  /// Whether the key is Ctrl+C.
  public static bool IsInterrupt(ConsoleKeyInfo key) =>
      key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control);

  /// Whether the key is Shift+Tab.
  public static bool IsBackTab(ConsoleKeyInfo key) =>
      key.Key == ConsoleKey.Tab && key.Modifiers.HasFlag(ConsoleModifiers.Shift);
}

/// Two panes side by side, or one above the other.
public static class Panes {
  /// Splits area into a left pane of leftWidth columns, at most all but 10 of them, and the
  /// rest.
  public static (Rect Left, Rect Right) Columns(Rect area, int leftWidth) {
    int left = Math.Clamp(leftWidth, 0, Math.Max(0, area.Width - 10));
    return (
        new Rect(area.X, area.Y, left, area.Height),
        new Rect(area.X + left, area.Y, area.Width - left, area.Height));
  }

  /// Splits area into a top pane of topHeight rows, at most all but one of them, and the rest.
  public static (Rect Top, Rect Bottom) Rows(Rect area, int topHeight) {
    int top = Math.Clamp(topHeight, 0, Math.Max(0, area.Height - 1));
    return (
        new Rect(area.X, area.Y, area.Width, top),
        new Rect(area.X, area.Y + top, area.Width, area.Height - top));
  }
}

/// Which of the areas of a page has the focus: Tab and Shift+Tab go round them, and a page
/// moves between them with the arrow keys as it sees fit.
public sealed class FocusRing(int count) {
  public int Count { get; set; } = count;

  public int Index { get; set; }

  /// Tab goes to the next area and Shift+Tab to the one before; false for any other key, and
  /// at the last or the first area, where the focus leaves the page.
  public bool OnKey(ConsoleKeyInfo key) {
    if (key.Key != ConsoleKey.Tab) {
      return false;
    }

    return Ui.IsBackTab(key) ? Previous() : Next();
  }

  public bool Next() {
    if (Index >= Count - 1) {
      return false;
    }

    Index++;
    return true;
  }

  public bool Previous() {
    if (Index <= 0) {
      return false;
    }

    Index--;
    return true;
  }
}
