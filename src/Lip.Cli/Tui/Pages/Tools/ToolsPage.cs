namespace Lip.Cli.Tui.Pages.Tools;

/// The Tools page: the commands for developing packages (protocol/lip-pkg.md). Not built yet.
public sealed class ToolsPage(AppContext context) : Page(context) {
  public override string Title => ToolsText.Title;

  public override void Draw(ScreenBuffer screen, Rect area, bool focused) =>
      screen.WriteFit(area.X, area.Y, area.Width, ToolsText.Placeholder, Theme.Muted);
}
