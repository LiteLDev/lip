namespace Lip.Cli.Tui.Pages.Mods;

/// The Mods page: the installed packages of the selected workspace. Not built yet.
public sealed class ModsPage(AppContext context) : Page(context) {
  public override string Title => ModsText.Title;

  public override void Draw(ScreenBuffer screen, Rect area, bool focused) =>
      screen.WriteFit(area.X, area.Y, area.Width, ModsText.Placeholder, Theme.Muted);
}
