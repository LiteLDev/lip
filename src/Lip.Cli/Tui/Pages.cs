using Lip.Cli.Tui.Pages.GameConfig;
using Lip.Cli.Tui.Pages.GetMods;
using Lip.Cli.Tui.Pages.Home;
using Lip.Cli.Tui.Pages.ModConfig;
using Lip.Cli.Tui.Pages.Mods;
using Lip.Cli.Tui.Pages.Server;
using Lip.Cli.Tui.Pages.Settings;
using Lip.Cli.Tui.Pages.Tools;
using Lip.Cli.Tui.Pages.Workspaces;

namespace Lip.Cli.Tui;

/// The pages of the interface.
public enum PageId {
  Home,
  Server,
  Mods,
  GetMods,
  GameConfig,
  ModConfig,
  Workspaces,
  Tools,
  Settings,
}

/// What a page asks another to start, through AppContext.Navigate.
public static class PageRequest {
  /// To the Workspaces page: start the new server wizard.
  public const string NewServer = "new-server";

  /// To the Server page: start the server of the selected workspace.
  public const string StartServer = "start-server";

  /// To the Mods page: ask for a package to install from a source.
  public const string InstallFromSource = "install-from-source";

  /// To the Settings page: ask for the GitHub proxy.
  public const string GithubProxy = "github-proxy";
}

/// An entry of the sidebar: the page it shows, an icon of one column that stands for it where
/// the sidebar is narrow, and what creates the page.
public sealed record PageEntry(PageId Id, string Icon, Func<AppContext, Page> Create);

/// The pages of the interface, in the order of the sidebar: the only list of them.
public static class PageRegistry {
  public static IReadOnlyList<PageEntry> All { get; } = [
    new(PageId.Home, "⌂", context => new HomePage(context)),
    new(PageId.Server, "►", context => new ServerPage(context)),
    new(PageId.Mods, "■", context => new ModsPage(context)),
    new(PageId.GetMods, "▼", context => new GetModsPage(context)),
    new(PageId.GameConfig, "≡", context => new GameConfigPage(context)),
    new(PageId.ModConfig, "≈", context => new ModConfigPage(context)),
    new(PageId.Workspaces, "□", context => new WorkspacesPage(context)),
    new(PageId.Tools, "†", context => new ToolsPage(context)),
    new(PageId.Settings, "*", context => new SettingsPage(context)),
  ];
}
