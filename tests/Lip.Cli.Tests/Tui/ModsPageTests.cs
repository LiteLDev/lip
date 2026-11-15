using Lip.Cli.Tui;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Pages.Mods;

namespace Lip.Cli.Tests.Tui;

/// The Mods page of the interface, driven by the keys of a fake terminal.
public sealed class ModsPageTests {
  [Theory]
  [InlineData(Language.En, "Mods")]
  [InlineData(Language.ZhHans, "模组")]
  [InlineData(Language.ZhHant, "模組")]
  public async Task Page_IsReachedFromTheSidebar_AndShowsItsTitle(Language language, string title) {
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions { Language = language });

    await tui.GoTo(PageId.Mods);

    Assert.Equal(title, tui.Text(() => ModsText.Title));
    Assert.Contains($"─ {title} ─", tui.Screen);
  }
}
