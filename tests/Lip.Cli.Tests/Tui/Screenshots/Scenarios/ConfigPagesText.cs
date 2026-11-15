using System.Text.RegularExpressions;
using Lip.Cli.Tui.I18n;

namespace Lip.Cli.Tests.Tui.Screenshots.Scenarios;

/// What the scenarios of the Game config, Mod config, Workspaces, Tools and Settings pages
/// share: how they find a text that the interface wraps, and what they write for a spinner.
internal static class ConfigPagesText {
  /// The start of a text that a page or a dialog wraps over several rows, which a wait and a
  /// target find in the first of them. Use it only for a text that starts as no other text
  /// on the screen does; otherwise wait for something that only the new screen has.
  public static string Head(string text) => text[..Math.Min(text.Length, 12)];

  /// The pattern of the spinner of a progress dialog, which no two runs catch at the same
  /// place: Shot.Scrub writes one character for it. Read it once the interface runs, for the
  /// text of its language.
  public static string Spinner() => $@"(?<=│ )[|/\\-](?= {Regex.Escape(CommonText.Working)})";
}
