using Lip.Cli.Tui;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Pages.ModConfig;

namespace Lip.Cli.Tests.Tui;

/// The Mod config page of the interface, driven by the keys of a fake terminal.
public sealed class ModConfigPageTests {
  [Theory]
  [InlineData(Language.En, "Mod config")]
  [InlineData(Language.ZhHans, "模组配置")]
  [InlineData(Language.ZhHant, "模組設定")]
  public async Task Page_IsReachedFromTheSidebar_AndShowsItsTitle(Language language, string title) {
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions { Language = language });

    await tui.GoTo(PageId.ModConfig);

    Assert.Equal(title, tui.Text(() => ModConfigText.Title));
    Assert.Contains($"─ {title} ─", tui.Screen);
  }
}
