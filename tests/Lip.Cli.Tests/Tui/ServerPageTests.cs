using Lip.Cli.Tui;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Pages.Server;

namespace Lip.Cli.Tests.Tui;

/// The Server page of the interface, driven by the keys of a fake terminal.
public sealed class ServerPageTests {
  [Theory]
  [InlineData(Language.En, "Server")]
  [InlineData(Language.ZhHans, "服务器")]
  [InlineData(Language.ZhHant, "伺服器")]
  public async Task Page_IsReachedFromTheSidebar_AndShowsItsTitle(Language language, string title) {
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions { Language = language });

    await tui.GoTo(PageId.Server);

    Assert.Equal(title, tui.Text(() => ServerText.Title));
    Assert.Contains($"─ {title} ─", tui.Screen);
  }
}
