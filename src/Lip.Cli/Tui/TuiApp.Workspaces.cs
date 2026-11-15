namespace Lip.Cli.Tui;

/// The workspaces of the interface (protocol/lip-tui.md, "Workspaces"): w opens the workspace
/// list, in which the user selects, adds and removes workspaces. Every tab works on the selected
/// workspace.
public sealed partial class TuiApp {
  private void OpenWorkspaces() {
    // A workspace is selected only once the changes to a configuration file are saved or
    // discarded.
    if (ConfigDirty) {
      _tab = ConfigTab;
      LeaveConfigFile(OpenWorkspaces);
      return;
    }

    Push(new WorkspacesDialog(this));
  }

  /// Selects the workspace: the commands carried out from now on have it as their working
  /// directory, and the tabs show it.
  private void SwitchWorkspace(string workspace) {
    if (_workspaces.Same(workspace, _workspace)) {
      return;
    }

    if (ConfigDirty) {
      _tab = ConfigTab;
      LeaveConfigFile(() => SwitchWorkspace(workspace));
      return;
    }

    _workspace = _workspaces.Normalize(workspace);
    _client = _createClient(_interaction, _hookOutput, _workspace);
    _serverScroll = 0;
    _serverLine.Set("");
    _installed = [];
    _installedLoaded = false;
    _installedError = null;
    _packageSelected = 0;
    _configFiles = [];
    _configFilesLoaded = false;
    _configFilesError = null;
    _configFileSelected = 0;
    _configFileTop = 0;
    _configEntriesFocused = false;
    _configDocument = null;
    _configDocumentItem = null;
    _config = [];
    _configError = null;
    if (_tab != PackagesTab) {
      LoadPackages();
    }

    SwitchTab(_tab);
    _status = new StyledLine($"Workspace: {_workspace}", Theme.Accent);
  }

  /// Adds the directory at path, absolute or relative to the selected workspace, to the list,
  /// and selects it. False, with why, when it is not a directory.
  private bool AddWorkspace(string path, out string? error) {
    string full = FullPath(path);
    if (!_workspaces.FileSystem.Directory.Exists(full)) {
      error = $"{full} is not a directory.";
      return false;
    }

    try {
      SwitchWorkspace(_workspaces.Add(full));
      error = null;
      return true;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
      error = $"Could not write the workspace list: {ex.Message}";
      return false;
    }
  }

  /// The canonical form of a path, absolute or relative to the selected workspace.
  private string FullPath(string path) {
    System.IO.Abstractions.IFileSystem fileSystem = _workspaces.FileSystem;
    return _workspaces.Normalize(
        fileSystem.Path.IsPathFullyQualified(path) ? path : fileSystem.Path.Join(_workspace, path));
  }

  /// The workspace list: Enter selects a workspace, a adds one, d removes one from the list,
  /// which changes no file in it.
  private sealed class WorkspacesDialog : Dialog {
    private readonly TuiApp _app;
    private IReadOnlyList<string> _list = [];
    private int _selected;
    private int _top;
    private int _rows = 1;
    private StyledLine? _note;

    public WorkspacesDialog(TuiApp app) {
      _app = app;
      Reload();
      _selected = Math.Max(0, IndexOf(app._workspace));
    }

    public override (string, string)[] Hints =>
        [
          ("Enter", "select"), ("a", "add"), ("n", "new server"), ("d/Del", "remove from the list"), ("↑/↓", "move"),
          ("Esc", "close"),
        ];

    public override void Draw(ScreenBuffer screen) {
      int width = Math.Min(screen.Width - 4, 90);
      Rect inner = Frame(screen, "Workspaces", width, Math.Max(_list.Count, 1) + 4);
      _rows = Math.Max(1, inner.Height - 2);
      _top = ScrollTo(_selected, _top, _rows);
      if (_list.Count == 0) {
        screen.Write(inner.X, inner.Y, "The list is empty: press a to add a workspace.",
            Theme.Muted, inner.Right);
      }

      for (int i = 0; i < _rows && _top + i < _list.Count; i++) {
        string path = _list[_top + i];
        bool current = _app._workspaces.Same(path, _app._workspace);
        bool missing = !_app._workspaces.FileSystem.Directory.Exists(path);
        int y = inner.Y + i;
        int x = screen.Write(inner.X, y, current ? "● " : "  ", Theme.Good);
        x = screen.WriteFit(x, y, inner.Right - x - (missing ? 10 : 0), path,
            current ? Theme.Strong : default);
        if (missing) {
          screen.Write(x + 1, y, "(missing)", Theme.Warning);
        }

        if (_top + i == _selected) {
          screen.Restyle(inner.X - 1, y, inner.Width + 2, Theme.Selected);
        }
      }

      if (_note is { } note) {
        screen.Write(inner.X, inner.Bottom - 1, note, inner.Right);
      }
    }

    public override void OnKey(ConsoleKeyInfo key) {
      _note = null;
      switch (key.Key) {
        case ConsoleKey.Escape:
          _app.Close(this);
          return;
        case ConsoleKey.Enter or ConsoleKey.Spacebar:
          if (_selected < _list.Count) {
            _app.Close(this);
            _app.SwitchWorkspace(_list[_selected]);
          }
          return;
        case ConsoleKey.Delete:
          Remove();
          return;
      }

      switch (key.KeyChar) {
        case 'a':
          _app.Push(new PromptDialog(
              _app, "Add a workspace",
              [new PromptField(
                  "The path of the directory, absolute or relative to the selected workspace:")],
              values => {
                if (values[0].Length == 0) {
                  return;
                }

                if (_app.AddWorkspace(values[0], out string? error)) {
                  _app.Close(this);
                } else {
                  _note = new StyledLine(error!, Theme.Bad);
                }
              }));
          return;
        case 'd':
          Remove();
          return;
        case 'n':
          _app.Close(this);
          _app.NewServer();
          return;
      }

      MoveSelection(key, ref _selected, _list.Count, _rows, wrap: true);
    }

    /// Removes the selected workspace from the list; the workspace that is selected in the
    /// interface stays.
    private void Remove() {
      if (_selected >= _list.Count) {
        return;
      }

      string path = _list[_selected];
      if (_app._workspaces.Same(path, _app._workspace)) {
        _note = new StyledLine(
            "The workspace that is selected cannot be removed; select another one first.",
            Theme.Warning);
        return;
      }

      try {
        _app._workspaces.Remove(path);
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
        _note = new StyledLine($"Could not write the workspace list: {ex.Message}", Theme.Bad);
        return;
      }

      Reload();
      _selected = Math.Clamp(_selected, 0, Math.Max(0, _list.Count - 1));
      _note = new StyledLine($"Removed {path} from the list; no file in it changed.", Theme.Muted);
    }

    private void Reload() {
      try {
        _list = _app._workspaces.Read();
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
        _list = [];
        _note = new StyledLine($"Could not read the workspace list: {ex.Message}", Theme.Bad);
      }
    }

    private int IndexOf(string path) {
      for (int i = 0; i < _list.Count; i++) {
        if (_app._workspaces.Same(_list[i], path)) {
          return i;
        }
      }

      return -1;
    }
  }
}
