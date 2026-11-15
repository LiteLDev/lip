namespace Lip.Cli.Tui.Pages.GetMods;

/// The Get mods page: the catalogue (protocol/lip-tui.md, "Catalogue"). Not built yet.
public sealed class GetModsPage(AppContext context) : Page(context) {
  public override string Title => GetModsText.Title;

  public override void Draw(ScreenBuffer screen, Rect area, bool focused) =>
      screen.WriteFit(area.X, area.Y, area.Width, GetModsText.Placeholder, Theme.Muted);
}
