using Lip.Cli.Tui;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Pages.Workspaces;

namespace Lip.Cli.Tests.Tui;

/// The Workspaces page of the interface, driven by the keys of a fake terminal.
public sealed class WorkspacesPageTests {
  [Theory]
  [InlineData(Language.En, "Workspaces")]
  [InlineData(Language.ZhHans, "工作区")]
  [InlineData(Language.ZhHant, "工作區")]
  public async Task Page_IsReachedFromTheSidebar_AndShowsItsTitle(Language language, string title) {
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions { Language = language });

    await tui.GoTo(PageId.Workspaces);

    Assert.Equal(title, tui.Text(() => WorkspacesText.Title));
    Assert.Contains($"─ {title} ─", tui.Screen);
  }
}
