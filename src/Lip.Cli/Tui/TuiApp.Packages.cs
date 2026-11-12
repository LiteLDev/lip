using Lip.Core.Catalog;
using Lip.Core.Mcmod;

namespace Lip.Cli.Tui;

/// The Packages tab: lip list, install, update and uninstall.
public sealed partial class TuiApp {
  private static readonly (string, string)[] PackagesHints = [
    ("i", "install"), ("u", "update"), ("U", "update all"), ("d", "uninstall"), ("r", "reload"),
    ("Tab", "next tab"), ("q", "quit"),
  ];

  // Explicitly installed packages first, as lip list groups them.
  private List<InstalledPackage> _installed = [];
  private string? _installedError;
  private bool _installedLoaded;
  private int _packageSelected;
  private int _packageTop;

  private void LoadPackages() {
    _ = Task.Run(async () => {
      try {
        IReadOnlyList<InstalledPackage> packages = await _client.ListPackages();
        Post(() => {
          _installed = [.. packages.OrderByDescending(p => p.Explicit)];
          _installedError = null;
          _installedLoaded = true;
          _packageSelected = Math.Clamp(_packageSelected, 0, Math.Max(0, _installed.Count - 1));
        });
      }
      catch (Exception ex) {
        Post(() => {
          _installed = [];
          _installedError = ex.Message;
          _installedLoaded = true;
        });
      }
    });
  }

  /// Whether a mod of the catalogue is installed, in any variant.
  private bool IsInstalled(string id) =>
      _installed.Any(p => p.Identity.Split('#')[0].Equals(id, StringComparison.OrdinalIgnoreCase));

  private void DrawPackages(Rect body) {
    int listWidth = Math.Max(30, body.Width * 3 / 5);
    Rect list = Panel(
        _screen, new Rect(body.X, body.Y, listWidth, body.Height),
        $"Installed ({_installed.Count})", focused: true);
    Rect details = Panel(
        _screen, new Rect(body.X + listWidth, body.Y, body.Width - listWidth, body.Height),
        "Details");

    if (_installedError is { } error) {
      int y = list.Y;
      foreach (string line in TextWidth.Wrap("Could not read the workspace: " + error, list.Width)) {
        _screen.Write(list.X, y++, line, Theme.Bad, list.Right);
      }

      return;
    }

    if (_installed.Count == 0) {
      _screen.WriteFit(
          list.X, list.Y, list.Width,
          _installedLoaded ? "No package is installed in this workspace." : "Reading...",
          Theme.Muted);
      if (_installedLoaded) {
        _screen.WriteFit(
            list.X, list.Y + 2, list.Width,
            "Press i to install one, or 2 to browse the mods of levimc.org.", Theme.Muted);
      }

      return;
    }

    _screen.WriteFit(list.X, list.Y, list.Width - 12, "Package", Theme.Muted);
    _screen.Write(list.Right - 12, list.Y, "Version", Theme.Muted);
    int rows = list.Height - 1;
    _packageTop = ScrollTo(_packageSelected, _packageTop, rows);
    for (int i = 0; i < rows && _packageTop + i < _installed.Count; i++) {
      InstalledPackage package = _installed[_packageTop + i];
      int y = list.Y + 1 + i;
      _screen.WriteFit(
          list.X, y, list.Width - 13, package.Identity,
          package.Explicit ? default : Theme.Muted);
      _screen.WriteFit(list.Right - 12, y, 12, package.Version);
      if (_packageTop + i == _packageSelected) {
        _screen.Restyle(list.X - 1, y, list.Width + 2, Theme.Selected);
      }
    }

    InstalledPackage selected = _installed[_packageSelected];
    int row = details.Y;
    _screen.WriteFit(details.X, row++, details.Width, selected.Identity, Theme.Strong);
    _screen.WriteFit(details.X, row++, details.Width, "version " + selected.Version);
    _screen.WriteFit(
        details.X, row++, details.Width,
        selected.Explicit ? "explicitly installed" : "installed as a dependency", Theme.Muted);
    CatalogMod? mod = _catalog?.Mods.FirstOrDefault(m =>
        m.Id.Equals(selected.Identity.Split('#')[0], StringComparison.OrdinalIgnoreCase));
    if (mod is not null) {
      row++;
      foreach (string line in TextWidth.Wrap(mod.Description, details.Width)) {
        if (row < details.Bottom) {
          _screen.Write(details.X, row++, line, default, details.Right);
        }
      }
    }
  }

  private void PackagesKey(ConsoleKeyInfo key) {
    if (MoveSelection(key, ref _packageSelected, _installed.Count, 10)) {
      return;
    }

    InstalledPackage? selected =
        _packageSelected < _installed.Count ? _installed[_packageSelected] : null;
    switch (key.KeyChar) {
      case 'i':
        PromptInstall("");
        break;
      case 'r':
        LoadPackages();
        break;
      case 'U':
        PlanAndRun("Update every package", (dryRun, token) =>
            _client.Update([], dryRun, null, token));
        break;
      case 'u' when selected is not null:
        PlanAndRun($"Update {selected.Identity}", (dryRun, token) =>
            _client.Update([selected.Identity], dryRun, null, token));
        break;
      case 'd' when selected is not null:
        PlanAndRun($"Uninstall {selected.Identity}", (dryRun, token) =>
            _client.Uninstall([selected.Identity], dryRun, null, token));
        break;
    }
  }

  /// Asks for the package arguments and the platform of lip install, then installs.
  private void PromptInstall(string packages) {
    Push(new PromptDialog(
        this, "Install packages",
        [
          new PromptField(
              "Packages, separated by spaces: a repository, an https:// URL of a .zip file, or"
                  + " a local .zip file or directory",
              packages, "LeviLamina  github.com/owner/repo#variant@1.2.3  ./mod.zip"),
          new PromptField("Platform (--platform), empty for the one of the workspace"),
        ],
        values => {
          List<string> arguments = SplitArguments(values[0]);
          if (arguments.Count == 0) {
            return;
          }

          string? platform = values[1].Length == 0 ? null : values[1];
          PlanAndRun($"Install {string.Join(' ', arguments)}", (dryRun, token) =>
              _client.Install(arguments, dryRun, platform, token));
        }));
  }
}
