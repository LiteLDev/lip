using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Widgets;
using Lip.Core.Config;
using Lip.Core.PublicApi;

namespace Lip.Cli.Tui.Pages.GameConfig;

/// The Game config page: server.properties, allowlist.json and permissions.json of the selected
/// workspace (protocol/lip-tui.md, "Configuration"), as three sections picked from a list at
/// the left. The files are read when the page is shown, changed in memory, and written only
/// when the user saves.
public sealed partial class GameConfigPage : Page {
  /// The areas of the page that take the focus.
  private enum Area {
    Sections,
    Entries,
    Buttons,
  }

  private const int PropertiesSection = 0;
  private const int AllowlistSection = 1;
  private const int PermissionsSection = 2;
  private const string ChangedMark = "*";

  private readonly ListView<int> _sections;
  // The rows of each section: the index of a property or of a player, AddRow for "Add player…".
  private readonly ListView<int>[] _lists;
  private readonly TextViewer _viewer = new();
  private readonly ButtonRow _buttons;
  // The files of the sections, in their order; null until they are read.
  private GameFile[]? _files;
  // The workspace that the files were read from.
  private string _workspace = "";
  private int _generation;
  private Area _area;
  // The area that the focus came to the buttons from.
  private Area _from;
  private GameFile? _viewed;
  private int _nameWidth;
  private int _entryWidth;

  public GameConfigPage(AppContext context) : base(context) {
    _sections = new ListView<int>(RenderSection) {
      Items = [PropertiesSection, AllowlistSection, PermissionsSection],
    };
    _lists = [
      new ListView<int>(RenderProperty) { Empty = () => GameConfigText.NoProperties },
      new ListView<int>(index => RenderPlayer(AllowlistSection, index)),
      new ListView<int>(index => RenderPlayer(PermissionsSection, index)),
    ];
    _buttons = new ButtonRow(
        new Button(() => CommonText.Save, () => Save()) { Enabled = () => Dirty },
        new Button(() => CommonText.Discard, AskToDiscard) { Enabled = () => Dirty });
  }

  public override string Title => GameConfigText.Title;

  public override bool CanLeave => !Dirty;

  public override IReadOnlyList<Hint> Hints {
    get {
      Settle();
      return _area switch {
        Area.Sections => [
          CommonText.HintMove,
          .. EntriesFocusable ? [CommonText.HintOpen] : (Hint[])[],
          CommonText.HintNext,
        ],
        Area.Entries when CurrentFile is { Editable: true } =>
            [CommonText.HintMove, .. EntryHints, CommonText.HintNext],
        Area.Entries when CurrentFile is { } file && ReferenceEquals(_viewed, file) =>
            [.. _viewer.Hints, CommonText.HintNext],
        Area.Entries => [CommonText.HintNext],
        _ => [.. _buttons.Hints, CommonText.HintNext],
      };
    }
  }

  /// Whether a file has changes that are not saved.
  private bool Dirty => _files?.Any(file => file.Editable && file.Dirty) == true;

  private int Section => _sections.Selected;

  private GameFile? CurrentFile => _files?[Section];

  private ListView<int> CurrentList => _lists[Section];

  /// Whether the right side has something for the keys: rows, or text that scrolls.
  private bool EntriesFocusable =>
      CurrentFile is { } file
      && (file.Editable
          ? CurrentList.Items.Count > 0
          : ReferenceEquals(_viewed, file) && _viewer.Scrollable);

  public override void OnShown() => Load();

  public override void OnHidden() => Drop();

  public override void OnWorkspaceChanged() {
    // Changes are kept when it is still their workspace, such as after another page's
    // operation; the shell asked about them before it selected another one.
    if (Dirty && Context.Workspaces.Same(_workspace, Context.Workspace)) {
      return;
    }

    Drop();
    RefreshEntries();
    if (Context.CurrentPage == PageId.GameConfig) {
      Load();
    }
  }

  public override void ConfirmLeave(Action leave) {
    List<StyledLine> lines = [
      new StyledLine(CommonText.UnsavedQuestion),
      .. (_files ?? [])
          .Where(file => file.Editable && file.Dirty)
          .Select(file => new StyledLine(file.Name, Theme.Muted)),
    ];
    Context.Show(new ConfirmDialog(
        CommonText.UnsavedTitle, lines,
        [CommonText.Save, CommonText.Discard, CommonText.Cancel],
        choice => {
          switch (choice) {
            case 0:
              Save(leave);
              break;
            case 1:
              Drop();
              int dropped = _generation;
              leave();
              // Still shown, as when the user then does not quit, and nothing read the files
              // again, as another workspace does.
              if (Context.CurrentPage == PageId.GameConfig && dropped == _generation) {
                Load();
              }

              break;
          }
        }));
  }

  #region Reading and writing

  /// Forgets the files, and whatever is still being read of them. The rows stay until the
  /// files are read again, so that the selection does.
  private void Drop() {
    _files = null;
    _viewed = null;
    _generation++;
  }

  /// Forgets the files and reads the three of them again in the background; until then the
  /// page shows none, so that nothing is edited that is not what the files hold.
  private void Load() {
    Drop();
    int generation = _generation;
    LipClient client = Context.Client;
    string workspace = Context.Workspace;
    Context.Background(
        async () => new GameFile[] {
          await ReadProperties(client),
          await ReadPlayers(client, PlayerLists.AllowlistFileName),
          await ReadPlayers(client, PlayerLists.PermissionsFileName),
        },
        files => {
          if (generation != _generation) {
            return;
          }

          _files = files;
          _workspace = workspace;
          _viewed = null;
          RefreshEntries();
        });
  }

  private static async Task<GameFile> ReadProperties(LipClient client) {
    try {
      return new PropertiesFile(await client.ReadConfigFile(ServerProperties.FileName), null);
    }
    catch (Exception ex) {
      return new PropertiesFile(null, ex.Message);
    }
  }

  private static async Task<GameFile> ReadPlayers(LipClient client, string name) {
    try {
      return new PlayerListFile(name, await client.ReadConfigFile(name), null);
    }
    catch (Exception ex) {
      return new PlayerListFile(name, null, ex.Message);
    }
  }

  /// Writes the files that have changes, then calls then. A file that was written is saved
  /// even when one after it fails. When a file is no longer what was read, nothing is written
  /// unless overwrite is set: the user is asked first.
  private void Save(Action? then = null, bool overwrite = false) {
    List<(GameFile File, string Text)> changed = [
      .. (_files ?? [])
          .Where(file => file.Editable && file.Dirty)
          .Select(file => (file, file.Serialize())),
    ];
    if (changed.Count == 0) {
      then?.Invoke();
      return;
    }

    LipClient client = Context.Client;
    List<(GameFile File, string Text)> written = [];
    List<string> conflicts = [];
    Context.RunOperation(
        GameConfigText.SaveTitle,
        async _ => {
          try {
            foreach (GameFile file in overwrite ? [] : changed.Select(change => change.File)) {
              if (await ReadNow(client, file.Name) != (true, file.Source)) {
                conflicts.Add(file.Name);
              }
            }

            if (conflicts.Count > 0) {
              return;
            }

            foreach ((GameFile file, string text) in changed) {
              await client.WriteConfigFile(file.Name, text);
              written.Add((file, text));
            }
          }
          finally {
            Context.Post(() => {
              foreach ((GameFile file, string text) in written) {
                file.MarkSaved(text);
              }

              RefreshEntries();
            });
          }
        },
        _ => {
          if (conflicts.Count > 0) {
            Context.Show(new ConfirmDialog(
                GameConfigText.SaveTitle,
                [
                  new StyledLine(GameConfigText.ChangedOnDisk),
                  .. conflicts.Select(name => new StyledLine(name, Theme.Muted)),
                ],
                [GameConfigText.Overwrite, CommonText.Cancel],
                choice => {
                  if (choice == 0) {
                    Save(then, overwrite: true);
                  }
                }) { Selected = 1 });
            return;
          }

          Context.Toast(
              Context.ServerRunning ? GameConfigText.SavedWhileRunning : GameConfigText.Saved,
              ToastKind.Good);
          then?.Invoke();
        });
  }

  /// The text of a file now, null when it does not exist; not Read when it cannot be read.
  private static async Task<(bool Read, string? Text)> ReadNow(LipClient client, string name) {
    try {
      return (true, await client.ReadConfigFile(name));
    }
    catch (Exception) {
      return (false, null);
    }
  }

  private void AskToDiscard() =>
      Context.Confirm(
          GameConfigText.DiscardTitle, GameConfigText.DiscardQuestion, CommonText.Discard,
          Load);

  /// The rows of the sections are those of the files as they are now.
  private void RefreshEntries() {
    for (int section = 0; section < _lists.Length; section++) {
      _lists[section].Items = _files?[section] switch {
        PropertiesFile { Properties: { } properties } =>
            [.. Enumerable.Range(0, properties.Properties.Count)],
        PlayerListFile { Editable: true } players =>
            [AddRow, .. Enumerable.Range(0, players.Players.Count)],
        _ => [],
      };
    }
  }

  #endregion

  #region Keys

  public override bool OnKey(ConsoleKeyInfo key) {
    Settle();
    if (key.Key == ConsoleKey.Tab) {
      return Ui.IsBackTab(key) ? PreviousArea() : NextArea();
    }

    switch (_area) {
      case Area.Sections:
        if (_sections.OnKey(key)) {
          return true;
        }

        switch (key.Key) {
          case ConsoleKey.Enter or ConsoleKey.RightArrow:
            if (EntriesFocusable) {
              _area = Area.Entries;
            }

            return true;
          case ConsoleKey.DownArrow:
            return ToButtons();
          default:
            return false;
        }

      case Area.Entries:
        if (key.Key is ConsoleKey.LeftArrow or ConsoleKey.Escape) {
          _area = Area.Sections;
          return true;
        }

        if (CurrentFile is not { Editable: true }) {
          return CurrentFile is { } shown && ReferenceEquals(_viewed, shown)
              && _viewer.OnKey(key);
        }

        if (CurrentList.OnKey(key)) {
          return true;
        }

        return key.Key switch {
          ConsoleKey.Enter => Act(space: false),
          ConsoleKey.Spacebar => Act(space: true),
          ConsoleKey.DownArrow => ToButtons(),
          _ => false,
        };

      default:
        if (_buttons.OnKey(key)) {
          return true;
        }

        if (key.Key == ConsoleKey.UpArrow) {
          _area = _from;
          return true;
        }

        return false;
    }
  }

  /// The focus is on an area that can have it: the buttons only while there are changes, the
  /// right side only while it has rows.
  private void Settle() {
    if (_area == Area.Buttons && !Dirty) {
      _area = _from;
    }

    // While the files are read again the focus waits where it is.
    if (_area == Area.Entries && _files is not null && !EntriesFocusable) {
      _area = Area.Sections;
    }
  }

  private bool NextArea() {
    if (_area == Area.Sections && EntriesFocusable) {
      _area = Area.Entries;
      return true;
    }

    return ToButtons();
  }

  private bool PreviousArea() {
    switch (_area) {
      case Area.Buttons:
        _area = EntriesFocusable ? Area.Entries : Area.Sections;
        return true;
      case Area.Entries:
        _area = Area.Sections;
        return true;
      default:
        return false;
    }
  }

  /// Gives the buttons the focus; false when they are disabled, or have it.
  private bool ToButtons() {
    if (_area == Area.Buttons || !Dirty) {
      return false;
    }

    _from = _area;
    _area = Area.Buttons;
    return true;
  }

  #endregion

  #region Drawing

  public override void Draw(ScreenBuffer screen, Rect area, bool focused) {
    if (area.Width < 20 || area.Height < 5) {
      return;
    }

    Settle();
    Rect body = area.Shrink(bottom: 2);
    int left = Math.Min(
        Enumerable.Range(0, 3).Max(section => TextWidth.Of(SectionName(section))) + 2,
        area.Width / 2);
    _sections.Draw(
        screen, new Rect(body.X, body.Y, left, body.Height), focused && _area == Area.Sections);
    int bar = body.X + left + 1;
    for (int y = body.Y; y < body.Bottom; y++) {
      screen.Write(bar, y, "│", Theme.Border);
    }

    screen.Write(area.X, body.Bottom, new string('─', area.Width), Theme.Border);
    screen.Write(bar, body.Bottom, "┴", Theme.Border);
    DrawEntries(
        screen, new Rect(bar + 2, body.Y, Math.Max(0, body.Right - bar - 2), body.Height),
        focused && _area == Area.Entries);

    int row = area.Bottom - 1;
    int x = _buttons.Draw(screen, area.X, row, focused && _area == Area.Buttons, area.Right);
    string note = GameConfigText.AppliesOnStart;
    Style style = Context.ServerRunning ? Theme.Warning : Theme.Muted;
    if (area.Right - x - 1 >= TextWidth.Of(note)) {
      screen.Write(area.Right - TextWidth.Of(note), row, note, style);
    } else {
      screen.WriteFit(x + 1, row, area.Right - x - 1, note, style);
    }
  }

  private void DrawEntries(ScreenBuffer screen, Rect rect, bool focused) {
    if (rect.Width < 4) {
      return;
    }

    if (CurrentFile is not { } file) {
      screen.WriteFit(rect.X, rect.Y, rect.Width, CommonText.Loading, Theme.Muted);
      return;
    }

    if (!file.Editable) {
      DrawUneditable(screen, rect, file);
      return;
    }

    // The details of the selected row have the same rows whatever it is, so that the list
    // does not move as the selection does.
    int detailRows = Math.Clamp(rect.Height * 2 / 5, 3, 8);
    Rect list = rect.Shrink(bottom: detailRows + 1);
    _entryWidth = rect.Width;
    _nameWidth = NameWidth(file, rect.Width);
    CurrentList.Draw(screen, list, focused);
    screen.Write(rect.X, list.Bottom, new string('─', rect.Width), Theme.Border);
    List<StyledLine> details = Details(file, rect.Width);
    for (int i = 0; i < details.Count && i < detailRows; i++) {
      screen.Write(rect.X, list.Bottom + 1 + i, details[i], rect.Right);
    }
  }

  /// A file that does not exist, or that lip cannot read: why, and its text when there is any.
  private void DrawUneditable(ScreenBuffer screen, Rect rect, GameFile file) {
    List<StyledLine> lines = [];
    if (file.Missing) {
      lines.AddRange(TextWidth.Wrap(GameConfigText.Missing(file.Name), rect.Width)
          .Select(line => new StyledLine(line, Theme.Muted)));
    } else {
      lines.AddRange(TextWidth.Wrap(GameConfigText.Unreadable(file.Name), rect.Width)
          .Select(line => new StyledLine(line, Theme.Warning)));
      lines.AddRange(TextWidth.Wrap(file.Problem ?? "", rect.Width)
          .Select(line => new StyledLine(line, Theme.Muted)));
    }

    int y = rect.Y;
    foreach (StyledLine line in lines.Take(Math.Max(1, rect.Height / 2))) {
      screen.Write(rect.X, y++, line, rect.Right);
    }

    if (file.Raw is not { } raw) {
      return;
    }

    if (!ReferenceEquals(_viewed, file)) {
      _viewed = file;
      _viewer.SetText(raw);
      _viewer.ScrollToStart();
    }

    screen.Write(rect.X, y, new string('─', rect.Width), Theme.Border);
    _viewer.Draw(screen, rect.Shrink(top: y + 1 - rect.Y));
  }

  private StyledLine RenderSection(int section) {
    StyledLine line = new(SectionName(section));
    if (_files?[section] is { Editable: true, Dirty: true }) {
      line.Add(" " + ChangedMark, Theme.Accent);
    }

    return line;
  }

  private static string SectionName(int section) => section switch {
    PropertiesSection => GameConfigText.Properties,
    AllowlistSection => GameConfigText.Allowlist,
    _ => GameConfigText.Permissions,
  };

  /// The text in exactly width columns: cut with an ellipsis, or followed by spaces.
  private static string Pad(string text, int width) {
    string fit = TextWidth.Fit(text, width);
    return fit + new string(' ', Math.Max(0, width - TextWidth.Of(fit)));
  }

  #endregion
}
