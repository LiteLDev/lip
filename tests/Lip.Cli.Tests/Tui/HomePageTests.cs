using Lip.Cli.Tui;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Pages.Home;

namespace Lip.Cli.Tests.Tui;

/// The Home page of the interface, driven by the keys of a fake terminal.
public sealed class HomePageTests {
  [Theory]
  [InlineData(Language.En, "Home")]
  [InlineData(Language.ZhHans, "主页")]
  [InlineData(Language.ZhHant, "首頁")]
  public async Task Page_IsReachedFromTheSidebar_AndShowsItsTitle(Language language, string title) {
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions { Language = language });

    await tui.GoTo(PageId.Home);

    Assert.Equal(title, tui.Text(() => HomeText.Title));
    Assert.Contains($"─ {title} ─", tui.Screen);
  }
}
