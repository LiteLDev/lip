namespace Lip.Cli.Tui.Pages.Home;

/// The Home page: what the selected workspace needs next. Not built yet.
public sealed class HomePage(AppContext context) : Page(context) {
  public override string Title => HomeText.Title;

  public override void Draw(ScreenBuffer screen, Rect area, bool focused) =>
      screen.WriteFit(area.X, area.Y, area.Width, HomeText.Placeholder, Theme.Muted);
}
