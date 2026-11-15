namespace Lip.Cli.Tui.Pages.ModConfig;

/// The Mod config page: the configuration files of the mods (protocol/lip-tui.md, "Configuration"). Not built yet.
public sealed class ModConfigPage(AppContext context) : Page(context) {
  public override string Title => ModConfigText.Title;

  public override void Draw(ScreenBuffer screen, Rect area, bool focused) =>
      screen.WriteFit(area.X, area.Y, area.Width, ModConfigText.Placeholder, Theme.Muted);
}
