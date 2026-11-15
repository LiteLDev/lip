using System.IO.Abstractions;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Widgets;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Compat;
using Lip.Core.PublicApi;

namespace Lip.Cli.Tui.Pages.Tools;

/// The Tools page: the commands for developing packages (protocol/lip-pkg.md), each a row that
/// Enter carries out, with one sentence about the selected one, and whether the selected
/// workspace has a lip.toml and a tooth.json.
public sealed class ToolsPage : Page {
  // How many ticks pass between two looks at the files of the workspace: two seconds.
  private const int RefreshTicks = 8;

  private readonly ToolAction[] _actions;
  private readonly ListView<ToolAction> _list;
  private FileStatus? _status;
  private int _ticks;

  // Counts the reads of the files, so that only the result of the last one is shown.
  private int _reads;

  public ToolsPage(AppContext context) : base(context) {
    _actions = [
      new ToolAction(() => ToolsText.Init, () => ToolsText.InitAbout, Init),
      new ToolAction(() => ToolsText.Migrate, () => ToolsText.MigrateAbout, Migrate),
      new ToolAction(() => ToolsText.Pack, () => ToolsText.PackAbout, Pack),
      new ToolAction(() => ToolsText.View, () => ToolsText.ViewAbout, View),
    ];
    _list = new ListView<ToolAction>(action => new StyledLine(action.Label())) {
      Wrap = true,
      Items = _actions,
    };
  }

  public override string Title => ToolsText.Title;

  public override IReadOnlyList<Hint> Hints => [.. _list.Hints, CommonText.HintSelect];

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
      _list.Current.Run();
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

  /// lip pkg pack: asks for the directory and the archive, and resolves both as lip pkg pack
  /// does, against the selected workspace, which is the working directory of the commands of
  /// the interface (protocol/lip-tui.md, "Commands"), and not against that of the process.
  private void Pack() =>
      Context.Show(new PromptDialog(
          ToolsText.PackTitle,
          [
            new PromptField(ToolsText.Directory, "."),
            new PromptField(ToolsText.Output, "", "package.zip"),
          ],
          values => {
            IFileSystem fs = Context.FileSystem;
            string workspace = Context.Workspace;
            string directory;
            string output;
            try {
              directory = fs.Path.GetFullPath(values[0].Length == 0 ? "." : values[0], workspace);
              output = fs.Path.GetFullPath(values[1], workspace);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException
                or IOException) {
              Context.ShowFailure(ToolsText.PackTitle, ex);
              return;
            }

            Context.RunOperation(
                ToolsText.PackTitle,
                _ => Packer.WriteArchive(fs, directory, output),
                done => {
                  LoadStatus();
                  Context.ShowResult(ToolsText.PackTitle, done, ToolsText.Packed(output));
                });
          },
          validate: values => values[1].Length == 0 ? ToolsText.OutputRequired : null));

  /// lip pkg view: asks for a package argument, and shows the result, a JSON text, as the
  /// command gives it. The command reads the network and takes no cancellation token: Esc
  /// stops waiting for it.
  private void View() =>
      Context.Show(new PromptDialog(
          ToolsText.ViewTitle,
          [new PromptField(ToolsText.Package, "", "github.com/owner/repo@1.0.0")],
          values => {
            LipClient client = Context.Client;
            string package = values[0];
            Context.RunOperation(
                ToolsText.ViewTitle,
                token => client.View(package).WaitAsync(token),
                json => Context.Show(new ViewerDialog(package, json.TrimEnd())),
                cancellable: true);
          },
          validate: values => values[0].Length == 0 ? ToolsText.PackageRequired : null));

  /// An action of the page: its label and one sentence about it, in the language of the
  /// interface when they are read, and what Enter does.
  private sealed record ToolAction(Func<string> Label, Func<string> About, Action Run);

  /// Whether the selected workspace has a lip.toml and a tooth.json.
  private readonly record struct FileStatus(bool LipToml, bool ToothJson);
}
