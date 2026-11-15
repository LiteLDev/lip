namespace Lip.Cli.Tui.Pages.Workspaces;

/// The Workspaces page: the workspace list (protocol/lip-tui.md, "Workspaces"). Not built yet.
public sealed class WorkspacesPage(AppContext context) : Page(context) {
  public override string Title => WorkspacesText.Title;

  public override void Draw(ScreenBuffer screen, Rect area, bool focused) =>
      screen.WriteFit(area.X, area.Y, area.Width, WorkspacesText.Placeholder, Theme.Muted);
}
