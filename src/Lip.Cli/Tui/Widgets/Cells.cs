using System.Globalization;

namespace Lip.Cli.Tui.Widgets;

/// What the rows of lists and of details are written with.
public static class Cells {
  /// The text in exactly width columns: cut with an ellipsis when it is longer, followed by
  /// spaces when it is shorter.
  public static string Pad(string text, int width) {
    string fit = TextWidth.Fit(text, Math.Max(0, width));
    return fit + new string(' ', Math.Max(0, width - TextWidth.Of(fit)));
  }

  /// The text at the right of exactly width columns: cut with an ellipsis when it is longer,
  /// after spaces when it is shorter.
  public static string PadLeft(string text, int width) {
    string fit = TextWidth.Fit(text, Math.Max(0, width));
    return new string(' ', Math.Max(0, width - TextWidth.Of(fit))) + fit;
  }

  /// A day as 2026-11-01; nothing for no day.
  public static string Date(DateTimeOffset? time) =>
      time?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";

  /// The last part of the id of a package, without a variant.
  public static string BareName(string id) {
    string bare = id.Split('#')[0];
    return bare[(bare.LastIndexOf('/') + 1)..];
  }

  /// A count in at most five columns, the same in every language: 7, 999, 1.2k, 55.9k, 123k,
  /// 1.2M.
  public static string Compact(long count) {
    if (count < 1000) {
      return count.ToString(CultureInfo.InvariantCulture);
    }

    (double scaled, string unit) = count < 999_500 ? (count / 1e3, "k") : (count / 1e6, "M");
    // One decimal below a hundred, none from there on; 99.96 is 100, not 100.0.
    string number = Math.Round(scaled, 1, MidpointRounding.AwayFromZero) < 100
        ? Math.Round(scaled, 1, MidpointRounding.AwayFromZero)
            .ToString("0.#", CultureInfo.InvariantCulture)
        : Math.Round(scaled, MidpointRounding.AwayFromZero)
            .ToString("0", CultureInfo.InvariantCulture);
    return number + unit;
  }

  /// A count in full, with its thousands apart: 55,912.
  public static string Count(long count) => count.ToString("N0", CultureInfo.InvariantCulture);
}
