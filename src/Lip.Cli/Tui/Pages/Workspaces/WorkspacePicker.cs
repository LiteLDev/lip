using System.IO.Abstractions;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Widgets;
using Lip.Cli.Workspaces;

namespace Lip.Cli.Tui.Pages.Workspaces;

/// The workspace picker, which Enter on the workspace of the top bar opens: the workspace list
/// (protocol/lip-tui.md, "Workspaces") below the row that adds a folder, which may be one that
/// the folder browser creates. Enter on a workspace offers to select it and to remove it from
/// the list; on the selected one it closes the picker. Selecting or adding a workspace closes
/// the picker too. Nothing is installed here: the Server page sets a workspace up. A workspace
/// whose server runs, whichever lip runs it, says so.
public sealed class WorkspacePicker : Dialog {
  // The areas of the picker that take the focus.
  private const int ActionsArea = 0;
  private const int ListArea = 1;

  // How many ticks pass between two readings of the list while the picker is shown: other
  // interfaces change it too.
  private const int TicksPerLoad = 8;

  // The most rows of workspaces that the picker is high enough for.
  private const int MostRows = 14;

  /// A workspace of the list, and whether its directory is missing.
  private sealed record Entry(string Path, bool Missing);

  private readonly ListView<int> _actions;
  private readonly ListView<Entry> _list;
  private readonly FocusRing _focus = new(2) { Index = ListArea };
  private bool _selectCurrent = true;
  private bool _started;
  private bool _loading;
  private bool _loadAgain;
  private string? _loadError;
  private int _ticks;
  private int _rowWidth;

  public WorkspacePicker() {
    _actions = new ListView<int>(_ => new StyledLine(WorkspacesText.AddFolder, Theme.Accent)) {
      Items = [0],
    };
    _list = new ListView<Entry>(Row) { Empty = () => WorkspacesText.Empty };
  }

  public override IReadOnlyList<Hint> Hints =>
      !_list.HasCurrent ? [CommonText.HintSelect, CommonText.HintClose]
          : _focus.Index == ActionsArea
              ? [CommonText.HintMove, CommonText.HintSelect, CommonText.HintNext,
                CommonText.HintClose]
              : [
                CommonText.HintMove, CommonText.HintActions, CommonText.HintNext,
                .. _list.PageHints, CommonText.HintClose,
              ];

  public override void OnTick() {
    if (++_ticks % TicksPerLoad == 0) {
      Load();
    }
  }

  public override void Draw(ScreenBuffer screen) {
    Start();
    // The action, a rule, the list, a rule, and two rows about the selected row.
    int rows = Math.Clamp(_list.Items.Count, 3, MostRows);
    Rect inner = Ui.DialogFrame(
        screen, WorkspacesText.Title, Ui.DialogWidth(screen), _actions.Items.Count + rows + 7);
    if (inner.Height < 1 || inner.Width < 1) {
      return;
    }

    bool focusOnList = _focus.Index == ListArea && _list.HasCurrent;
    int actions = Math.Min(_actions.Items.Count, inner.Height);
    _actions.Draw(screen, new Rect(inner.X, inner.Y, inner.Width, actions), !focusOnList);
    int details = inner.Height >= actions + 5 ? 3 : 0;
    Rect list = new(
        inner.X, inner.Y + actions + 1, inner.Width, inner.Height - actions - 1 - details);
    if (list.Height < 1) {
      return;
    }

    screen.Write(inner.X, list.Y - 1, new string('─', inner.Width), Theme.Border);
    // The last column is that of the marks of rows hidden above or below.
    _rowWidth = list.Width - 1;
    _list.Draw(screen, list, focusOnList);
    if (details > 0) {
      screen.Write(inner.X, list.Bottom, new string('─', inner.Width), Theme.Border);
      List<string> lines = Details(inner.Width);
      for (int i = 0; i < lines.Count && i < details - 1; i++) {
        screen.Write(inner.X, list.Bottom + 1 + i, lines[i], Theme.Muted, inner.Right);
      }
    }
  }

  public override void OnKey(ConsoleKeyInfo key) {
    Start();
    if (key.Key == ConsoleKey.Escape) {
      Close();
      return;
    }

    // Tab does not go to a list without rows.
    _focus.Count = _list.HasCurrent ? 2 : 1;
    if (_focus.Index == ListArea && !_list.HasCurrent) {
      _focus.Index = ActionsArea;
    }

    if (key.Key == ConsoleKey.Tab) {
      // Round, from the last area to the first and back.
      if (!_focus.OnKey(key)) {
        _focus.Index = _focus.Index == ActionsArea ? _focus.Count - 1 : ActionsArea;
      }

      return;
    }

    if (_focus.Index == ActionsArea) {
      if (key.Key is ConsoleKey.Enter or ConsoleKey.Spacebar) {
        AddFolder(Context.Workspace);
      } else if (key.Key == ConsoleKey.DownArrow && _list.HasCurrent) {
        _list.Selected = 0;
        _focus.Index = ListArea;
      }

      return;
    }

    if (key.Key is ConsoleKey.Enter or ConsoleKey.Spacebar) {
      OpenMenu(_list.Current);
      return;
    }

    if (!_list.OnKey(key) && key.Key == ConsoleKey.UpArrow) {
      _focus.Index = ActionsArea;
    }
  }

  /// Reads the list once the picker is first drawn or takes a key.
  private void Start() {
    if (!_started) {
      _started = true;
      Load();
    }
  }

  /// Reads the workspace list, and which of its directories are missing, in the background:
  /// one reading at a time, and once more after it when the list was asked for meanwhile.
  private void Load() {
    if (_loading) {
      _loadAgain = true;
      return;
    }

    _loading = true;
    WorkspaceList workspaces = Context.Workspaces;
    IFileSystem fileSystem = Context.FileSystem;
    AppContext context = Context;
    void Loaded(string? error) {
      _loading = false;
      if (error is not null && error != _loadError) {
        // Said once, not at every reading that fails the same way.
        context.Toast($"{WorkspacesText.ListUnreadable}: {error}", ToastKind.Bad);
      }

      _loadError = error;
      if (_loadAgain) {
        _loadAgain = false;
        Load();
      }
    }

    // The list is of no workspace: what was read of it counts whichever one is selected now.
    context.Background(
        () => Task.FromResult<IReadOnlyList<Entry>>([
          .. workspaces.Read().Select(p => new Entry(p, !fileSystem.Directory.Exists(p))),
        ]),
        entries => {
          Show(entries);
          Loaded(null);
        },
        ex => Loaded(ex.Message),
        anyWorkspace: true);
  }

  /// Shows the workspaces, keeping the selection on the workspace it is on, or putting it on
  /// the selected workspace when the picker opens.
  private void Show(IReadOnlyList<Entry> entries) {
    string? selected = _selectCurrent ? Context.Workspace
        : _list.HasCurrent ? _list.Current.Path : null;
    _list.Items = entries;
    if (selected is not null
        && _list.Select(e => WorkspaceList.PathComparer.Equals(e.Path, selected))) {
      _selectCurrent = false;
    }
  }

  private StyledLine Row(Entry entry) {
    bool current = IsCurrent(entry);
    string mark = entry.Missing ? WorkspacesText.Missing
        : IsRunning(entry) ? WorkspacesText.ServerRunning : "";
    int room = _rowWidth - 2 - (mark.Length > 0 ? TextWidth.Of(mark) + 2 : 0);
    string path = TextWidth.FitEnd(entry.Path, room);
    StyledLine line = new StyledLine()
        .Add(current ? "● " : "  ", Theme.Good)
        .Add(path, current ? Theme.Strong : default);
    if (mark.Length > 0) {
      int gap = _rowWidth - 2 - TextWidth.Of(path) - TextWidth.Of(mark);
      line.Add(new string(' ', Math.Max(1, gap)))
          .Add(mark, entry.Missing ? Theme.Warning : Theme.Good);
    }

    return line;
  }

  /// What the details area says of the row that has the focus: one sentence for the action;
  /// the whole path of a workspace, and one sentence about its state.
  private List<string> Details(int width) {
    if (_focus.Index == ActionsArea || !_list.HasCurrent) {
      return TextWidth.Wrap(WorkspacesText.AddFolderDetail, width);
    }

    Entry entry = _list.Current;
    bool current = IsCurrent(entry);
    string state = entry.Missing ? WorkspacesText.MissingDetail
        : IsRunning(entry)
            ? current ? WorkspacesText.CurrentRunningDetail : WorkspacesText.RunningDetail
        : current ? WorkspacesText.CurrentDetail
        : "";
    return [TextWidth.FitEnd(entry.Path, width), TextWidth.Fit(state, width)];
  }

  /// Whether the workspace is the selected one. Both paths are in canonical form.
  private bool IsCurrent(Entry entry) =>
      WorkspaceList.PathComparer.Equals(entry.Path, Context.Workspace);

  /// Whether the server of the workspace runs (protocol/lip-tui.md, "Server"): one of this
  /// process, or one that another lip runs.
  private bool IsRunning(Entry entry) => Context.IsServerRunning(entry.Path);

  /// The action menu of a workspace: to select it, unless its directory is missing, and to
  /// remove it from the list. Enter on the selected workspace, which is neither selected again
  /// nor removed, closes the picker.
  private void OpenMenu(Entry entry) {
    if (IsCurrent(entry)) {
      Close();
      return;
    }

    List<MenuItem> items = [];
    if (!entry.Missing) {
      items.Add(new MenuItem(
          WorkspacesText.Switch, () => Context.SwitchWorkspace(entry.Path, Close)));
    }

    items.Add(new MenuItem(WorkspacesText.Remove, () => Remove(entry.Path)));
    Context.ShowMenu(Name(entry.Path), [.. items]);
  }

  /// Asks for a folder in a folder browser that starts at a folder, where [ New folder… ]
  /// creates one; adds it to the list, selects it, and closes the picker.
  private void AddFolder(string start) {
    AppContext context = Context;
    context.Show(PathPicker.Folder(WorkspacesText.AddTitle, start, folder => {
      string? error;
      try {
        // The directory may be the selected workspace already, which changes nothing.
        if (context.AddWorkspace(folder, out error, Close)) {
          return;
        }
      }
      catch (Exception ex) when (ex is ArgumentException or NotSupportedException
          or IOException) {
        error = WorkspacesText.InvalidPath;
      }

      // The browser is back at the folder, below why it was not added.
      AddFolder(folder);
      context.Message(WorkspacesText.AddTitle, error!, Theme.Bad);
    }));
  }

  /// Removes a workspace other than the selected one from the list, once the user confirmed;
  /// no file in it changes.
  private void Remove(string path) {
    AppContext context = Context;
    context.Confirm(
        Name(path),
        WorkspacesText.RemoveQuestion,
        WorkspacesText.Remove,
        () => {
          WorkspaceList workspaces = context.Workspaces;
          if (workspaces.Same(path, context.Workspace)) {
            return;
          }

          context.Background(
              () => Task.Run(() => {
                workspaces.Remove(path);
                return true;
              }),
              _ => {
                context.Toast(WorkspacesText.Removed, ToastKind.Good);
                Load();
              },
              ex => context.Message(
                  Name(path), $"{CommonText.WorkspaceListUnwritable}: {ex.Message}",
                  Theme.Bad),
              anyWorkspace: true);
        });
  }

  /// The name of the directory at a path, or the path when it has none, as a root.
  private string Name(string path) =>
      Context.FileSystem.Path.GetFileName(path) is { Length: > 0 } name ? name : path;
}
