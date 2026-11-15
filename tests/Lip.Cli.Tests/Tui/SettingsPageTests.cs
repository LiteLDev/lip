using Lip.Cli.Tui;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Pages.Settings;

namespace Lip.Cli.Tests.Tui;

/// The Settings page of the interface, driven by the keys of a fake terminal.
public sealed class SettingsPageTests {
  [Theory]
  [InlineData(Language.En, "Settings")]
  [InlineData(Language.ZhHans, "设置")]
  [InlineData(Language.ZhHant, "設定")]
  public async Task Page_IsReachedFromTheSidebar_AndShowsItsTitle(Language language, string title) {
    using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions { Language = language });

    await tui.GoTo(PageId.Settings);

    Assert.Equal(title, tui.Text(() => SettingsText.Title));
    Assert.Contains($"─ {title} ─", tui.Screen);
  }
}
