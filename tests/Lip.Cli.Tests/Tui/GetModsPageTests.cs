using Lip.Cli.Tui;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Pages.GetMods;

namespace Lip.Cli.Tests.Tui;

/// The Get mods page of the interface, driven by the keys of a fake terminal.
public sealed class GetModsPageTests {
  [Theory]
  [InlineData(Language.En, "Get mods")]
  [InlineData(Language.ZhHans, "获取模组")]
  [InlineData(Language.ZhHant, "取得模組")]
  public async Task Page_IsReachedFromTheSidebar_AndShowsItsTitle(Language language, string title) {
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions { Language = language });

    await tui.GoTo(PageId.GetMods);

    Assert.Equal(title, tui.Text(() => GetModsText.Title));
    Assert.Contains($"─ {title} ─", tui.Screen);
  }
}
