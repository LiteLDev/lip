using System.Globalization;

namespace Lip.Cli.Tui.Pages.GetMods;

/// What the rows of the Get mods page and of its dialogs are written with.
public static class Cells {
  /// The text in exactly width columns: cut with an ellipsis when it is longer, followed by
  /// spaces when it is shorter.
  public static string Pad(string text, int width) {
    string fit = TextWidth.Fit(text, Math.Max(0, width));
    return fit + new string(' ', Math.Max(0, width - TextWidth.Of(fit)));
  }

  /// A day as 2026-11-01; nothing for no day.
  public static string Date(DateTimeOffset? time) =>
      time?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";

  /// The last part of the id of a package, without a variant.
  public static string BareName(string id) {
    string bare = id.Split('#')[0];
    return bare[(bare.LastIndexOf('/') + 1)..];
  }
}
