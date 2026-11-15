using Lip.Cli.Tui;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Pages.GameConfig;

namespace Lip.Cli.Tests.Tui;

/// The Game config page of the interface, driven by the keys of a fake terminal.
public sealed class GameConfigPageTests {
  [Theory]
  [InlineData(Language.En, "Game config")]
  [InlineData(Language.ZhHans, "游戏配置")]
  [InlineData(Language.ZhHant, "遊戲設定")]
  public async Task Page_IsReachedFromTheSidebar_AndShowsItsTitle(Language language, string title) {
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions { Language = language });

    await tui.GoTo(PageId.GameConfig);

    Assert.Equal(title, tui.Text(() => GameConfigText.Title));
    Assert.Contains($"─ {title} ─", tui.Screen);
  }
}
