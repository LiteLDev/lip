using System.IO.Abstractions;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Widgets;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Compat;
using Lip.Core.PublicApi;

namespace Lip.Cli.Tui.Pages.Tools;

/// The Tools page: the commands for developing packages (protocol/lip-pkg.md), each a row that
/// Enter carries out, with one sentence about the selected one, and whether the selected
/// workspace has a lip.toml and a tooth.json. An action that those files make fail (lip pkg
/// init when lip.toml exists; lip pkg migrate when it exists or there is no tooth.json) is
/// shown as disabled, with the reason below its sentence, until the files change.
public sealed class ToolsPage : Page {
  // How many ticks pass between two looks at the files of the workspace: two seconds.
  private const int RefreshTicks = 8;

  // The archive that Pack ZIP offers to write, and what its name ends with.
  private const string PackFileName = "package.zip";
  private const string PackExtension = ".zip";

  // The columns that a row of the list of the installed packages may take, so that it fits the
  // dialog on the narrowest terminal, and the fewest that the identity of a package gets.
  private const int RowWidth = 50;
  private const int MinNameWidth = 16;

  private readonly ToolAction[] _actions;
  private readonly ListView<ToolAction> _list;
  private FileStatus? _status;
  private int _ticks;

  // Counts the reads of the files, so that only the result of the last one is shown.
  private int _reads;

  public ToolsPage(AppContext context) : base(context) {
    _actions = [
      new ToolAction(() => ToolsText.Init, () => ToolsText.InitAbout, Init, InitReason),
      new ToolAction(
          () => ToolsText.Migrate, () => ToolsText.MigrateAbout, Migrate, MigrateReason),
      new ToolAction(() => ToolsText.Pack, () => ToolsText.PackAbout, Pack, () => null),
      new ToolAction(() => ToolsText.View, () => ToolsText.ViewAbout, () => View(0), () => null),
    ];
    _list = new ListView<ToolAction>(action => new StyledLine(
        action.Label(), action.Reason() is null ? default : Theme.Muted)) {
      Wrap = true,
      Items = _actions,
    };
  }

  public override string Title => ToolsText.Title;

  public override IReadOnlyList<Hint> Hints =>
      _list.Current.Reason() is null ? [.. _list.Hints, CommonText.HintSelect] : _list.Hints;

  public override void OnShown() => LoadStatus();

  public override void OnWorkspaceChanged() {
    _status = null;
    if (Context.CurrentPage == PageId.Tools) {
      LoadStatus();
    }
  }

  public override void OnTick() {
    // The files may change outside the interface, or by a command that failed half way.
    if (Context.CurrentPage == PageId.Tools && Context.Idle && ++_ticks >= RefreshTicks) {
      LoadStatus();
    }
  }

  public override void Draw(ScreenBuffer screen, Rect area, bool focused) {
    int y = area.Y;
    _list.Draw(
        screen, new Rect(area.X, y, area.Width, Math.Min(_actions.Length, area.Height)), focused);
    y += _actions.Length;
    if (y < area.Bottom) {
      screen.Write(area.X, y++, new string('─', area.Width), Theme.Border);
    }

    foreach (string line in TextWidth.Wrap(_list.Current.About(), area.Width)) {
      if (y < area.Bottom) {
        screen.Write(area.X, y++, line, default, area.Right);
      }
    }

    if (_list.Current.Reason() is { } reason) {
      foreach (string line in TextWidth.Wrap(reason, area.Width)) {
        if (y < area.Bottom) {
          screen.Write(area.X, y++, line, Theme.Warning, area.Right);
        }
      }
    }

    y++;
    string[] labels = [CommonText.WorkspaceSelected, PackageFormat.FileName, ToothCompat.ToothFileName];
    int labelWidth = labels.Max(TextWidth.Of) + 2;
    int valueWidth = area.Width - labelWidth;
    if (valueWidth < 1) {
      return;
    }

    for (int i = 0; i < labels.Length && y < area.Bottom; i++, y++) {
      screen.WriteFit(area.X, y, labelWidth, labels[i], Theme.Muted);
      int x = area.X + labelWidth;
      if (i == 0) {
        screen.WriteFit(x, y, valueWidth, Context.Workspace);
      } else if (_status is { } status) {
        bool found = i == 1 ? status.LipToml : status.ToothJson;
        screen.WriteFit(
            x, y, valueWidth, found ? ToolsText.Found : ToolsText.Missing,
            found ? Theme.Good : Theme.Muted);
      } else {
        screen.WriteFit(x, y, valueWidth, "…", Theme.Muted);
      }
    }
  }

  public override bool OnKey(ConsoleKeyInfo key) {
    if (key.Key == ConsoleKey.Enter) {
      // A disabled action is not carried out: its reason is on the page. The files are looked
      // at again, should they have changed a moment ago.
      if (_list.Current.Reason() is null) {
        _list.Current.Run();
      } else {
        LoadStatus();
      }

      return true;
    }

    return _list.OnKey(key);
  }

  /// Reads, in the background, which of the two files the selected workspace has.
  private void LoadStatus() {
    _ticks = 0;
    int read = ++_reads;
    IFileSystem fs = Context.FileSystem;
    string workspace = Context.Workspace;
    Context.Background(
        () => Task.FromResult(new FileStatus(
            fs.File.Exists(fs.Path.Join(workspace, PackageFormat.FileName)),
            fs.File.Exists(fs.Path.Join(workspace, ToothCompat.ToothFileName)))),
        status => {
          if (read == _reads) {
            _status = status;
          }
        },
        _ => {
          if (read == _reads) {
            _status = null;
          }
        });
  }

  /// Why lip pkg init fails in the selected workspace, or null: it writes no lip.toml where
  /// one exists (protocol/lip-pkg.md). Null while the files are not read yet.
  private string? InitReason() =>
      _status is { LipToml: true } ? ToolsText.Exists(PackageFormat.FileName) : null;

  /// Why lip pkg migrate fails in the selected workspace, or null: it needs a tooth.json and
  /// no lip.toml (protocol/lip-compatibility.md, "lip pkg migrate").
  private string? MigrateReason() =>
      _status switch {
        { LipToml: true } => ToolsText.Exists(PackageFormat.FileName),
        { ToothJson: false } => ToolsText.NoFile(ToothCompat.ToothFileName),
        _ => null,
      };

  /// lip pkg init: the command asks for the package id through the interface.
  private void Init() {
    LipClient client = Context.Client;
    Context.RunOperation(ToolsText.Init, _ => client.Init(), done => {
      LoadStatus();
      Context.ShowResult(ToolsText.Init, done, ToolsText.Created(PackageFormat.FileName));
    });
  }

  /// lip pkg migrate: the command asks for what the tooth.json lacks through the interface.
  private void Migrate() {
    LipClient client = Context.Client;
    Context.RunOperation(ToolsText.Migrate, _ => client.Migrate(), done => {
      LoadStatus();
      Context.ShowResult(ToolsText.Migrate, done, ToolsText.Migrated);
    });
  }

  /// lip pkg pack: asks for the folder to pack, then for the archive to write, each with a
  /// file browser that starts in the selected workspace, which is the working directory of the
  /// commands of the interface (protocol/lip-tui.md, "Commands"). Esc in the second step goes
  /// back to the first.
  private void Pack() => AskPackFolder(Context.Workspace, Context.Workspace);

  /// The first step: the folder to pack, with the folder at start shown first.
  private void AskPackFolder(string workspace, string start) =>
      Context.Show(PathPicker.Folder(
          ToolsText.PackFolder, start,
          folder => AskPackOutput(workspace, folder, workspace, PackFileName)));

  /// The second step: the archive, in the folder at start unless another is chosen. An
  /// archive that exists is replaced only when the user says so.
  private void AskPackOutput(string workspace, string folder, string start, string fileName) =>
      Context.Show(PathPicker.SaveFile(
          ToolsText.PackOutput, start, fileName, PackExtension,
          output => {
            // As an operation, whose dialog takes the keys while the file system is read.
            IFileSystem fs = Context.FileSystem;
            Context.RunOperation(
                ToolsText.PackTitle,
                _ => Task.FromResult(fs.File.Exists(output)),
                exists => {
                  if (exists) {
                    AskReplace(workspace, folder, output);
                  } else {
                    WritePack(folder, output);
                  }
                });
          },
          cancel: () => AskPackFolder(workspace, folder)));

  /// Asks whether to replace the archive; the answer no leads back to where it was chosen.
  private void AskReplace(string workspace, string folder, string output) {
    IPath path = Context.FileSystem.Path;
    string name = path.GetFileName(output);
    Context.Show(new ConfirmDialog(
        ToolsText.PackTitle,
        [new StyledLine(ToolsText.ReplaceQuestion(name))],
        [ToolsText.Replace, CommonText.Cancel],
        choice => {
          if (choice == 0) {
            WritePack(folder, output);
          } else {
            AskPackOutput(workspace, folder, path.GetDirectoryName(output) ?? workspace, name);
          }
        }) {
      // Enter alone replaces nothing.
      Selected = 1,
    });
  }

  /// Writes the archive. lip pkg pack leaves the archive itself out of it, so one that is
  /// written into the folder that is packed does not hold itself.
  private void WritePack(string folder, string output) {
    IFileSystem fs = Context.FileSystem;
    Context.RunOperation(
        ToolsText.PackTitle,
        _ => Packer.WriteArchive(fs, folder, output),
        done => {
          LoadStatus();
          Context.ShowResult(ToolsText.PackTitle, done, ToolsText.Packed(output));
        });
  }

  /// lip pkg view: asks what to show, a package installed in the workspace, a package
  /// argument that is typed, or a local archive or folder, with the source at selected
  /// highlighted. Esc in what a source asks leads back here.
  private void View(int selected) =>
      Context.Show(new ChoiceDialog(
          ToolsText.ViewTitle,
          [
            new ChoiceItem(ToolsText.ViewInstalled, ToolsText.ViewInstalledAbout),
            new ChoiceItem(ToolsText.ViewTyped, ToolsText.ViewTypedAbout),
            new ChoiceItem(ToolsText.ViewBrowse, ToolsText.ViewBrowseAbout),
          ],
          source => {
            switch (source) {
              case 0:
                ViewInstalled();
                break;
              case 1:
                ViewTyped();
                break;
              default:
                ViewBrowsed();
                break;
            }
          },
          selected: selected));

  /// Lists the packages installed in the selected workspace, as the Mods page orders them:
  /// the mods that the user installed, their dependencies, then the game and the loader.
  /// Enter shows the chosen one by the source that the state file records, since only a
  /// package of the version tags of a repository is named by its identity and version: a
  /// repository at the version or the commit that is installed, a URL or a local path as it
  /// is now. An identity that is too long for its column is cut in its middle, and the line
  /// below the list has it in full.
  private void ViewInstalled() {
    LipClient client = Context.Client;
    Context.RunOperation(
        ToolsText.ViewInstalledTitle,
        _ => client.ListPackages(),
        installed => {
          IReadOnlyList<InstalledPackage> packages = InstalledPackage.Listed(installed);
          if (packages.Count == 0) {
            Context.Show(new ConfirmDialog(
                ToolsText.ViewInstalledTitle, [new StyledLine(ToolsText.NothingInstalled)],
                [CommonText.Close], _ => View(0)));
            return;
          }

          int nameWidth = Math.Min(
              packages.Max(p => TextWidth.Of(p.Identity)),
              Math.Max(
                  MinNameWidth, RowWidth - 2 - packages.Max(p => TextWidth.Of(p.Version))));
          Context.Show(new ChoiceDialog(
              ToolsText.ViewInstalledTitle,
              [
                .. packages.Select(p => new ChoiceItem(
                    new StyledLine(
                        PackageCells.PadMiddle(p.Identity, nameWidth) + "  " + p.Version,
                        p.Explicit ? default : Theme.Muted),
                    Described(p))),
              ],
              chosen => Show(packages[chosen].ViewArgument, Shown(packages[chosen])),
              cancel: () => View(0)));
        });
  }

  /// Asks for a package argument.
  private void ViewTyped() =>
      Context.Show(new PromptDialog(
          ToolsText.ViewTitle,
          [new PromptField(ToolsText.Package, "", "github.com/owner/repo@1.0.0")],
          values => Show(values[0], values[0]),
          cancel: () => View(1),
          validate: values => values[0].Length == 0 ? ToolsText.PackageRequired : null));

  /// Asks for a local archive, or the folder of a package, with a file browser.
  private void ViewBrowsed() {
    IPath path = Context.FileSystem.Path;
    Context.Show(PathPicker.OpenFile(
        ToolsText.ViewLocal, Context.Workspace, [PackExtension],
        chosen => {
          string name = path.GetFileName(chosen);
          Show(chosen, name.Length == 0 ? chosen : name);
        },
        cancel: () => View(2),
        orFolder: true));
  }

  /// Shows the result of lip pkg view for a package argument, a JSON text, as the command
  /// gives it, under a title that names the package. The command reads the network and takes
  /// no cancellation token: Esc stops waiting for it.
  private void Show(string package, string title) {
    LipClient client = Context.Client;
    Context.RunOperation(
        ToolsText.ViewTitle,
        token => client.View(package).WaitAsync(token),
        json => Context.Show(new ViewerDialog(title, json.TrimEnd())),
        cancellable: true);
  }

  /// What an installed package is called: its identity, with the variant after '#', then its
  /// version after '@'.
  private static string Named(InstalledPackage package) =>
      $"{package.Identity}@{package.Version}";

  /// The title of what is shown of an installed package: what it is called when it is of a
  /// repository, whose installed version or commit is shown; otherwise the URL or the local
  /// path, since what that holds now is shown.
  private static string Shown(InstalledPackage package) =>
      package.Repository is null ? package.SourceNote ?? Named(package) : Named(package);

  /// The line about an installed package below the list: what it is called, in full, and
  /// where it is from when that is not the version tags of its repository.
  private static string Described(InstalledPackage package) =>
      package.SourceNote is { } source ? $"{Named(package)} · {source}" : Named(package);

  /// An action of the page: its label and one sentence about it, in the language of the
  /// interface when they are read, what Enter does, and why it cannot be done now, or null.
  private sealed record ToolAction(
      Func<string> Label, Func<string> About, Action Run, Func<string?> Reason);

  /// Whether the selected workspace has a lip.toml and a tooth.json.
  private readonly record struct FileStatus(bool LipToml, bool ToothJson);
}
