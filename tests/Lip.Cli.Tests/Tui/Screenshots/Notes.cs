namespace Lip.Cli.Tests.Tui.Screenshots;

/// What an annotation points at: cells of the frame, found when the screenshot is drawn. A
/// target that the frame does not have fails the screenshot.
public abstract record Target {
  /// The cells of the target in the frame.
  public abstract CellRect Resolve(Frame frame);

  /// The target with columns added at its left and its right, and rows above and below.
  public Target Pad(int columns, int rows = 0) => new PaddedTarget(this, columns, rows);

  /// The smallest rectangle that holds this target and the other.
  public Target To(Target other) => new SpanTarget(this, other);

  /// The message of a target that the frame does not have.
  protected static InvalidOperationException Missing(string what, Frame frame) =>
      new($"The frame does not show {what}:{Environment.NewLine}{frame.Text}");
}

/// The targets of annotations.
public static class At {
  /// The cells of a text of one row: its nth place, from the top and then from the left.
  public static Target Text(string text, int nth = 1) => new TextTarget(text, nth);

  /// The row that shows the text, from the border at its left to the border at its right:
  /// the row of a list or of a menu.
  public static Target Row(string text, int nth = 1) => new RowTarget(text, nth);

  /// The button with the label: "[ label ]".
  public static Target Button(string label, int nth = 1) => new TextTarget($"[ {label} ]", nth);

  /// The box with the title in its top edge, borders included: the sidebar, the content area,
  /// a panel of a page or a dialog.
  public static Target Panel(string title, int nth = 1) => new PanelTarget(title, nth);

  /// Cells by their position: the column and the row of the first, from 0, and how many.
  public static Target Cells(int x, int y, int width = 1, int height = 1) =>
      new CellsTarget(new CellRect(x, y, width, height));

  /// Whole rows of the frame.
  public static Target Rows(int y, int height = 1) => new RowsTarget(y, height);
}

internal sealed record TextTarget(string Text, int Nth) : Target {
  public override CellRect Resolve(Frame frame) =>
      frame.Find(Text, Nth) ?? throw Missing($"'{Text}' {Nth} times", frame);
}

internal sealed record RowTarget(string Text, int Nth) : Target {
  public override CellRect Resolve(Frame frame) {
    CellRect found = frame.Find(Text, Nth) ?? throw Missing($"'{Text}' {Nth} times", frame);
    int left = found.X;
    while (left > 0 && !PanelTarget.IsVertical(frame[left - 1, found.Y].Text)) {
      left--;
    }

    int right = found.Right;
    while (right < frame.Width && !PanelTarget.IsVertical(frame[right, found.Y].Text)) {
      right++;
    }

    return new CellRect(left, found.Y, right - left, 1);
  }
}

internal sealed record PanelTarget(string Title, int Nth) : Target {
  public static bool IsVertical(string cell) => cell is "│" or "├" or "┤" or "┼";

  public override CellRect Resolve(Frame frame) {
    int seen = 0;
    for (int nth = 1; frame.Find($" {Title} ", nth) is { } found; nth++) {
      // The title is in the top edge of its box: the edge goes on at its left to the corner.
      int left = found.X - 1;
      while (left > 0 && frame[left, found.Y].Text == "─") {
        left--;
      }

      if (left < 0 || frame[left, found.Y].Text != "┌") {
        continue;
      }

      int right = found.Right;
      while (right < frame.Width - 1 && frame[right, found.Y].Text != "┐") {
        right++;
      }

      int bottom = found.Y + 1;
      while (bottom < frame.Height - 1 && frame[left, bottom].Text != "└") {
        bottom++;
      }

      if (frame[right, found.Y].Text == "┐" && frame[left, bottom].Text == "└"
          && ++seen == Nth) {
        return new CellRect(left, found.Y, right - left + 1, bottom - found.Y + 1);
      }
    }

    throw Missing($"a box titled '{Title}' {Nth} times", frame);
  }
}

internal sealed record CellsTarget(CellRect Rect) : Target {
  public override CellRect Resolve(Frame frame) =>
      Rect.X >= 0 && Rect.Y >= 0 && Rect.Width > 0 && Rect.Height > 0
          && Rect.Right <= frame.Width && Rect.Bottom <= frame.Height
          ? Rect
          : throw new InvalidOperationException(
              $"The cells {Rect} are not in a frame of {frame.Width}×{frame.Height}.");
}

internal sealed record RowsTarget(int Y, int Height) : Target {
  public override CellRect Resolve(Frame frame) =>
      new CellsTarget(new CellRect(0, Y, frame.Width, Height)).Resolve(frame);
}

internal sealed record PaddedTarget(Target Inner, int Columns, int Rows) : Target {
  public override CellRect Resolve(Frame frame) {
    CellRect rect = Inner.Resolve(frame);
    int x = Math.Max(0, rect.X - Columns);
    int y = Math.Max(0, rect.Y - Rows);
    return new CellRect(
        x, y, Math.Min(frame.Width, rect.Right + Columns) - x,
        Math.Min(frame.Height, rect.Bottom + Rows) - y);
  }
}

internal sealed record SpanTarget(Target First, Target Second) : Target {
  public override CellRect Resolve(Frame frame) =>
      First.Resolve(frame).Union(Second.Resolve(frame));
}

/// A side of a target or of the window.
public enum Side {
  Left,
  Right,
  Top,
  Bottom,
}

/// An annotation drawn above a frame. Its texts are in the language of the screenshot: the
/// scenario gives them.
public abstract record Note {
  /// A rounded box around the target.
  public static Note Box(Target target) => new BoxNote(target, Round: false);

  /// An ellipse around the target.
  public static Note Ellipse(Target target) => new BoxNote(target, Round: true);

  /// A label in the margin at a side of the window, with an arrow to the target.
  public static Note Arrow(Target target, string label, Side side = Side.Right) =>
      new ArrowNote(target, label, side);

  /// A circle with a number at a side of the target. With a text, the legend below the
  /// window says what the number stands for.
  public static Note Callout(
      int number, Target target, string? text = null, Side side = Side.Left) =>
      new CalloutNote(number, target, text, side);

  /// The keys that the user presses next, as a row of badges below the window.
  public static Note Keys(params string[] keys) => new KeysNote(null, keys);

  /// The keys that the user presses next, after a caption such as "Then press".
  public static Note KeysAfter(string caption, params string[] keys) =>
      new KeysNote(caption, keys);

  /// Dims the frame but for the target. Several of them keep several targets bright.
  public static Note Focus(Target target) => new FocusNote(target);
}

internal sealed record BoxNote(Target Target, bool Round) : Note;

internal sealed record ArrowNote(Target Target, string Label, Side Side) : Note;

internal sealed record CalloutNote(int Number, Target Target, string? Text, Side Side) : Note;

internal sealed record KeysNote(string? Caption, IReadOnlyList<string> Badges) : Note;

internal sealed record FocusNote(Target Target) : Note;
