namespace Lip.Cli.Tui.Pages.GameConfig;

/// The Game config page: the configuration files of the server (protocol/lip-tui.md, "Configuration"). Not built yet.
public sealed class GameConfigPage(AppContext context) : Page(context) {
  public override string Title => GameConfigText.Title;

  public override void Draw(ScreenBuffer screen, Rect area, bool focused) =>
      screen.WriteFit(area.X, area.Y, area.Width, GameConfigText.Placeholder, Theme.Muted);
}
