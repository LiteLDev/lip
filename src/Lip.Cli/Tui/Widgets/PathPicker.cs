using System.IO.Abstractions;
using System.Text;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Workspaces;

namespace Lip.Cli.Tui.Widgets;

/// Asks for a path with a small file browser, so that no path has to be typed: a path bar, the
/// list of what the folder shown holds, and buttons. Make one with Folder, OpenFile or SaveFile.
///
/// The list starts with "..", then has the folders, each with a separator after its name, then
/// the files that the picker is for. Enter or → opens a folder; Enter on ".." or ← goes up, to
/// the roots (the drives on Windows, / elsewhere) from a root, with the folder just left
/// selected. The path bar takes a path that is typed or pasted, absolute or relative to the
/// folder shown, and Enter goes there. Tab and Shift+Tab go round the bar, the list, the file
/// name of SaveFile and the buttons; ↑ and ↓ leave the list at its ends. Esc cancels, but from
/// the path bar, where it goes back to the list.
///
/// Entries whose name starts with a dot, and hidden or system ones, are left out everywhere; a
/// path to one can still be typed. A folder that cannot be read shows one line that says so,
/// and stays one to go up from. The file system is read in the background only.
public sealed class PathPicker : Dialog {
  /// The most folders, and the most files, of a folder that are listed.
  public const int MaxEntries = 5000;

  private enum Mode {
    Folder,
    NewFolder,
    File,
    FileOrFolder,
    Save,
  }

  private enum Area {
    Bar,
    List,
    Name,
    Buttons,
  }

  private enum EntryKind {
    Parent,
    Root,
    Directory,
    File,
  }

  private enum Note {
    None,
    Unreadable,
    Capped,
  }

  private enum Problem {
    None,
    NotFound,
    NotAFolder,
    WrongType,
    InvalidName,
    CannotCreate,
  }

  /// A row of the list: its name, and the full path that it leads to.
  private sealed record Entry(EntryKind Kind, string Name, string Path);

  /// What reading the file system came to: a path that is chosen, a problem that keeps what is
  /// shown, or the folder to show (the roots when Directory is null) with its entries.
  private sealed record Outcome {
    public Problem Problem { get; init; }
    public string? Choose { get; init; }
    public string? Directory { get; init; }
    public IReadOnlyList<Entry> Entries { get; init; } = [];
    public string? Select { get; init; }
    public Note Note { get; init; }
    public string? Name { get; init; }

    /// The path that the bar starts with, and takes the focus for: the folder that the
    /// picker started at, when lip creates it later and it does not exist yet.
    public string? Typed { get; init; }
  }

  private readonly string _title;
  private readonly Mode _mode;
  private readonly string _start;
  private readonly IReadOnlyList<string> _extensions;
  private readonly Action<string> _chosen;
  private readonly Action? _cancel;
  private readonly Func<string, string?>? _validate;
  private readonly string _defaultName;
  private readonly TextField _bar = new();
  private readonly TextField _name;
  private readonly ListView<Entry> _list;
  private readonly ButtonRow _buttons;
  private readonly Area[] _areas;
  private int _focus;
  private int _listWidth = 1;
  private char _separator = '/';
  // The folder shown; null for the roots, and before the first folder is read.
  private string? _current;
  private bool _started;
  private bool _loading;
  // Counts what was asked of the file system: an answer to anything but the last is dropped.
  private int _generation;
  private Note _note;
  private Problem _problem;
  private string? _error;

  private PathPicker(
      string title,
      Mode mode,
      string start,
      IReadOnlyList<string> extensions,
      string fileName,
      Action<string> chosen,
      Action? cancel,
      Func<string, string?>? validate) {
    _title = title;
    _mode = mode;
    _start = start;
    _extensions = [
      .. extensions.Where(e => e.Length > 0).Select(e => e.StartsWith('.') ? e : "." + e),
    ];
    _chosen = chosen;
    _cancel = cancel;
    _validate = validate;
    _defaultName = fileName;
    _name = new TextField(fileName);
    _list = new ListView<Entry>(Row);
    // Not the roots, which are no folder.
    Func<bool> inFolder = () => _current is not null;
    Button select = new(() => PathPickerText.SelectFolder, SelectFolder) { Enabled = inFolder };
    Button save = new(() => CommonText.Save, Save) { Enabled = inFolder };
    Button create = new(() => PathPickerText.NewFolder, AskNewFolder) { Enabled = inFolder };
    Button close = new(() => CommonText.Cancel, Cancel);
    _buttons = mode switch {
      Mode.File => new ButtonRow(close),
      Mode.FileOrFolder => new ButtonRow(select, close),
      Mode.Save => new ButtonRow(save, create, close),
      _ => new ButtonRow(select, create, close),
    };
    _areas = mode == Mode.Save
        ? [Area.Bar, Area.List, Area.Name, Area.Buttons]
        : [Area.Bar, Area.List, Area.Buttons];
    _focus = 1;
  }

  /// Asks for a folder. The list shows folders only; [ Select this folder ] chooses the one
  /// shown, and [ New folder… ] asks for a name, creates that folder and shows it.
  ///
  /// With mayNotExist, for a folder that lip creates later, nothing is created: [ New folder… ]
  /// chooses the folder of that name in the one shown, and a path typed in the bar that does
  /// not exist is chosen as it is, unless a name in it is none or a file is in its way. When
  /// start itself does not exist, as when the user comes back to the picker after they chose
  /// such a folder, the bar has the focus and holds start, so that Enter chooses it again.
  ///
  /// start is the folder shown first: the nearest folder above it that exists when it does
  /// not, and the roots when there is none. chosen gets the full path, without a separator at
  /// its end unless it is a root, once the dialog is closed; cancel is called when the user
  /// gives up. validate returns why a path cannot be taken, in the language of the interface,
  /// or null: the dialog shows it in one line and stays open. It is called on the thread of the
  /// interface, so it must not read the file system.
  public static PathPicker Folder(
      string title,
      string start,
      Action<string> chosen,
      Action? cancel = null,
      Func<string, string?>? validate = null,
      bool mayNotExist = false) =>
      new(
          title, mayNotExist ? Mode.NewFolder : Mode.Folder, start, [], "", chosen, cancel,
          validate);

  /// Asks for a file that exists. The list shows the folders and the files whose name ends
  /// with one of extensions (".zip"; every file when there is none), whatever the case; Enter
  /// on a file chooses it. A file typed in the bar is chosen when it is of those too.
  ///
  /// With orFolder, [ Select this folder ] chooses the folder shown instead of a file.
  /// start, chosen, cancel and validate are as for Folder.
  public static PathPicker OpenFile(
      string title,
      string start,
      IReadOnlyList<string> extensions,
      Action<string> chosen,
      Action? cancel = null,
      Func<string, string?>? validate = null,
      bool orFolder = false) =>
      new(
          title, orFolder ? Mode.FileOrFolder : Mode.File, start, extensions, "", chosen, cancel,
          validate);

  /// Asks where to save a file: the folder is the one shown, and the name is typed in a field
  /// that starts with fileName ("package.zip"). Enter in the field, or [ Save ], chooses the
  /// path; extension (".zip", or "" for none) is added to a name that does not end with it.
  /// Enter on a file of the list, which shows those that end with extension, takes its name.
  /// The file may exist: asking before it is overwritten is up to the caller.
  ///
  /// start, chosen, cancel and validate are as for Folder.
  public static PathPicker SaveFile(
      string title,
      string start,
      string fileName,
      string extension,
      Action<string> chosen,
      Action? cancel = null,
      Func<string, string?>? validate = null) =>
      new(title, Mode.Save, start, [extension], fileName, chosen, cancel, validate);

  private Area Focused => _areas[_focus];

  private bool ShowsFiles => _mode is Mode.File or Mode.FileOrFolder or Mode.Save;

  public override IReadOnlyList<Hint> Hints =>
      Focused switch {
        Area.Bar => [
          PathPickerText.HintGo, PathPickerText.HintFocus("↓/Tab"), CommonText.HintBack,
        ],
        Area.Name => [
          PathPickerText.HintSave, PathPickerText.HintFocus("↑↓/Tab"), CommonText.HintCancel,
        ],
        // ← and → do nothing while one button only can be chosen, as at the roots.
        Area.Buttons => [
          .. _buttons.Buttons.Count(button => button.IsEnabled) > 1
              ? [CommonText.HintSwitch]
              : (Hint[])[],
          CommonText.HintSelect, PathPickerText.HintFocus("↑/Tab"), CommonText.HintCancel,
        ],
        _ => ListHints(),
      };

  private List<Hint> ListHints() {
    List<Hint> hints = [];
    if (!_loading) {
      hints.Add(CommonText.HintMove);
      if (_list.HasCurrent) {
        EntryKind kind = _list.Current.Kind;
        hints.Add(kind switch {
          EntryKind.Parent => PathPickerText.HintUpEither,
          EntryKind.File => CommonText.HintSelect,
          _ => PathPickerText.HintOpenEither,
        });
        if (kind != EntryKind.Parent && _current is not null) {
          hints.Add(PathPickerText.HintUp);
        }
      }
    }

    hints.Add(CommonText.HintNext);
    if (!_loading) {
      hints.AddRange(_list.PageHints);
    }

    hints.Add(CommonText.HintCancel);
    return hints;
  }

  public override void Draw(ScreenBuffer screen) {
    Start();
    _separator = Context.FileSystem.Path.DirectorySeparatorChar;
    Rect inner = Ui.DialogFrame(
        screen, _title, Ui.DialogWidth(screen), Math.Min(screen.Height - 2, 26));

    // The path bar, and a line below it.
    int x = screen.Write(inner.X, inner.Y, PathPickerText.Folder, Theme.Muted, inner.Right) + 2;
    int room = inner.Right - x;
    if (Focused == Area.Bar) {
      _bar.Draw(screen, x, inner.Y, room, focused: true);
    } else if (_current is null) {
      screen.WriteFit(x, inner.Y, room, _started && !_loading ? PathPickerText.Computer : "");
    } else {
      screen.Write(x, inner.Y, FitMiddle(_current, room), Theme.Strong, inner.Right);
    }

    screen.Write(inner.X, inner.Y + 1, new string('─', inner.Width), Theme.Border);

    // The list, above the file name, the line that says what goes on, and the buttons.
    int below = _mode == Mode.Save ? 3 : 2;
    Rect rows = new(inner.X, inner.Y + 2, inner.Width, Math.Max(0, inner.Height - 2 - below));
    _listWidth = rows.Width;
    _list.Draw(screen, rows, Focused == Area.List && !_loading);

    int y = inner.Bottom - below;
    if (_mode == Mode.Save) {
      int field = screen.Write(inner.X, y, PathPickerText.FileName, Theme.Muted, inner.Right) + 2;
      _name.Draw(screen, field, y, inner.Right - field, Focused == Area.Name, _defaultName);
      y++;
    }

    (string text, Style style) = Status();
    screen.WriteFit(inner.X, y, inner.Width, text, style);
    _buttons.Draw(screen, inner.X, inner.Bottom - 1, Focused == Area.Buttons, inner.Right);
  }

  /// The line above the buttons: why something could not be done, that the file system is
  /// read, what the list leaves out, or the files that the list shows.
  private (string Text, Style Style) Status() {
    string extensions = string.Join(", ", _extensions);
    if (_error is not null) {
      return (_error, Theme.Bad);
    }

    switch (_problem) {
      case Problem.NotFound:
        return (PathPickerText.NotFound, Theme.Bad);
      case Problem.NotAFolder:
        return (CommonText.NotADirectory, Theme.Bad);
      case Problem.WrongType:
        return (PathPickerText.WrongType(extensions), Theme.Bad);
      case Problem.InvalidName:
        return (PathPickerText.InvalidName, Theme.Bad);
      case Problem.CannotCreate:
        return (PathPickerText.CannotCreate, Theme.Bad);
    }

    if (_loading) {
      return (PathPickerText.Reading, Theme.Muted);
    }

    if (_current is null && _started && _mode != Mode.File) {
      // The roots are no folder: the buttons that need one are disabled, and this is why.
      return (PathPickerText.AtRoots, Theme.Muted);
    }

    return _note switch {
      Note.Unreadable => (PathPickerText.Unreadable, Theme.Warning),
      Note.Capped => (PathPickerText.Capped(MaxEntries), Theme.Warning),
      _ => ShowsFiles && _extensions.Count > 0
          ? (PathPickerText.Files(extensions), Theme.Muted)
          : ("", default),
    };
  }

  private StyledLine Row(Entry entry) =>
      new(TextWidth.Fit(
          entry.Kind == EntryKind.Directory ? entry.Name + _separator : entry.Name,
          Math.Max(1, _listWidth - 2)));

  public override void OnKey(ConsoleKeyInfo key) {
    Start();
    _error = null;
    _problem = Problem.None;
    if (key.Key == ConsoleKey.Tab) {
      Step(Ui.IsBackTab(key) ? -1 : 1);
      return;
    }

    switch (Focused) {
      case Area.Bar:
        BarKey(key);
        return;
      case Area.List:
        ListKey(key);
        return;
      case Area.Name:
        NameKey(key);
        return;
      default:
        ButtonsKey(key);
        return;
    }
  }

  public override void OnTick() => Start();

  private void BarKey(ConsoleKeyInfo key) {
    switch (key.Key) {
      case ConsoleKey.Escape or ConsoleKey.DownArrow:
        Focus(Area.List);
        return;
      case ConsoleKey.Enter:
        Jump(_bar.Value);
        return;
    }

    _bar.OnKey(key);
  }

  private void ListKey(ConsoleKeyInfo key) {
    if (key.Key == ConsoleKey.Escape) {
      Cancel();
      return;
    }

    if (_loading) {
      return;
    }

    switch (key.Key) {
      case ConsoleKey.LeftArrow:
        GoUp();
        return;
      case ConsoleKey.Enter or ConsoleKey.RightArrow when _list.HasCurrent:
        Entry entry = _list.Current;
        if (entry.Kind is EntryKind.Directory or EntryKind.Root) {
          Open(entry.Path, select: null);
        } else if (key.Key == ConsoleKey.Enter) {
          if (entry.Kind == EntryKind.Parent) {
            GoUp();
          } else if (_mode == Mode.Save) {
            _name.Set(entry.Name);
            Focus(Area.Name);
          } else {
            Choose(entry.Path);
          }
        }

        return;
    }

    if (_list.OnKey(key)) {
      return;
    }

    // ↑ on the first row and ↓ on the last leave the list.
    if (key.Key == ConsoleKey.UpArrow) {
      Step(-1);
    } else if (key.Key == ConsoleKey.DownArrow) {
      Step(1);
    }
  }

  private void NameKey(ConsoleKeyInfo key) {
    switch (key.Key) {
      case ConsoleKey.Escape:
        Cancel();
        return;
      case ConsoleKey.Enter:
        Save();
        return;
      case ConsoleKey.UpArrow:
        Step(-1);
        return;
      case ConsoleKey.DownArrow:
        Step(1);
        return;
    }

    _name.OnKey(key);
  }

  private void ButtonsKey(ConsoleKeyInfo key) {
    switch (key.Key) {
      case ConsoleKey.Escape:
        Cancel();
        return;
      case ConsoleKey.UpArrow:
        Step(-1);
        return;
    }

    _buttons.OnKey(key);
  }

  private void Focus(Area area) {
    _focus = Math.Max(0, Array.IndexOf(_areas, area));
    if (Focused == Area.Bar) {
      // What is typed starts from the folder shown.
      _bar.Set(_current ?? "");
    }
  }

  /// To the area after or before, round.
  private void Step(int step) => Focus(_areas[(_focus + step + _areas.Length) % _areas.Length]);

  /// Reads the first folder, once the dialog is shown.
  private void Start() {
    if (_started) {
      return;
    }

    _started = true;
    string start = _start;
    Read(reader => reader.Start(start));
  }

  private void Open(string? directory, string? select) =>
      Read(reader => reader.Open(directory, select));

  /// To the folder that holds the one shown, or to the roots from a root, with the one shown
  /// selected.
  private void GoUp() {
    if (_current is not { } current) {
      return;
    }

    string? parent = Context.FileSystem.Path.GetDirectoryName(current);
    Open(string.IsNullOrEmpty(parent) ? null : parent, current);
  }

  /// Goes where the text of the path bar, or of the file name, leads. name is the file name
  /// to show once it led to a folder; null keeps the one shown.
  private void Jump(string text, string? name = null) {
    string path = text.Trim().Trim('"').Trim();
    if (path.Length == 0) {
      return;
    }

    string? current = _current;
    Read(reader => reader.Jump(path, current, name));
  }

  private void SelectFolder() {
    if (_current is { } current && !_loading) {
      Choose(current);
    }
  }

  private void AskNewFolder() {
    if (_current is not { } parent || _loading) {
      return;
    }

    Context.Show(new PromptDialog(
        PathPickerText.NewFolderTitle, [new PromptField(PathPickerText.Name)],
        values => NewFolder(parent, values[0]), validate: values => NameError(values[0])));
  }

  private void NewFolder(string parent, string name) {
    string path = Context.Workspaces.Normalize(Context.FileSystem.Path.Join(parent, name));
    if (_mode == Mode.NewFolder) {
      // lip creates it later.
      Choose(path);
    } else {
      Read(reader => reader.Create(path));
    }
  }

  /// Chooses the file of the name typed in the folder shown; a name that is a path leads there
  /// first.
  private void Save() {
    string name = _name.Value.Trim();
    IPath path = Context.FileSystem.Path;
    if (name.Length == 0) {
      _error = CommonText.Required;
      return;
    }

    if (name.Contains(path.DirectorySeparatorChar) || name.Contains(path.AltDirectorySeparatorChar)
        || path.IsPathRooted(name)) {
      // The folder is shown, and the name is asked for again.
      Jump(name, _defaultName);
      return;
    }

    if (_current is not { } directory || _loading) {
      return;
    }

    if (NameError(name) is { } error) {
      _error = error;
      return;
    }

    string extension = _extensions.Count > 0 ? _extensions[0] : "";
    if (!name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) {
      name += extension;
    }

    Choose(Context.Workspaces.Normalize(path.Join(directory, name)));
  }

  /// Why a name cannot be that of a file or a folder, or null.
  private string? NameError(string name) {
    if (name.Length == 0) {
      return CommonText.Required;
    }

    return name is "." or ".."
        || name.IndexOfAny(Context.FileSystem.Path.GetInvalidFileNameChars()) >= 0
        ? PathPickerText.InvalidName
        : null;
  }

  private void Choose(string path) {
    if (_validate?.Invoke(path) is { } error) {
      _error = error;
      return;
    }

    _generation++;
    Close();
    _chosen(path);
  }

  private void Cancel() {
    _generation++;
    Close();
    _cancel?.Invoke();
  }

  /// Reads the file system in the background, then shows what it came to unless something
  /// else was asked meanwhile, or the dialog was closed.
  private void Read(Func<Reader, Outcome> work) {
    int generation = ++_generation;
    _loading = true;
    Reader reader = new(
        Context.FileSystem, Context.Workspaces, _mode, ShowsFiles, _extensions);
    Context.Background(
        () => Task.FromResult(work(reader)),
        outcome => {
          if (generation == _generation) {
            Show(outcome);
          }
        },
        exception => {
          if (generation == _generation) {
            _loading = false;
            _error = exception.Message;
          }
        },
        anyWorkspace: true);
  }

  private void Show(Outcome outcome) {
    _loading = false;
    if (outcome.Problem != Problem.None) {
      _problem = outcome.Problem;
      return;
    }

    if (outcome.Choose is { } path) {
      Choose(path);
      return;
    }

    _current = outcome.Directory;
    _note = outcome.Note;
    _list.Items = outcome.Entries;
    _list.Selected = 0;
    if (outcome.Select is { } select) {
      _list.Select(entry => entry.Kind != EntryKind.Parent
          && WorkspaceList.PathComparer.Equals(entry.Path, select));
    }

    if (Focused != Area.Buttons) {
      // The first button again, which the roots do not have.
      _buttons.Selected = 0;
    }

    if (outcome.Name is { } name) {
      _name.Set(name);
      Focus(Area.Name);
    } else if (outcome.Typed is { } typed) {
      _focus = Array.IndexOf(_areas, Area.Bar);
      _bar.Set(typed);
    } else if (Focused == Area.Bar) {
      Focus(Area.List);
    }
  }

  /// The text in at most width columns, with an ellipsis in place of its middle when it is
  /// longer: the start of a path and its end say the most.
  private static string FitMiddle(string text, int width) {
    if (TextWidth.Of(text) <= width) {
      return text;
    }

    if (width <= 1) {
      return width == 1 ? "…" : "";
    }

    List<Rune> runes = [.. text.EnumerateRunes()];
    int head = (width - 1) / 3;
    int tail = width - 1 - head;
    int to = 0;
    for (int used = 0; to < runes.Count; to++) {
      used += TextWidth.Of(runes[to]);
      if (used > head) {
        break;
      }
    }

    int from = runes.Count;
    for (int used = 0; from > to; from--) {
      used += TextWidth.Of(runes[from - 1]);
      if (used > tail) {
        break;
      }
    }

    StringBuilder builder = new();
    foreach (Rune rune in runes.Take(to)) {
      builder.Append(rune.ToString());
    }

    builder.Append('…');
    foreach (Rune rune in runes.Skip(from)) {
      builder.Append(rune.ToString());
    }

    return builder.ToString();
  }

  /// Reads the file system for the picker, off the thread of the interface.
  private sealed class Reader(
      IFileSystem fileSystem,
      WorkspaceList paths,
      Mode mode,
      bool files,
      IReadOnlyList<string> extensions) {
    /// The nearest folder that exists, from start upwards; the roots when there is none.
    public Outcome Start(string start) {
      string? directory;
      try {
        directory = paths.Normalize(start);
      }
      catch (Exception ex) when (IsPathFailure(ex)) {
        directory = null;
      }

      string? wanted = directory;
      while (directory is not null && !fileSystem.Directory.Exists(directory)) {
        string? parent = fileSystem.Path.GetDirectoryName(directory);
        directory = string.IsNullOrEmpty(parent) ? null : parent;
      }

      Outcome shown = Open(directory, select: null);
      // A folder that lip creates later is not lost for not being there yet.
      return mode == Mode.NewFolder && wanted is not null && wanted != directory
          ? shown with { Typed = wanted }
          : shown;
    }

    /// The entries of a folder, or the roots when directory is null: "..", the folders, then
    /// the files, each sorted by name once.
    public Outcome Open(string? directory, string? select, string? name = null) {
      if (directory is null) {
        return new Outcome { Entries = Roots(), Select = select };
      }

      List<Entry> folders = [];
      List<Entry> listed = [];
      Note note = Note.None;
      try {
        foreach (IFileSystemInfo info in
            fileSystem.DirectoryInfo.New(directory).EnumerateFileSystemInfos()) {
          FileAttributes attributes = info.Attributes;
          if (info.Name.StartsWith('.')
              || (attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) {
            continue;
          }

          // The folders and the files are capped apart, so that neither crowds the other
          // out; that some are left out is said only when some are.
          bool folder = attributes.HasFlag(FileAttributes.Directory);
          if (!folder && !(files && Matches(info.Name))) {
            continue;
          }

          List<Entry> entries = folder ? folders : listed;
          if (entries.Count >= MaxEntries) {
            note = Note.Capped;
          } else {
            entries.Add(new Entry(
                folder ? EntryKind.Directory : EntryKind.File, info.Name, info.FullName));
          }
        }
      }
      catch (Exception ex) when (IsPathFailure(ex)) {
        folders.Clear();
        listed.Clear();
        note = Note.Unreadable;
      }

      Comparison<Entry> byName =
          (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name);
      folders.Sort(byName);
      listed.Sort(byName);
      return new Outcome {
        Directory = directory,
        Entries = [new Entry(EntryKind.Parent, "..", ""), .. folders, .. listed],
        Select = select,
        Note = note,
        Name = name,
      };
    }

    /// Where a path that was typed leads: a folder is shown, with the file name when one is
    /// given, and what else it may be depends on what the picker is for.
    public Outcome Jump(string text, string? current, string? folderName) {
      string full;
      try {
        if (!fileSystem.Path.IsPathFullyQualified(text)) {
          if (current is null) {
            return new Outcome { Problem = Problem.NotFound };
          }

          // As the platform resolves it: a path that starts with a separator or a drive is
          // not below the folder shown.
          text = fileSystem.Path.GetFullPath(text, current);
        }

        full = paths.Normalize(text);
      }
      catch (Exception ex) when (IsPathFailure(ex)) {
        return new Outcome { Problem = Problem.NotFound };
      }

      try {
        if (fileSystem.Directory.Exists(full)) {
          return Open(full, select: null, folderName);
        }

        string? parent = fileSystem.Path.GetDirectoryName(full);
        string name = fileSystem.Path.GetFileName(full);
        if (fileSystem.File.Exists(full)) {
          return mode switch {
            Mode.File or Mode.FileOrFolder => Matches(name)
                ? new Outcome { Choose = full }
                : new Outcome { Problem = Problem.WrongType },
            Mode.Save => Open(parent, select: full, name),
            _ => new Outcome { Problem = Problem.NotAFolder },
          };
        }

        return mode switch {
          Mode.NewFolder => new Outcome { Problem = Creatable(full), Choose = full },
          Mode.Save when !string.IsNullOrEmpty(parent) && fileSystem.Directory.Exists(parent) =>
              Open(parent, select: null, name),
          _ => new Outcome { Problem = Problem.NotFound },
        };
      }
      catch (Exception ex) when (IsPathFailure(ex)) {
        return new Outcome { Problem = Problem.NotFound };
      }
    }

    /// Why a folder that does not exist cannot be created later, as far as its path says: a
    /// name that is none, a file in its way, or nothing above it that exists.
    private Problem Creatable(string path) {
      char[] invalid = fileSystem.Path.GetInvalidFileNameChars();
      string? at = path;
      while (!string.IsNullOrEmpty(at) && !fileSystem.Directory.Exists(at)) {
        if (fileSystem.File.Exists(at)) {
          return Problem.NotAFolder;
        }

        string name = fileSystem.Path.GetFileName(at);
        if (name.Length == 0 || name.IndexOfAny(invalid) >= 0) {
          return Problem.InvalidName;
        }

        at = fileSystem.Path.GetDirectoryName(at);
      }

      return string.IsNullOrEmpty(at) ? Problem.NotFound : Problem.None;
    }

    /// Creates a folder and shows it.
    public Outcome Create(string path) {
      try {
        fileSystem.Directory.CreateDirectory(path);
      }
      catch (Exception ex) when (IsPathFailure(ex)) {
        return new Outcome { Problem = Problem.CannotCreate };
      }

      return Open(path, select: null);
    }

    private List<Entry> Roots() {
      if (fileSystem.Path.DirectorySeparatorChar != '\\') {
        return [new Entry(EntryKind.Root, "/", "/")];
      }

      try {
        List<Entry> roots = [
          .. fileSystem.DriveInfo.GetDrives()
              .Select(drive => new Entry(EntryKind.Root, drive.Name, drive.Name)),
        ];
        roots.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
        return roots;
      }
      catch (Exception ex) when (IsPathFailure(ex)) {
        return [];
      }
    }

    private bool Matches(string name) =>
        extensions.Count == 0
        || extensions.Any(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    /// Whether the exception is one of a path that cannot be read, or that is none.
    private static bool IsPathFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or System.Security.SecurityException;
  }
}
