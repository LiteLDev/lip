namespace Lip.Cli.Tui.Pages.Settings;

/// The Settings page: the language, the configuration and lip itself. Not built yet.
public sealed class SettingsPage(AppContext context) : Page(context) {
  public override string Title => SettingsText.Title;

  public override void Draw(ScreenBuffer screen, Rect area, bool focused) =>
      screen.WriteFit(area.X, area.Y, area.Width, SettingsText.Placeholder, Theme.Muted);
}
