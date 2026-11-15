using System.Text;

namespace Lip.Cli.Tui.Widgets;

/// What the rows of installed packages are written with, on the Mods page and the Tools page.
public static class PackageCells {
  /// The text in at most width columns, with an ellipsis in its middle when it is longer: the
  /// identity of a package and a path both tell the most at their start and at their end.
  public static string FitMiddle(string text, int width) {
    if (TextWidth.Of(text) <= width) {
      return text;
    }

    if (width <= 0) {
      return "";
    }

    // The end gets the column that is left over: the name of the package is there.
    int head = (width - 1) / 2;
    int tail = width - 1 - head;
    Rune[] runes = [.. text.EnumerateRunes()];
    StringBuilder start = new();
    int used = 0;
    int first = 0;
    while (first < runes.Length && used + TextWidth.Of(runes[first]) <= head) {
      used += TextWidth.Of(runes[first]);
      start.Append(runes[first++].ToString());
    }

    used = 0;
    int last = runes.Length;
    while (last > first && used + TextWidth.Of(runes[last - 1]) <= tail) {
      used += TextWidth.Of(runes[--last]);
    }

    return start.Append('…').AppendJoin("", runes[last..]).ToString();
  }

  /// The text in exactly width columns: cut in its middle when it is longer, followed by spaces
  /// when it is shorter.
  public static string PadMiddle(string text, int width) {
    string fit = FitMiddle(text, Math.Max(0, width));
    return fit + new string(' ', Math.Max(0, width - TextWidth.Of(fit)));
  }
}
