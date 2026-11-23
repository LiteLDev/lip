using System.IO.Abstractions;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Widgets;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Compat;
using Lip.Core.Mcmod.Editing;
using Lip.Core.PublicApi;

namespace Lip.Cli.Tui.Pages.Tools;

/// The Tools page: the commands for developing packages (protocol/lip-pkg.md), each a row that
/// Enter carries out, with one sentence about the selected one, and whether the selected
/// workspace has a lip.toml and a tooth.json. An action that those files make fail (lip pkg
/// migrate when lip.toml exists or there is no tooth.json) is shown as disabled, with the
/// reason below its sentence, until the files change. The first action creates lip.toml, or
/// edits it when it exists, in the editor (ManifestEditor), which then takes the page.
public sealed class ToolsPage : Page {
  // How many ticks pass between two looks at the files of the workspace: two seconds.
  private const int RefreshTicks = 8;

  private readonly ToolAction[] _actions;
  private readonly ListView<ToolAction> _list;
  private FileStatus? _status;
  private int _ticks;

  // Counts the reads of the files, so that only the result of the last one is shown.
  private int _reads;

  // The lip.toml editor while it is open, and the workspace that it edits the file of.
  private ManifestEditor? _editor;
  private string? _editorWorkspace;

  // Whether the editor is being opened: Enter meanwhile opens no second one.
  private bool _opening;

  public ToolsPage(AppContext context) : base(context) {
    _actions = [
      new ToolAction(
          () => _status is { LipToml: true } ? ManifestEditorText.Edit : ToolsText.Init,
          () => _status is { LipToml: true }
              ? ManifestEditorText.EditAbout
              : ToolsText.InitAbout,
          OpenEditor, () => null),
      new ToolAction(
          () => ToolsText.Migrate, () => ToolsText.MigrateAbout, Migrate, MigrateReason),
    ];
    _list = new ListView<ToolAction>(action => new StyledLine(
        action.Label(), action.Reason() is null ? default : Theme.Muted)) {
      Wrap = true,
      Items = _actions,
    };
  }

  public override string Title => ToolsText.Title;

  public override IReadOnlyList<Hint> Hints =>
      _editor is { } editor ? editor.Hints
          : _list.Current.Reason() is null ? [.. _list.Hints, CommonText.HintSelect]
          : _list.Hints;

  public override bool CanLeave => _editor is not { Dirty: true };

  public override void ConfirmLeave(Action leave) {
    if (_editor is { } editor) {
      editor.ConfirmDiscard(() => {
        editor.Close();
        leave();
      });
    } else {
      leave();
    }
  }

  public override void OnShown() => LoadStatus();

  public override void OnWorkspaceChanged() {
    // The editor is of the workspace that was selected; an operation in it leaves it open.
    if (_editorWorkspace is { } edited && !Context.Workspaces.Same(edited, Context.Workspace)) {
      _editor?.Close();
    }

    // A file that was being read for the editor is of no workspace that is selected now.
    _opening = false;
    _status = null;
    if (Context.CurrentPage == PageId.Tools) {
      LoadStatus();
    }
  }

  public override void OnTick() {
    _editor?.OnTick();
    // The files may change outside the interface, or by a command that failed half way.
    if (Context.CurrentPage == PageId.Tools && Context.Idle && ++_ticks >= RefreshTicks) {
      LoadStatus();
    }
  }

  public override void Draw(ScreenBuffer screen, Rect area, bool focused) {
    if (_editor is { } editor) {
      editor.Draw(screen, area, focused);
      return;
    }

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
    if (_editor is { } editor) {
      return editor.OnKey(key);
    }

    if (_opening) {
      return true;
    }

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

  /// Why lip pkg migrate fails in the selected workspace, or null: it needs a tooth.json and
  /// no lip.toml (protocol/lip-compatibility.md, "lip pkg migrate").
  private string? MigrateReason() =>
      _status switch {
        { LipToml: true } => ToolsText.Exists(PackageFormat.FileName),
        { ToothJson: false } => ToolsText.NoFile(ToothCompat.ToothFileName),
        _ => null,
      };

  public override void OnTabIn(bool backward) => _editor?.TabIn(backward);

  /// Opens the lip.toml editor: on the file of the selected workspace when it has one, on a
  /// new one otherwise. A file that the editor cannot read is not opened, and the user is told
  /// why.
  private void OpenEditor() {
    IFileSystem fs = Context.FileSystem;
    string workspace = Context.Workspace;
    _opening = true;
    Context.Background(
        () => Task.FromResult(PackageFileStore.Read(fs, workspace)),
        file => {
          _opening = false;
          PackageDraft draft;
          try {
            draft = file.Written is { } written ? PackageDraft.Read(written) : PackageDraft.New();
          }
          catch (PackageDraftException ex) {
            CannotEdit(ex);
            return;
          }

          _editorWorkspace = workspace;
          _editor = new ManifestEditor(Context, file, draft, () => {
            _editor = null;
            _editorWorkspace = null;
            LoadStatus();
          });
        },
        ex => {
          _opening = false;
          CannotEdit(ex);
        });
  }

  private void CannotEdit(Exception ex) =>
      Context.Message(
          ManifestEditorText.EditTitle, $"{ManifestEditorText.CannotEdit} {ex.Message}",
          Theme.Warning);

  /// lip pkg migrate: the command asks for what the tooth.json lacks through the interface.
  private void Migrate() {
    LipClient client = Context.Client;
    Context.RunOperation(ToolsText.Migrate, _ => client.Migrate(), done => {
      LoadStatus();
      Context.ShowResult(ToolsText.Migrate, done, ToolsText.Migrated);
    });
  }

  /// An action of the page: its label and one sentence about it, in the language of the
  /// interface when they are read, what Enter does, and why it cannot be done now, or null.
  private sealed record ToolAction(
      Func<string> Label, Func<string> About, Action Run, Func<string?> Reason);

  /// Whether the selected workspace has a lip.toml and a tooth.json.
  private readonly record struct FileStatus(bool LipToml, bool ToothJson);
}
