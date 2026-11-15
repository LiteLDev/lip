using System.IO.Abstractions;
using System.Text;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Widgets;
using Lip.Cli.Workspaces;

namespace Lip.Cli.Tui.Pages.Workspaces;

/// The Workspaces page: the workspace list (protocol/lip-tui.md, "Workspaces"), below the rows
/// that ask for a new server ("New server") and that add a directory. Enter on a workspace
/// offers to select it, to set it up again, and to remove it from the list.
public sealed class WorkspacesPage : Page {
  // The areas of the page that take the focus.
  private const int ActionsArea = 0;
  private const int ListArea = 1;

  // How many ticks pass between two readings of the list while the page is shown: other
  // interfaces change it too.
  private const int TicksPerLoad = 8;

  /// A workspace of the list, and whether its directory is missing.
  private sealed record Entry(string Path, bool Missing);

  private readonly ListView<int> _actions;
  private readonly ListView<Entry> _list;
  private readonly FocusRing _focus = new(2);
  private readonly ServerWizard _wizard;
  private bool _selectCurrent = true;
  private bool _loading;
  private bool _loadAgain;
  private string? _loadError;
  private int _ticks;
  private int _rowWidth;

  public WorkspacesPage(AppContext context) : base(context) {
    _wizard = new ServerWizard(context);
    _actions = new ListView<int>(action => new StyledLine(
        action == 0 ? WorkspacesText.NewServer : WorkspacesText.AddFolder, Theme.Accent)) {
      Items = [0, 1],
    };
    _list = new ListView<Entry>(Row) { Empty = () => WorkspacesText.Empty };
  }

  public override string Title => WorkspacesText.Title;

  public override IReadOnlyList<Hint> Hints =>
      _focus.Index == ActionsArea
          ? [CommonText.HintMove, CommonText.HintSelect, CommonText.HintNext]
          : _list.HasCurrent
              ? [CommonText.HintMove, CommonText.HintActions, CommonText.HintNext]
              : [CommonText.HintNext];

  public override void OnShown() {
    _ticks = 0;
    Load();
  }

  public override void OnTick() {
    if (Context.CurrentPage == PageId.Workspaces && ++_ticks % TicksPerLoad == 0) {
      Load();
    }
  }

  public override void OnWorkspaceChanged() {
    _selectCurrent = true;
    if (Context.CurrentPage == PageId.Workspaces) {
      Load();
    }
  }

  public override void OnRequest(string request) {
    if (request == PageRequest.NewServer) {
      _wizard.NewServer();
    }
  }

  public override void Draw(ScreenBuffer screen, Rect area, bool focused) {
    if (area.Height < 1 || area.Width < 1) {
      return;
    }

    // The actions, a rule, the list, a rule, and two rows about the selected row.
    int actions = Math.Min(_actions.Items.Count, area.Height);
    _actions.Draw(
        screen, new Rect(area.X, area.Y, area.Width, actions),
        focused && _focus.Index == ActionsArea);
    int details = area.Height >= actions + 5 ? 3 : 0;
    Rect list = new(
        area.X, area.Y + actions + 1, area.Width, area.Height - actions - 1 - details);
    if (list.Height < 1) {
      return;
    }

    screen.Write(area.X, list.Y - 1, new string('─', area.Width), Theme.Border);
    // The last column is that of the marks of rows hidden above or below.
    _rowWidth = list.Width - 1;
    _list.Draw(screen, list, focused && _focus.Index == ListArea);
    if (details > 0) {
      screen.Write(area.X, list.Bottom, new string('─', area.Width), Theme.Border);
      List<string> lines = Details(area.Width);
      for (int i = 0; i < lines.Count && i < details - 1; i++) {
        screen.Write(area.X, list.Bottom + 1 + i, lines[i], Theme.Muted, area.Right);
      }
    }
  }

  public override bool OnKey(ConsoleKeyInfo key) {
    // Tab does not go to a list without rows.
    _focus.Count = _list.HasCurrent ? 2 : 1;
    if (_focus.OnKey(key)) {
      return true;
    }

    if (_focus.Index == ActionsArea) {
      if (key.Key == ConsoleKey.Enter) {
        if (_actions.Current == 0) {
          _wizard.NewServer();
        } else {
          AddFolder();
        }

        return true;
      }

      if (_actions.OnKey(key)) {
        return true;
      }

      if (key.Key == ConsoleKey.DownArrow && _list.HasCurrent) {
        _list.Selected = 0;
        return _focus.Next();
      }

      return false;
    }

    if (key.Key == ConsoleKey.Enter) {
      if (_list.HasCurrent) {
        OpenMenu(_list.Current);
      }

      return true;
    }

    if (_list.OnKey(key)) {
      return true;
    }

    if (key.Key == ConsoleKey.UpArrow) {
      _actions.Selected = _actions.Items.Count - 1;
      return _focus.Previous();
    }

    return false;
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
    _ = Task.Run(() => {
      IReadOnlyList<Entry>? entries = null;
      string? error = null;
      try {
        entries = [
          .. workspaces.Read().Select(p => new Entry(p, !fileSystem.Directory.Exists(p))),
        ];
      }
      catch (Exception ex) {
        error = ex.Message;
      }

      context.Post(() => {
        _loading = false;
        if (entries is not null) {
          Show(entries);
        } else if (error != _loadError) {
          // Said once, not at every reading that fails the same way.
          Context.Toast($"{WorkspacesText.ListUnreadable}: {error}", ToastKind.Bad);
        }

        _loadError = error;
        if (_loadAgain) {
          _loadAgain = false;
          Load();
        }
      });
    });
  }

  /// Shows the workspaces, keeping the selection on the workspace it is on, or putting it on
  /// the selected workspace after that one changed.
  private void Show(IReadOnlyList<Entry> entries) {
    string? selected = _selectCurrent ? Context.Workspace
        : _list.HasCurrent ? _list.Current.Path : null;
    _list.Items = entries;
    if (selected is not null) {
      _list.Select(e => WorkspaceList.PathComparer.Equals(e.Path, selected));
    }

    _selectCurrent = false;
    if (entries.Count == 0 && _focus.Index == ListArea) {
      _focus.Index = ActionsArea;
    }
  }

  private StyledLine Row(Entry entry) {
    bool current = IsCurrent(entry);
    bool running = IsRunning(entry);
    string mark = entry.Missing ? WorkspacesText.Missing
        : running ? WorkspacesText.ServerRunning : "";
    int room = _rowWidth - 2 - (mark.Length > 0 ? TextWidth.Of(mark) + 2 : 0);
    string path = FitEnd(entry.Path, room);
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

  /// What the details area says of the row that has the focus: one sentence for an action; the
  /// whole path of a workspace, and one sentence about its state.
  private List<string> Details(int width) {
    if (_focus.Index == ActionsArea) {
      return TextWidth.Wrap(
          _actions.Current == 0 ? WorkspacesText.NewServerDetail : WorkspacesText.AddFolderDetail,
          width);
    }

    if (!_list.HasCurrent) {
      return [];
    }

    Entry entry = _list.Current;
    string state = entry.Missing ? WorkspacesText.MissingDetail
        : IsCurrent(entry) ? WorkspacesText.CurrentDetail
        : IsRunning(entry) ? WorkspacesText.RunningDetail
        : "";
    return [FitEnd(entry.Path, width), TextWidth.Fit(state, width)];
  }

  /// Whether the workspace is the selected one. Both paths are in canonical form.
  private bool IsCurrent(Entry entry) =>
      WorkspaceList.PathComparer.Equals(entry.Path, Context.Workspace);

  /// Whether the server of the workspace runs (protocol/lip-tui.md, "Server").
  private bool IsRunning(Entry entry) => Context.Servers.Find(entry.Path)?.Running == true;

  /// The action menu of a workspace. The selected workspace is neither selected again nor
  /// removed; a directory that is missing is neither selected nor set up; and a workspace is
  /// not set up while its server runs.
  private void OpenMenu(Entry entry) {
    bool current = IsCurrent(entry);
    List<MenuItem> items = [];
    if (!current && !entry.Missing) {
      items.Add(new MenuItem(WorkspacesText.Switch, () => Context.SwitchWorkspace(entry.Path)));
    }

    if (!entry.Missing) {
      items.Add(new MenuItem(
          WorkspacesText.ChangeVersions, () => _wizard.ChangeVersions(entry.Path),
          Enabled: !IsRunning(entry)));
    }

    if (!current) {
      items.Add(new MenuItem(WorkspacesText.Remove, () => Remove(entry.Path)));
    }

    if (items.Count == 0) {
      Context.Toast(WorkspacesText.MissingDetail, ToastKind.Warning);
      return;
    }

    Context.ShowMenu(Name(entry.Path), [.. items]);
  }

  /// Asks for a directory that exists, adds it to the list and selects it.
  private void AddFolder() =>
      Context.Show(new PromptDialog(
          WorkspacesText.AddTitle,
          [new PromptField(WorkspacesText.Folder, "", WorkspacesText.FolderExample)],
          _ => {
            // The directory may be the selected workspace already, which changes nothing.
            _selectCurrent = true;
            _focus.Index = ListArea;
            Load();
          },
          validate: values => {
            if (values[0].Length == 0) {
              return CommonText.Required;
            }

            // Adding is the check: the dialog stays open with why the directory was not added.
            try {
              return Context.AddWorkspace(values[0], out string? error) ? null : error;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException
                or IOException) {
              return WorkspacesText.InvalidPath;
            }
          }));

  /// Removes a workspace other than the selected one from the list, once the user confirmed;
  /// no file in it changes.
  private void Remove(string path) =>
      Context.Confirm(
          Name(path),
          WorkspacesText.RemoveQuestion,
          WorkspacesText.Remove,
          () => {
            WorkspaceList workspaces = Context.Workspaces;
            if (workspaces.Same(path, Context.Workspace)) {
              return;
            }

            Context.Background(
                () => Task.Run(() => {
                  workspaces.Remove(path);
                  return true;
                }),
                _ => {
                  Context.Toast(WorkspacesText.Removed, ToastKind.Good);
                  Load();
                },
                ex => Context.Message(
                    Name(path), $"{CommonText.WorkspaceListUnwritable}: {ex.Message}",
                    Theme.Bad));
          });

  /// The name of the directory at a path, or the path when it has none, as a root.
  private string Name(string path) =>
      Context.FileSystem.Path.GetFileName(path) is { Length: > 0 } name ? name : path;

  /// The text cut to at most width columns, keeping its end, after an ellipsis when it was
  /// cut: the end of a path says the most.
  private static string FitEnd(string text, int width) {
    if (TextWidth.Of(text) <= width) {
      return text;
    }

    if (width <= 0) {
      return "";
    }

    List<Rune> runes = [.. text.EnumerateRunes()];
    int used = 0;
    int first = runes.Count;
    while (first > 0 && used + TextWidth.Of(runes[first - 1]) <= width - 1) {
      used += TextWidth.Of(runes[--first]);
    }

    return "…" + string.Concat(runes.Skip(first).Select(r => r.ToString()));
  }
}
