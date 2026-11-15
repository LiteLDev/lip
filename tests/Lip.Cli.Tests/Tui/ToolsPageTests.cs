using Lip.Cli.Tui;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Pages.Tools;

namespace Lip.Cli.Tests.Tui;

/// The Tools page of the interface, driven by the keys of a fake terminal.
public sealed class ToolsPageTests {
  [Theory]
  [InlineData(Language.En, "Tools")]
  [InlineData(Language.ZhHans, "工具")]
  [InlineData(Language.ZhHant, "工具")]
  public async Task Page_IsReachedFromTheSidebar_AndShowsItsTitle(Language language, string title) {
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions { Language = language });

    await tui.GoTo(PageId.Tools);

    Assert.Equal(title, tui.Text(() => ToolsText.Title));
    Assert.Contains($"─ {title} ─", tui.Screen);
  }
}
