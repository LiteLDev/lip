using System.Text.Json;
using System.Text.Json.Nodes;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Widgets;
using Lip.Core.Config;
using Lip.Core.PublicApi;

namespace Lip.Cli.Tui.Pages.ModConfig;

/// The Mod config page: the configuration files of the installed mods (protocol/lip-tui.md,
/// "Configuration"). The files are listed at the left; the selected one is shown at the right.
/// A JSON file is edited as a tree, in memory, and written only when the user saves it; any
/// other file, and a JSON file that cannot be read, is only shown. The files are edited and
/// saved while the server runs too; the page then says that a change may need a restart. The
/// server and the mods write these files themselves: a file that changed on the disk since
/// it was read is saved only once the user said to overwrite it.
public sealed class ModConfigPage : Page {
  /// What saving a file came to.
  private enum Saved {
    Written,
    Gone,
    Changed,
  }

  /// The narrowest area that shows the files and the open file side by side; in a narrower one
  /// the page shows the one that has the focus.
  private const int WideWidth = 72;

  /// The columns of the list of files, as last drawn.
  private int _fileColumns = int.MaxValue / 2;

  /// A row of the list of files: a configuration file of a mod, or a mod that has none.
  private sealed record FileItem(string Mod, string? Path, string Name) {
    public string Label => Path is null ? Mod : $"{Mod} · {Name}";
  }

  private enum Area {
    Files,
    Document,
    Buttons,
  }

  private readonly ListView<FileItem> _files;
  private readonly ListView<TreeRow> _rows;
  private readonly TextViewer _viewer = new();
  private readonly ButtonRow _buttons;
  private readonly ButtonRow _getMods;
  private Area _focus;
  private bool _loaded;
  private string? _error;
  private string _workspace;
  // What tells the answer of the newest reading of the list, and of a file, from an older one.
  private int _load;
  private int _read;
  // The file that is shown or being read, and its text when it was read.
  private FileItem? _open;
  private string? _text;
  // What is shown of the file: a tree, or a note above its text, or a line in place of it.
  private JsonTree? _tree;
  private Func<string>? _note;
  private Func<string>? _message;
  // Whether the file that is read again takes the focus once it is shown.
  private bool _refocus;
  // The columns of the names and of the values of the tree, as last drawn.
  private int _nameWidth = 12;
  private int _valueWidth = 40;

  public ModConfigPage(AppContext context) : base(context) {
    _workspace = context.Workspace;
    _files = new ListView<FileItem>(FileRow);
    _rows = new ListView<TreeRow>(Row);
    _buttons = new ButtonRow(
        new Button(() => CommonText.Save, () => Save()) { Enabled = () => Dirty },
        new Button(() => CommonText.Discard, AskDiscard) { Enabled = () => Dirty });
    _getMods = new ButtonRow(
        new Button(() => CommonText.GetMods, () => Context.Navigate(PageId.GetMods)));
  }

  public override string Title => ModConfigText.Title;

  public override bool CanLeave => !Dirty;

  private bool Dirty => _tree?.Dirty == true;

  /// Whether the list was read and names no mod: none is installed, or none that keeps files
  /// in a directory of its own.
  private bool NoMods => _loaded && _error is null && _files.Items.Count == 0;

  /// Whether the open file has something that takes keys: a tree, or text that scrolls.
  private bool DocumentFocusable => _tree is not null || _note is not null;

  public override IReadOnlyList<Hint> Hints {
    get {
      Settle();
      if (NoMods) {
        return [CommonText.HintSelect];
      }

      // Tab goes on wherever the page has more than the list of files to go to.
      switch (_focus) {
        case Area.Files:
          return DocumentFocusable
              ? [.. _files.Hints, CommonText.HintOpen, CommonText.HintNext, .. _files.PageHints]
              : [.. _files.Hints, .. _files.PageHints];
        case Area.Buttons:
          // ↑ goes back to the file.
          return [.. _buttons.Hints, CommonText.HintNext, CommonText.HintMove];
        default:
          if (_tree is null) {
            return _viewer.Scrollable
                ? [
                  CommonText.HintScroll, CommonText.HintNext, CommonText.HintPage,
                  CommonText.HintEnds,
                ]
                : [CommonText.HintNext];
          }

          List<Hint> hints = [CommonText.HintMove];
          if (_rows.HasCurrent) {
            TreeRow row = _rows.Current;
            if (row.Kind == TreeRowKind.Add) {
              hints.Add(CommonText.HintSelect);
            } else if (row.ReadOnly) {
              // Nothing changes the row.
            } else if (row.Kind == TreeRowKind.Element) {
              hints.Add(CommonText.HintActions);
              if (row.IsBoolean) {
                hints.Add(CommonText.HintToggle);
              }
            } else if (row.IsContainer) {
              hints.Add(CommonText.HintOpen);
            } else if (row.IsBoolean) {
              hints.Add(CommonText.HintToggleEither);
            } else {
              hints.Add(CommonText.HintEdit);
            }
          }

          hints.Add(CommonText.HintNext);
          hints.AddRange(_rows.PageHints);
          return hints;
      }
    }
  }

  public override void OnShown() {
    // An edited file stays as it is; anything else is read again, since the server and the
    // mods write these files.
    if (!Dirty) {
      Load();
    }
  }

  public override void OnWorkspaceChanged() {
    if (Context.Workspaces.Same(_workspace, Context.Workspace)) {
      // An operation changed the workspace: the files are listed again, and the one shown
      // stays shown.
      if (!Dirty && Context.CurrentPage == PageId.ModConfig) {
        Load();
      }

      return;
    }

    _workspace = Context.Workspace;
    _load++;
    _read++;
    _files.Items = [];
    _loaded = false;
    _error = null;
    _open = null;
    _text = null;
    ClearDocument();
    _focus = Area.Files;
    if (Context.CurrentPage == PageId.ModConfig) {
      Load();
    }
  }

  public override void ConfirmLeave(Action leave) => AskUnsaved(leave);

  /// Tab from the sidebar goes to the files, and Shift+Tab to the last area that takes keys.
  public override void OnTabIn(bool backward) {
    _focus = !backward ? Area.Files : Dirty ? Area.Buttons : Area.Document;
    Settle();
  }

  #region Reading

  /// Reads the list of the files in the background.
  private void Load() {
    int load = ++_load;
    LipClient client = Context.Client;
    Context.Background(
        () => client.ListModConfigs(),
        mods => {
          if (load == _load) {
            Loaded([.. mods.SelectMany(Items)], null);
          }
        },
        exception => {
          if (load == _load) {
            Loaded([], exception.Message);
          }
        });
  }

  /// The rows of a mod: one for each of its files, or one for the mod when it has none.
  private static IEnumerable<FileItem> Items(Lip.Core.Config.ModConfig mod) {
    if (mod.Files.Count == 0) {
      return [new FileItem(mod.ModName, null, "")];
    }

    string prefix = $"mods/{mod.ModName}/";
    return mod.Files.Select(file => new FileItem(
        mod.ModName, file,
        file.StartsWith(prefix, StringComparison.Ordinal) ? file[prefix.Length..] : file));
  }

  private void Loaded(IReadOnlyList<FileItem> items, string? error) {
    if (error is not null && Dirty) {
      // The file that is edited stays reachable, so that it can be saved.
      return;
    }

    FileItem? open = _open;
    _files.Items = items;
    _error = error;
    _loaded = true;
    if (open is not null && _files.Select(item => item == open)) {
      // The file that was shown stays shown, with what it holds now unless it is edited.
      if (!Dirty) {
        Open(_files.Current, keep: true);
      }

      return;
    }

    if (Dirty) {
      return;
    }

    if (!_files.Select(item => item.Path is not null)) {
      _files.Selected = 0;
    }

    Open(_files.HasCurrent ? _files.Current : null);
  }

  /// Shows a file, reading it in the background. With keep, what is shown of it stays until
  /// its text is read, and after that when the text is the same.
  private void Open(FileItem? item, bool keep = false) {
    int read = ++_read;
    if (item != _open) {
      _refocus = false;
    }

    _open = item;
    if (item?.Path is not { } path) {
      _text = null;
      ClearDocument();
      _message = item is null ? null : () => ModConfigText.NoFile;
      return;
    }

    if (!keep) {
      _text = null;
      ClearDocument();
      _message = () => CommonText.Loading;
    }

    LipClient client = Context.Client;
    Context.Background(
        () => client.ReadConfigFile(path),
        text => {
          // What is shown stays when it is edited, when a dialog about it is open, and when
          // the file holds what it held.
          if (read != _read || Dirty
              || (keep && (!Context.Idle || (text is not null && text == _text)))) {
            return;
          }

          Show(path, text);
        },
        exception => {
          if (read == _read && !Dirty) {
            _refocus = false;
            _text = null;
            ClearDocument();
            _message = () => $"{ModConfigText.CannotRead}: {exception.Message}";
          }
        });
  }

  /// Shows the text of the file at path: a JSON file as a tree, anything else as it is.
  private void Show(string path, string? text) {
    ClearDocument();
    _text = text;
    bool refocus = _refocus;
    _refocus = false;
    if (text is null) {
      _message = () => ModConfigText.Gone;
      return;
    }

    if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) {
      try {
        _tree = new JsonTree(JsonConfigFile.Parse(text));
        _rows.Items = _tree.Rows();
        _rows.Selected = 0;
        if (refocus) {
          _focus = Area.Document;
        }

        return;
      }
      catch (JsonException exception) {
        // What the JSON reader says, which names the line, follows in English.
        string why = exception.Message;
        _note = () => $"{ModConfigText.InvalidJson} {why}";
      }
    } else {
      _note = () => ModConfigText.ReadOnly;
    }

    _viewer.SetText(text.TrimStart('﻿'));
    _viewer.ScrollToStart();
  }

  private void ClearDocument() {
    _tree = null;
    _note = null;
    _message = null;
    _rows.Items = [];
    _viewer.Lines = [];
  }

  #endregion

  #region Saving

  /// Writes the open file when it has changes, then calls then; a failure is shown, and then
  /// is not called. A file that does not hold what was read of it any more, because the
  /// server or a mod wrote it meanwhile, is written only with overwrite: the user is asked
  /// whether to overwrite it, to read it again, or to do neither.
  private void Save(Action? then = null, bool overwrite = false) {
    if (_tree is not { Dirty: true } tree || _open?.Path is not { } path) {
      then?.Invoke();
      return;
    }

    string text = tree.Serialize();
    string title = ModConfigText.SaveTitle(_open.Name);
    string? source = _text;
    LipClient client = Context.Client;
    Context.RunOperation(
        title,
        async _ => {
          // A configuration file that does not exist is not created.
          if (await client.ReadConfigFile(path) is not { } now) {
            return Saved.Gone;
          }

          if (!overwrite && now != source) {
            return Saved.Changed;
          }

          await client.WriteConfigFile(path, text);
          return Saved.Written;
        },
        saved => {
          if (saved == Saved.Gone) {
            Context.Message(title, ModConfigText.Gone, Theme.Bad);
            return;
          }

          if (saved == Saved.Changed) {
            // Esc presses the last button, which changes nothing, and so does Enter at first.
            Context.Show(new ConfirmDialog(
                title,
                [
                  new StyledLine(CommonText.ChangedOnDisk),
                  new StyledLine(ModConfigText.ReloadDetail, Theme.Muted),
                ],
                [CommonText.Overwrite, ModConfigText.Reload, CommonText.Cancel],
                choice => {
                  if (choice == 0) {
                    Save(then, overwrite: true);
                  } else if (choice == 1 && ReferenceEquals(tree, _tree)) {
                    // The file is read again, which drops the changes. A user who was on
                    // their way elsewhere goes on, as after Discard; one who saved with the
                    // button is shown the file where they were.
                    _refocus = then is null;
                    Discard();
                    then?.Invoke();
                  }
                }) { Selected = 2 });
            return;
          }

          tree.MarkSaved(text);
          if (ReferenceEquals(tree, _tree)) {
            _text = text;
          }

          Context.Toast(
              Context.ServerRunning ? ModConfigText.SavedWhileRunning : CommonText.Saved,
              ToastKind.Good);
          then?.Invoke();
        });
  }

  /// Drops the changes of the open file: it is read again.
  private void Discard() => Open(_open);

  private void AskDiscard() =>
      Context.Confirm(
          CommonText.DiscardTitle, CommonText.DiscardQuestion, CommonText.Discard, Discard);

  /// Asks whether to save or discard the changes of the open file, and calls then once they
  /// are saved or discarded.
  private void AskUnsaved(Action then) {
    if (!Dirty) {
      then();
      return;
    }

    Context.Show(new ConfirmDialog(
        CommonText.UnsavedTitle,
        [
          new StyledLine(_open?.Label ?? "", Theme.Strong),
          new StyledLine(CommonText.UnsavedQuestion),
        ],
        [CommonText.Save, CommonText.Discard, CommonText.Cancel],
        choice => {
          switch (choice) {
            case 0:
              Save(then);
              break;
            case 1:
              Discard();
              then();
              break;
          }
        }));
  }

  #endregion

  #region Keys

  /// The focus is on something that takes keys.
  private void Settle() {
    if (_focus == Area.Buttons && !Dirty) {
      _focus = Area.Document;
    }

    if (_focus == Area.Document && !DocumentFocusable) {
      _focus = Area.Files;
    }
  }

  public override bool OnKey(ConsoleKeyInfo key) {
    Settle();
    if (NoMods) {
      return key.Key is ConsoleKey.Enter or ConsoleKey.Spacebar && _getMods.OnKey(key);
    }

    if (!_loaded || _error is not null) {
      return false;
    }

    if (key.Key == ConsoleKey.Tab) {
      return Ui.IsBackTab(key) ? FocusPrevious() : FocusNext();
    }

    return _focus switch {
      Area.Files => FilesKey(key),
      Area.Buttons => ButtonsKey(key),
      _ => _tree is { } tree ? TreeKey(tree, key) : ViewerKey(key),
    };
  }

  private bool FocusNext() {
    if (_focus == Area.Files && DocumentFocusable) {
      _focus = Area.Document;
      return true;
    }

    if (_focus == Area.Document && Dirty) {
      _focus = Area.Buttons;
      return true;
    }

    return false;
  }

  private bool FocusPrevious() {
    if (_focus == Area.Files) {
      return false;
    }

    _focus = _focus == Area.Buttons ? Area.Document : Area.Files;
    return true;
  }

  private bool FilesKey(ConsoleKeyInfo key) {
    if (key.Key is ConsoleKey.Enter or ConsoleKey.RightArrow) {
      if (DocumentFocusable) {
        _focus = Area.Document;
      }

      return true;
    }

    int before = _files.Selected;
    if (!_files.OnKey(key)) {
      return false;
    }

    int after = _files.Selected;
    if (after == before) {
      return true;
    }

    if (Dirty) {
      // The edited file stays selected until the user says what becomes of its changes.
      _files.Selected = before;
      AskUnsaved(() => {
        _files.Selected = after;
        Open(_files.HasCurrent ? _files.Current : null);
      });
    } else {
      Open(_files.Current);
    }

    return true;
  }

  private bool TreeKey(JsonTree tree, ConsoleKeyInfo key) {
    switch (key.Key) {
      case ConsoleKey.Escape or ConsoleKey.LeftArrow:
        if (tree.Up() is { } selected) {
          _rows.Items = tree.Rows();
          _rows.Selected = selected;
        } else {
          _focus = Area.Files;
        }

        return true;
      case ConsoleKey.Enter:
        if (_rows.HasCurrent) {
          Activate(tree, _rows.Current);
        }

        return true;
      case ConsoleKey.Spacebar:
        if (_rows.HasCurrent && _rows.Current is { IsBoolean: true, ReadOnly: false } row) {
          tree.Toggle(row);
          _rows.Items = tree.Rows();
        }

        return true;
    }

    if (_rows.OnKey(key)) {
      return true;
    }

    if (key.Key == ConsoleKey.DownArrow && Dirty) {
      _focus = Area.Buttons;
      return true;
    }

    return false;
  }

  private bool ViewerKey(ConsoleKeyInfo key) {
    if (key.Key is ConsoleKey.Escape or ConsoleKey.LeftArrow) {
      _focus = Area.Files;
      return true;
    }

    return _viewer.OnKey(key);
  }

  private bool ButtonsKey(ConsoleKeyInfo key) {
    if (_buttons.OnKey(key)) {
      return true;
    }

    switch (key.Key) {
      case ConsoleKey.UpArrow or ConsoleKey.Escape:
        _focus = Area.Document;
        return true;
      case ConsoleKey.LeftArrow:
        _focus = Area.Files;
        return true;
      default:
        return false;
    }
  }

  /// What Enter does on a row of the tree.
  private void Activate(JsonTree tree, TreeRow row) {
    if (row.Kind == TreeRowKind.Add) {
      Context.Show(new ValueDialog(
          ModConfigText.AddTitle, ModConfigText.Value, "", "42",
          text => {
            if (ReferenceEquals(tree, _tree)) {
              int added = tree.Add(JsonConfigFile.ParseValue(text));
              _rows.Items = tree.Rows();
              _rows.Selected = Math.Max(0, added);
            }
          },
          text => text.Trim().Length == 0 ? CommonText.Required : null));
    } else if (row.ReadOnly) {
      // The details area says why.
    } else if (row.Kind == TreeRowKind.Element) {
      Context.ShowMenu(
          row.Name,
          new MenuItem(
              row.IsContainer ? ModConfigText.Open
                  : row.IsBoolean ? ModConfigText.Toggle
                  : ModConfigText.Edit,
              () => Change(tree, row)),
          new MenuItem(ModConfigText.Remove, () => {
            if (ReferenceEquals(tree, _tree)) {
              tree.Remove(row);
              _rows.Items = tree.Rows();
            }
          }));
    } else {
      Change(tree, row);
    }
  }

  /// Opens the object or the array of a row, switches its boolean, or asks for its value.
  private void Change(JsonTree tree, TreeRow row) {
    if (!ReferenceEquals(tree, _tree)) {
      return;
    }

    if (row.IsContainer) {
      tree.Open(row, _rows.Selected);
      _rows.Items = tree.Rows();
      _rows.Selected = 0;
      return;
    }

    if (row.IsBoolean) {
      tree.Toggle(row);
      _rows.Items = tree.Rows();
      return;
    }

    JsonNode? current = row.Value;
    JsonValueKind kind = current?.GetValueKind() ?? JsonValueKind.Null;
    string before = JsonConfigFile.EditText(current);
    Context.Show(new ValueDialog(
        row.Kind == TreeRowKind.Value ? ModConfigText.Value : row.Name, KindLabel(kind), before,
        "",
        text => {
          if (ReferenceEquals(tree, _tree) && text != before
              && JsonConfigFile.TryEdit(current, text, out JsonNode? edited, out _)) {
            tree.Set(row, edited);
            _rows.Items = tree.Rows();
          }
        },
        text =>
            JsonConfigFile.TryEdit(current, text, out _, out _) ? null
                : kind == JsonValueKind.Number ? ModConfigText.NotNumber
                : ModConfigText.NotScalar));
  }

  private static string KindLabel(JsonValueKind kind) => kind switch {
    JsonValueKind.String => ModConfigText.Text,
    JsonValueKind.Number => ModConfigText.Number,
    JsonValueKind.True or JsonValueKind.False => ModConfigText.Boolean,
    _ => ModConfigText.Value,
  };

  #endregion

  #region Drawing

  public override void Draw(ScreenBuffer screen, Rect area, bool focused) {
    Settle();
    if (area.Width < 1 || area.Height < 1) {
      return;
    }

    if (_error is { } error) {
      List<string> lines = TextWidth.Wrap($"{ModConfigText.CannotList}: {error}", area.Width);
      for (int i = 0; i < lines.Count && i < area.Height; i++) {
        screen.Write(area.X, area.Y + i, lines[i], Theme.Bad, area.Right);
      }

      return;
    }

    if (!_loaded) {
      screen.WriteFit(area.X, area.Y, area.Width, CommonText.Loading, Theme.Muted);
      return;
    }

    if (NoMods) {
      screen.WriteFit(area.X, area.Y, area.Width, ModConfigText.NoMods, Theme.Muted);
      _getMods.Draw(screen, area.X, area.Y + 2, focused, area.Right);
      return;
    }

    // While the server runs, the last row says that it may not see a change until it restarts.
    if (Context.ServerRunning && area.Height > 3) {
      screen.WriteFit(
          area.X, area.Bottom - 1, area.Width, ModConfigText.MayNeedRestart, Theme.Warning);
      area = area.Shrink(bottom: 1);
    }

    bool onFiles = _focus == Area.Files;
    if (area.Width >= WideWidth) {
      // The widest label, the mark of a file with changes, and the border of the box.
      int widest = _files.Items.Max(item => TextWidth.Of(item.Label));
      (Rect left, Rect right) = Panes.Columns(
          area, Math.Clamp(widest + 6, 24, area.Width * 2 / 5));
      DrawFiles(
          screen, Ui.Panel(screen, left, ModConfigText.Files, focused && onFiles),
          focused && onFiles);
      DrawDocument(
          screen, Ui.Panel(screen, right, _open?.Mod ?? "", focused && !onFiles),
          focused && !onFiles, _open?.Name ?? "");
    } else if (!onFiles) {
      DrawDocument(screen, area, focused, _open?.Label ?? "");
    } else if (_message is { } message && area.Height > 2) {
      // There is no room for the file beside the list: what stands in place of it is said
      // below the list.
      DrawFiles(screen, area.Shrink(bottom: 2), focused);
      screen.WriteFit(area.X, area.Bottom - 1, area.Width, message(), Theme.Muted);
    } else {
      DrawFiles(screen, area, focused);
    }
  }

  /// Draws the list of files in rect, whose rows are fitted to its width.
  private void DrawFiles(ScreenBuffer screen, Rect rect, bool focused) {
    _fileColumns = rect.Width;
    _files.Draw(screen, rect, focused);
  }

  /// Draws the open file in rect: head, the name of the file, starts the line that says where
  /// in the file the rows are.
  private void DrawDocument(ScreenBuffer screen, Rect rect, bool focused, string head) {
    if (rect.Width < 1 || rect.Height < 1) {
      return;
    }

    if (_message is { } message) {
      List<string> lines = TextWidth.Wrap(message(), rect.Width);
      for (int i = 0; i < lines.Count && i < rect.Height; i++) {
        screen.Write(rect.X, rect.Y + i, lines[i], Theme.Muted, rect.Right);
      }

      return;
    }

    if (_tree is not { } tree) {
      screen.WriteFit(rect.X, rect.Y, rect.Width, head, Theme.Strong);
      List<string> note = TextWidth.Wrap(_note?.Invoke() ?? "", rect.Width);
      int rows = Math.Min(note.Count, 2);
      for (int i = 0; i < rows && i + 1 < rect.Height; i++) {
        screen.WriteFit(rect.X, rect.Y + 1 + i, rect.Width, note[i], Theme.Warning);
      }

      _viewer.Draw(screen, rect.Shrink(top: rows + 2));
      return;
    }

    // Where the rows are, and whether the file has changes.
    string unsaved = tree.Dirty ? ModConfigText.Unsaved : "";
    int room = rect.Width - (unsaved.Length > 0 ? TextWidth.Of(unsaved) + 2 : 0);
    screen.WriteFit(rect.X, rect.Y, room, Location(head, tree.Location, room), Theme.Strong);
    if (room > 0) {
      screen.Write(
          rect.Right - TextWidth.Of(unsaved), rect.Y, unsaved, Theme.Warning, rect.Right);
    }

    // Below the rows: a line about the selected one, and the buttons.
    Rect list = rect.Shrink(top: 1, bottom: 3);
    IReadOnlyList<TreeRow> items = _rows.Items;
    _nameWidth = Math.Clamp(
        items.Count == 0 ? 0 : items.Max(row => TextWidth.Of(row.Name)),
        0, Math.Max(6, list.Width * 2 / 5));
    _valueWidth = Math.Max(1, list.Width - (_nameWidth > 0 ? _nameWidth + 2 : 0));
    _rows.Draw(screen, list, focused && _focus == Area.Document);

    int y = rect.Bottom - 3;
    if (y > rect.Y) {
      screen.Write(rect.X, y, new string('─', rect.Width), Theme.Border);
      screen.WriteFit(
          rect.X, y + 1, rect.Width, _rows.HasCurrent ? Detail(_rows.Current) : "", Theme.Muted);
    }

    if (rect.Height > 1) {
      _buttons.Draw(
          screen, rect.X, rect.Bottom - 1, focused && _focus == Area.Buttons, rect.Right);
    }
  }

  /// The name of the file and the levels below it down to the one shown, without the first of
  /// them when they do not fit width columns.
  private static string Location(string head, IReadOnlyList<string> levels, int width) {
    const string Separator = " › ";
    string text = string.Join(Separator, [head, .. levels]);
    for (int skip = 0; skip < levels.Count && TextWidth.Of(text) > width; skip++) {
      text = "…" + Separator + string.Join(Separator, levels.Skip(skip));
    }

    return text;
  }

  /// A row of the list of files, marked while the file has changes that are not saved. The
  /// mark is never cut off: the label is cut to the room that is left. The last column is that
  /// of the marks of the rows that are hidden.
  private StyledLine FileRow(FileItem item) {
    const string Mark = " ●";
    bool dirty = Dirty && item == _open;
    int room = Math.Max(1, _fileColumns - 1 - (dirty ? TextWidth.Of(Mark) : 0));
    StyledLine line = new(FitLabel(item, room), item.Path is null ? Theme.Muted : default);
    return dirty ? line.Add(Mark, Theme.Warning) : line;
  }

  /// The label of a row of the list of files in at most width columns. One that is longer
  /// keeps the mod, and the end of the name of the file behind an ellipsis, as in
  /// "LeviOptimize · …g/config.json"; where the mod leaves the name too little room, the
  /// label is cut in its middle.
  private static string FitLabel(FileItem item, int width) {
    // The least of the name of a file that still tells files apart.
    const int LeastName = 8;
    if (item.Path is null || TextWidth.Of(item.Label) <= width) {
      return PackageCells.FitMiddle(item.Label, width);
    }

    string mod = item.Label[..^item.Name.Length];
    int room = width - TextWidth.Of(mod);
    return room >= Math.Min(LeastName, TextWidth.Of(item.Name))
        ? mod + TextWidth.FitEnd(item.Name, room)
        : PackageCells.FitMiddle(item.Label, width);
  }

  /// A row of the tree: its name, in a column, and its value.
  private StyledLine Row(TreeRow row) {
    if (row.Kind == TreeRowKind.Add) {
      return new StyledLine("+ " + ModConfigText.AddValue, Theme.Accent);
    }

    StyledLine line = new();
    if (_nameWidth > 0) {
      string name = TextWidth.Fit(row.Name, _nameWidth);
      line.Add(
          name + new string(' ', _nameWidth - TextWidth.Of(name) + 2),
          row.ReadOnly ? Theme.Muted : default);
    }

    (string text, Style style) = row.Value switch {
      JsonObject o => ($"{{…}} {ModConfigText.Items(o.Count)}", Theme.Accent),
      JsonArray a => ($"[…] {ModConfigText.Items(a.Count)}", Theme.Accent),
      null => ("null", Theme.Muted),
      JsonNode value => value.GetValueKind() switch {
        JsonValueKind.String => (JsonConfigFile.Serialize(value).TrimEnd(), default),
        JsonValueKind.True => ("[x] true", Theme.Good),
        JsonValueKind.False => ("[ ] false", default),
        _ => (value.ToJsonString(), Theme.Strong),
      },
    };
    return line.Add(TextWidth.Fit(text, _valueWidth), row.ReadOnly ? Theme.Muted : style);
  }

  /// One sentence about a row, for the line below the rows.
  private static string Detail(TreeRow row) {
    if (row.Kind == TreeRowKind.Add) {
      return ModConfigText.AddDetail;
    }

    if (row.ReadOnly) {
      return ModConfigText.VersionDetail;
    }

    return row.Value switch {
      JsonObject or JsonArray => "",
      null => ModConfigText.Null,
      JsonNode value => KindLabel(value.GetValueKind()),
    };
  }

  #endregion
}
