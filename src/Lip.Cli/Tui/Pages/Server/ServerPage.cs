namespace Lip.Cli.Tui.Pages.Server;

/// The Server page: the server of the selected workspace (protocol/lip-tui.md, "Server"). Not built yet.
public sealed class ServerPage(AppContext context) : Page(context) {
  public override string Title => ServerText.Title;

  public override void Draw(ScreenBuffer screen, Rect area, bool focused) =>
      screen.WriteFit(area.X, area.Y, area.Width, ServerText.Placeholder, Theme.Muted);
}
