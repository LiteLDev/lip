using System.IO.Abstractions;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Widgets;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Editing;
using Semver;

namespace Lip.Cli.Tui.Pages.Tools;

/// The lip.toml editor of the Tools page: every key of protocol/lip.toml.md as a form, which
/// creates the file when the workspace has none and edits it otherwise. Each value is checked
/// as it is typed (PackageDraftChecks), and each dependency online too, in the background
/// (ManifestEditor.Checks); the mark at the right of a row says how it stands, and the reason
/// of a ✗ is below it. Ctrl+S, or Save at the bottom, asks to confirm what is written, with
/// what is expected when the package is installed (ManifestSaveDialog); Esc closes the editor,
/// asking first when there are changes.
public sealed partial class ManifestEditor {
  private readonly AppContext _context;
  private readonly PackageFileText _file;
  private readonly PackageDraft _draft;
  private readonly Action _closed;
  private readonly ButtonRow _buttons;
  // What the draft was when the editor opened, to tell whether it changed.
  private readonly string _baseline;
  private List<Row> _rows = [];
  private int _focus;
  private int _top;
  // Why the package file is not valid as a whole (PackageDraftChecks.Document); worked out
  // again after each change.
  private string? _document;
  private bool _documentStale = true;

  public ManifestEditor(AppContext context, PackageFileText file, PackageDraft draft, Action closed) {
    _context = context;
    _file = file;
    _draft = draft;
    _closed = closed;
    _baseline = Json();
    _buttons = new ButtonRow(
        new Button(() => CommonText.Save, Save), new Button(() => CommonText.Cancel, Cancel));
    Build();
    foreach (DependencyDraft dependency in Dependencies()) {
      Schedule(dependency, now: true);
    }
  }

  public string Title =>
      _file.Exists ? ManifestEditorText.EditTitle : ManifestEditorText.CreateTitle;

  /// Whether the draft differs from what was opened.
  public bool Dirty => Json() != _baseline;

  public IReadOnlyList<Hint> Hints => [
    CommonText.HintMoveFields,
    .. Current.Hints,
    .. Current is ButtonsRow ? [CommonText.HintSelect]
        : Current.Enter is not null ? [ManifestEditorText.HintMore] : (Hint[])[],
    ManifestEditorText.HintSave,
    ManifestEditorText.HintClose,
  ];

  private Row Current => _rows[Math.Clamp(_focus, 0, _rows.Count - 1)];

  private string Json() => _draft.ToJson(_file.Written).ToJsonString();

  #region Rows

  /// Builds the rows of the draft again, after a value was added or removed; the focus goes to
  /// the row named focus, or stays on the row it is on.
  private void Build(string? focus = null) {
    string? key = focus ?? (_rows.Count > 0 ? Current.Key : null);
    List<Row> rows = [
      new HeadingRow("package", () => ManifestEditorText.Package),
      Field("id", () => ManifestEditorText.Id, () => _draft.Id, v => _draft.Id = v,
          () => Local(PackageDraftChecks.Id(_draft.Id)), () => ManifestEditorText.IdHelp,
          () => "github.com/owner/repo"),
      Field("version", () => ManifestEditorText.Version, () => _draft.Version,
          v => _draft.Version = v, () => Local(PackageDraftChecks.Version(_draft.Version)),
          () => ManifestEditorText.VersionHelp, () => "0.1.0"),
      new HeadingRow("info", () => ManifestEditorText.Information),
      Field("name", () => ManifestEditorText.Name, () => _draft.Name, v => _draft.Name = v,
          () => Status.None, () => ManifestEditorText.NameHelp),
      Field("description", () => ManifestEditorText.Description, () => _draft.Description,
          v => _draft.Description = v, () => Status.None,
          () => ManifestEditorText.DescriptionHelp),
      Field("logo", () => ManifestEditorText.Logo, () => _draft.Logo, v => _draft.Logo = v,
          () => Optional(_draft.Logo, PackageDraftChecks.Logo(_draft.Logo)),
          () => ManifestEditorText.LogoHelp, () => "https://"),
    ];
    // The tags below the logo, each in a row of its own, like the other lists.
    AddListRows(
        rows, "tags", () => ManifestEditorText.Tags, () => ManifestEditorText.TagsHelp,
        _draft.Tags, () => ManifestEditorText.Tag, _ => Status.None, indent: 0);
    for (int v = 0; v < _draft.Variants.Count; v++) {
      AddVariantRows(rows, _draft.Variants[v], $"v{v}");
    }

    rows.Add(new ActionRow("variant+", () => "", () => ManifestEditorText.AddVariant) {
      Enter = AddVariant,
    });
    rows.Add(new ButtonsRow(_buttons));
    _rows = rows;
    int index = key is null ? -1 : _rows.FindIndex(r => r.Key == key);
    _focus = index >= 0 ? index : Math.Clamp(_focus, 0, _rows.Count - 1);
    if (!Current.Focusable) {
      Move(1);
    }

    Changed();
  }

  private void AddVariantRows(List<Row> rows, VariantDraft variant, string key) {
    rows.Add(new HeadingRow(key + ".heading", () => ManifestEditorText.Variant(variant.Name)));
    rows.Add(Field(key, () => ManifestEditorText.VariantName, () => variant.Name,
        value => variant.Name = value,
        () => variant.Entries.Count == 0
            ? Local(new DraftProblem(DraftProblemKind.NoEntries))
            : Local(PackageDraftChecks.VariantName(
                variant.Name, _draft.Variants.Select(v => v.Name))),
        () => ManifestEditorText.VariantHelp + " " + ManifestEditorText.VariantActionsHelp,
        () => ManifestEditorText.DefaultVariantPlaceholder, () => VariantMenu(variant)));
    for (int e = 0; e < variant.Entries.Count; e++) {
      AddEntryRows(rows, variant, variant.Entries[e], $"{key}.e{e}", e);
    }
  }

  private void AddEntryRows(
      List<Row> rows, VariantDraft variant, EntryDraft entry, string key, int index) {
    rows.Add(new ActionRow(
        key, () => ManifestEditorText.Entry(index + 1), () => $"{entry.Side} · {entry.Platform}") {
      Indent = 1,
      Help = () => ManifestEditorText.EntryHelp,
      Enter = () => EntryMenu(variant, entry),
    });
    rows.Add(new ChoiceFieldRow(
        key + ".side", () => ManifestEditorText.Side, ["server", "client"], () => entry.Side,
        value => {
          entry.Side = value;
          Changed();
        },
        () => Local(PackageDraftChecks.Side(entry.Side))) {
      Indent = 2,
      Help = () => ManifestEditorText.SideHelp,
    });
    rows.Add(Field(key + ".platform", () => ManifestEditorText.Platform, () => entry.Platform,
        value => entry.Platform = value,
        () => Local(PackageDraftChecks.Platform(entry, variant.Entries)),
        () => ManifestEditorText.PlatformHelp, () => "win-x64", indent: 2));

    rows.Add(new ActionRow(
        key + ".deps", () => ManifestEditorText.Dependencies,
        () => ManifestEditorText.AddDependency) {
      Indent = 2,
      Help = () => ManifestEditorText.DependenciesHelp,
      Enter = () => AddDependency(entry),
    });
    for (int d = 0; d < entry.Dependencies.Count; d++) {
      DependencyDraft dependency = entry.Dependencies[d];
      rows.Add(Field(
          $"{key}.d{d}", () => dependency.Package, () => dependency.Range,
          value => {
            dependency.Range = value;
            Schedule(dependency);
          },
          () => DependencyStatus(dependency, entry), () => ManifestEditorText.RangeHelp,
          () => "*", () => DependencyMenu(entry, dependency), indent: 3));
    }

    rows.Add(new ActionRow(
        key + ".assets", () => ManifestEditorText.Assets, () => ManifestEditorText.AddAsset) {
      Indent = 2,
      Help = () => ManifestEditorText.AssetsHelp,
      Enter = () => AddAsset(entry),
    });
    for (int a = 0; a < entry.Assets.Count; a++) {
      AddAssetRows(rows, entry, entry.Assets[a], $"{key}.a{a}", a);
    }

    AddListRows(
        rows, key + ".preserve", () => ManifestEditorText.Preserve,
        () => ManifestEditorText.PreserveHelp, entry.Preserve, () => ManifestEditorText.Glob,
        glob => Optional(glob, PackageDraftChecks.Glob(glob)));
    AddListRows(
        rows, key + ".purge", () => ManifestEditorText.Purge, () => ManifestEditorText.PurgeHelp,
        entry.Purge, () => ManifestEditorText.Glob,
        glob => Optional(glob, PackageDraftChecks.Glob(glob)));
    for (int h = 0; h < EntryDraft.HookNames.Length; h++) {
      string name = EntryDraft.HookNames[h];
      AddListRows(
          rows, $"{key}.h{h}", () => ManifestEditorText.Hook(name),
          () => ManifestEditorText.HookHelp, entry.Hook(h), () => ManifestEditorText.Command,
          _ => Status.None);
    }
  }

  private void AddAssetRows(
      List<Row> rows, EntryDraft entry, AssetDraft asset, string key, int index) {
    rows.Add(new ChoiceFieldRow(
        key, () => ManifestEditorText.Asset(index + 1), AssetDraft.Types, () => asset.Type,
        value => {
          asset.Type = value;
          if (value != "self" && asset.Urls.Count == 0) {
            asset.Urls.Add("");
          }

          Build();
        },
        () => Local(PackageDraftChecks.AssetType(asset.Type) ?? PackageDraftChecks.Urls(asset)
            ?? PackageDraftChecks.Mcmod(asset, entry.Assets)
            ?? PackageDraftChecks.PlaceAllowed(asset))) {
      Indent = 3,
      Help = () => ManifestEditorText.AssetHelp,
      Enter = () => AssetMenu(entry, asset, key),
    });
    if (asset.Type != "self" || asset.Urls.Count > 0) {
      for (int u = 0; u < asset.Urls.Count; u++) {
        int at = u;
        rows.Add(Field(
            $"{key}.u{u}", () => ManifestEditorText.Url, () => asset.Urls[at],
            value => asset.Urls[at] = value,
            () => Optional(asset.Urls[at], PackageDraftChecks.Url(asset.Urls[at])),
            () => ManifestEditorText.UrlHelp, () => "https://",
            () => RemoveMenu(ManifestEditorText.Url, () => {
              asset.Urls.RemoveAt(at);
              Build(key);
            }),
            indent: 4));
      }
    }

    for (int p = 0; p < asset.Place.Count; p++) {
      PlaceDraft place = asset.Place[p];
      void Remove() => RemoveMenu(ManifestEditorText.PlaceSrc, () => {
        asset.Place.Remove(place);
        Build(key);
      });
      rows.Add(Field(
          $"{key}.p{p}.src", () => ManifestEditorText.PlaceSrc, () => place.Src,
          value => place.Src = value, () => Local(PackageDraftChecks.Src(place)),
          () => ManifestEditorText.PlaceHelp, () => "bin/", Remove, indent: 4));
      rows.Add(Field(
          $"{key}.p{p}.dest", () => ManifestEditorText.PlaceDest, () => place.Dest,
          value => place.Dest = value, () => Local(PackageDraftChecks.Dest(place)),
          () => ManifestEditorText.PlaceHelp, () => "plugins/", Remove, indent: 4));
    }
  }

  /// A list of texts: a row that Enter adds one to, and a row for each of them, that Enter
  /// removes.
  private void AddListRows(
      List<Row> rows, string key, Func<string> label, Func<string> help, List<string> items,
      Func<string> itemLabel, Func<string, Status> check, int indent = 2) {
    rows.Add(new ActionRow(key, label, () => ManifestEditorText.AddItem) {
      Indent = indent,
      Help = help,
      Enter = () => {
        items.Add("");
        Build($"{key}.{items.Count - 1}");
      },
    });
    for (int i = 0; i < items.Count; i++) {
      int at = i;
      rows.Add(Field(
          $"{key}.{i}", itemLabel, () => items[at], value => items[at] = value,
          () => check(items[at]), () => ManifestEditorText.ItemHelp, null,
          () => RemoveMenu(itemLabel(), () => {
            items.RemoveAt(at);
            Build(key);
          }),
          indent: indent + 1));
    }
  }

  /// A row of a text that the draft holds.
  private FieldRow Field(
      string key, Func<string> label, Func<string> get, Action<string> set, Func<Status> check,
      Func<string>? help = null, Func<string>? placeholder = null, Action? enter = null,
      int indent = 0) =>
      new(key, label, get(), value => {
        set(value);
        Changed();
      }, check, placeholder) {
        Help = help,
        Enter = enter,
        Indent = indent,
      };

  /// The status of a value that is checked without the network.
  private static Status Local(DraftProblem? problem) =>
      problem is null ? Status.Valid : new Status(Mark.Invalid, ManifestEditorText.Problem(problem));

  /// The same for an optional value: no mark while it is empty.
  private static Status Optional(string value, DraftProblem? problem) =>
      value.Length == 0 ? Status.None : Local(problem);

  /// A value changed: what depends on all of them is worked out again.
  private void Changed() => _documentStale = true;

  /// Why the package file is not valid as a whole, rendered, or null.
  private string? Document() {
    if (_documentStale) {
      _document = PackageDraftChecks.Document(_draft.ToJson(_file.Written));
      _documentStale = false;
    }

    return _document;
  }

  /// The key of the rows of an entry.
  private string EntryKey(EntryDraft entry) {
    for (int v = 0; v < _draft.Variants.Count; v++) {
      int e = _draft.Variants[v].Entries.IndexOf(entry);
      if (e >= 0) {
        return $"v{v}.e{e}";
      }
    }

    return "";
  }

  #endregion

  #region Menus

  private void VariantMenu(VariantDraft variant) =>
      _context.ShowMenu(
          ManifestEditorText.Variant(variant.Name),
          new MenuItem(ManifestEditorText.AddEntry, () => AddEntry(variant)),
          new MenuItem(ManifestEditorText.RemoveVariant, () => {
            _draft.Variants.Remove(variant);
            Forget();
            Build("variant+");
          }, _draft.Variants.Count > 1));

  private void EntryMenu(VariantDraft variant, EntryDraft entry) =>
      _context.ShowMenu(
          ManifestEditorText.Actions,
          new MenuItem(ManifestEditorText.AddEntry, () => AddEntry(variant)),
          new MenuItem(ManifestEditorText.RemoveEntry, () => {
            int v = _draft.Variants.IndexOf(variant);
            variant.Entries.Remove(entry);
            Forget();
            Build($"v{v}");
          }, variant.Entries.Count > 1));

  /// Adds an entry to a variant: for the server, on a platform that no entry of the variant
  /// for the server has yet.
  private void AddEntry(VariantDraft variant) {
    string platform = ((string[])["win-x64", "linux-x64", "win-arm64", "linux-arm64"])
        .FirstOrDefault(p => !variant.Entries.Any(e => e.Side == "server" && e.Platform == p))
        ?? "";
    EntryDraft entry = new() { Platform = platform };
    variant.Entries.Add(entry);
    Build(EntryKey(entry));
  }

  private void AddVariant() {
    string name = Enumerable.Range(1, 100).Select(i => i == 1 ? "variant" : $"variant{i}")
        .First(n => !_draft.Variants.Any(v => v.Name == n));
    _draft.Variants.Add(new VariantDraft { Name = name, Entries = [new EntryDraft()] });
    Build($"v{_draft.Variants.Count - 1}");
  }

  private void AddAsset(EntryDraft entry) =>
      _context.Show(new ChoiceDialog(
          ManifestEditorText.AssetType,
          [.. AssetDraft.Types.Select(type => new ChoiceItem(type))],
          chosen => {
            string type = AssetDraft.Types[chosen];
            entry.Assets.Add(new AssetDraft { Type = type, Urls = type == "self" ? [] : [""] });
            string key = $"{EntryKey(entry)}.a{entry.Assets.Count - 1}";
            Build(type == "self" ? key : key + ".u0");
          }));

  private void AssetMenu(EntryDraft entry, AssetDraft asset, string key) =>
      _context.ShowMenu(
          ManifestEditorText.AssetType + " " + asset.Type,
          new MenuItem(ManifestEditorText.AddUrl, () => {
            asset.Urls.Add("");
            Build($"{key}.u{asset.Urls.Count - 1}");
          }, asset.Type != "self"),
          new MenuItem(ManifestEditorText.AddPlace, () => {
            asset.Place.Add(new PlaceDraft());
            Build($"{key}.p{asset.Place.Count - 1}.src");
          }, asset.Type != "mcmod"),
          new MenuItem(ManifestEditorText.RemoveAsset, () => {
            entry.Assets.Remove(asset);
            Build(EntryKey(entry) + ".assets");
          }));

  private void RemoveMenu(string title, Action remove) =>
      _context.ShowMenu(title, new MenuItem(ManifestEditorText.Remove, remove));

  #endregion

  #region Drawing

  public void Draw(ScreenBuffer screen, Rect area, bool focused) {
    if (area.Height < 6 || area.Width < 20) {
      return;
    }

    int x = screen.Write(area.X, area.Y, Title, Theme.Strong, area.Right);
    screen.Write(x + 2, area.Y, _file.Path, Theme.Muted, area.Right);
    (string summary, Style style) = Summary();
    screen.WriteFit(area.X, area.Y + 1, area.Width, summary, style);
    screen.Write(area.X, area.Y + 2, new string('─', area.Width), Theme.Border);
    screen.Write(area.X, area.Bottom - 2, new string('─', area.Width), Theme.Border);
    bool onButtons = Current is ButtonsRow;
    screen.Write(area.X, area.Bottom - 1, onButtons && focused ? "> " : "  ", Theme.Accent);
    _buttons.Draw(screen, area.X + 2, area.Bottom - 1, onButtons && focused, area.Right);

    DrawRows(screen, new Rect(area.X, area.Y + 3, area.Width, area.Height - 5), focused);
  }

  /// The rows that fit rect, from the first one that leaves the focused one in sight.
  private void DrawRows(ScreenBuffer screen, Rect rect, bool focused) {
    int count = _rows.Count - 1;
    // The labels take as many columns as the longest needs, and at most half of the form.
    int labelWidth = Math.Clamp(
        _rows.Take(count).Where(r => r is not HeadingRow)
            .Max(r => TextWidth.Of(r.Label) + (r.Indent * 2)) + 1,
        12, Math.Max(12, rect.Width / 2));
    int valueX = rect.X + 2 + labelWidth + 1;
    // The last two columns are those of the mark and of the marks of scrolling.
    int valueWidth = Math.Max(1, rect.Right - 3 - valueX);
    int[] heights = [.. Enumerable.Range(0, count).Select(i => Details(i, valueWidth).Count + 1
        + (i > 0 && _rows[i] is HeadingRow ? 1 : 0))];
    if (_focus < count) {
      // A heading right above the row that has the focus is shown with it.
      int first = _focus > 0 && _rows[_focus - 1] is HeadingRow ? _focus - 1 : _focus;
      _top = Math.Min(_top, first);
      while (_top < _focus && heights[_top.._focus].Sum() + heights[_focus] > rect.Height) {
        _top++;
      }
    }

    _top = Math.Clamp(_top, 0, Math.Max(0, count - 1));
    int y = rect.Y;
    int last = _top - 1;
    for (int i = _top; i < count && y < rect.Bottom; i++) {
      Row row = _rows[i];
      if (row is HeadingRow) {
        if (i > _top) {
          y++;
        }

        if (y >= rect.Bottom) {
          break;
        }

        int at = screen.Write(
            rect.X + 2 + (row.Indent * 2), y, row.Label, Theme.Accent, rect.Right);
        screen.Write(at + 1, y, new string('─', Math.Max(0, rect.Right - at - 2)), Theme.Border);
        y++;
        last = i;
        continue;
      }

      bool current = focused && i == _focus;
      screen.Write(rect.X, y, current ? "> " : "  ", Theme.Accent);
      int indent = Math.Min(row.Indent * 2, labelWidth / 2);
      // A package id that is too long is cut in its middle: its end tells the most.
      string label = row.Label.Contains('/')
          ? PackageCells.FitMiddle(row.Label, labelWidth - indent)
          : row.Label;
      screen.WriteFit(
          rect.X + 2 + indent, y, labelWidth - indent, label,
          current ? Theme.Strong : i == _focus ? default : Theme.Muted);
      row.DrawValue(screen, valueX, y, valueWidth, current);
      (string mark, Style markStyle) = MarkOf(row.Status().Mark);
      screen.Write(rect.Right - 2, y, mark, markStyle);
      y++;
      foreach ((string line, Style style) in Details(i, valueWidth)) {
        if (y >= rect.Bottom) {
          break;
        }

        screen.Write(valueX, y++, line, style, rect.Right);
      }

      last = i;
    }

    Ui.ScrollMarks(screen, rect, _top > 0, last < count - 1);
  }

  /// The lines below a row: why its value is not valid, or could not be confirmed, always; and
  /// while it has the focus, its help, and what a check found.
  private List<(string Text, Style Style)> Details(int index, int width) {
    Row row = _rows[index];
    if (row is HeadingRow) {
      return [];
    }

    List<(string, Style)> lines = [];
    Status status = row.Status();
    bool current = index == _focus;
    if (status.Message is { } message && (current || status.Mark is Mark.Invalid or Mark.Unknown)) {
      Style style = status.Mark switch {
        Mark.Invalid => Theme.Bad,
        Mark.Unknown => Theme.Warning,
        _ => Theme.Muted,
      };
      lines.AddRange(TextWidth.Wrap(message, width).Take(3).Select(l => (l, style)));
    }

    if (current && row.Help?.Invoke() is { Length: > 0 } help) {
      lines.AddRange(TextWidth.Wrap(help, width).Take(3).Select(l => (l, Theme.Muted)));
    }

    return lines;
  }

  /// The line below the title: how the values stand as a whole.
  private (string Text, Style Style) Summary() {
    // What a check online found keeps nothing from being saved: it is a warning.
    List<Status> statuses = [.. _rows.Select(r => r.Status())];
    int invalid = statuses.Count(s => s is { Mark: Mark.Invalid, Remote: false });
    int checking = statuses.Count(s => s.Mark == Mark.Checking);
    int unknown = statuses.Count(s => s is { Mark: Mark.Unknown } or { Mark: Mark.Invalid, Remote: true });
    if (invalid > 0) {
      return ("✗ " + ManifestEditorText.Problems(invalid), Theme.Bad);
    }

    if (Document() is { } document) {
      return ("✗ " + document, Theme.Bad);
    }

    if (checking > 0) {
      return ("⋯ " + ManifestEditorText.Checking(checking), Theme.Accent);
    }

    return unknown > 0
        ? ("? " + ManifestEditorText.Warnings(unknown), Theme.Warning)
        : ("✓ " + ManifestEditorText.AllValid, Theme.Good);
  }

  #endregion

  #region Keys

  public bool OnKey(ConsoleKeyInfo key) {
    if (Ui.IsInterrupt(key)) {
      return false;
    }

    if (key.Modifiers.HasFlag(ConsoleModifiers.Control) && key.Key == ConsoleKey.S) {
      Save();
      return true;
    }

    switch (key.Key) {
      case ConsoleKey.Escape:
        Cancel();
        return true;
      case ConsoleKey.UpArrow:
        Move(-1);
        return true;
      case ConsoleKey.DownArrow:
        Move(1);
        return true;
      case ConsoleKey.Tab:
        // Past the last row, or before the first, Tab goes on to the sidebar.
        int before = _focus;
        Move(Ui.IsBackTab(key) ? -1 : 1);
        return _focus != before;
      case ConsoleKey.PageUp:
        for (int i = 0; i < 10; i++) {
          Move(-1);
        }

        return true;
      case ConsoleKey.PageDown:
        for (int i = 0; i < 10; i++) {
          Move(1);
        }

        return true;
      case ConsoleKey.Enter when Current is ButtonsRow:
        _buttons.OnKey(key);
        return true;
      case ConsoleKey.Enter:
        if (Current.Enter is { } enter) {
          enter();
        } else {
          Move(1);
        }

        return true;
    }

    // The other keys change the value of the row; none leaves the editor.
    Current.OnKey(key);
    return true;
  }

  /// Tab brought the focus from the sidebar: to the first row, or to the buttons.
  public void TabIn(bool backward) {
    _focus = backward ? _rows.Count - 1 : 0;
    if (!Current.Focusable) {
      Move(1);
    }
  }

  /// Moves the focus to the row step after it that can have it; round: from the last row to
  /// the first, and back.
  private void Move(int step, bool round = false) {
    int at = _focus;
    for (int i = 0; i < _rows.Count; i++) {
      at += step;
      if (at < 0 || at >= _rows.Count) {
        if (!round) {
          return;
        }

        at = (at + _rows.Count) % _rows.Count;
      }

      if (_rows[at].Focusable) {
        _focus = at;
        return;
      }
    }
  }

  #endregion

  #region Save and close

  /// Asks to confirm what is written, unless a value is not valid: the focus then goes to the
  /// first one that is not. A dependency that could not be confirmed online does not keep the
  /// file from being saved.
  private void Save() {
    if (PackageDraftChecks.HasProblems(_draft) || Document() is not null) {
      int invalid = _rows.FindIndex(r => r.Status() is { Mark: Mark.Invalid, Remote: false });
      if (invalid >= 0) {
        _focus = invalid;
      }

      _context.Toast(ManifestEditorText.FixFirst, ToastKind.Bad);
      return;
    }

    PackageFileUpdate update = PackageFileStore.Update(_file, _draft);
    bool unconfirmed = _rows.Any(r => r.Status() is { Remote: true, Mark: Mark.Invalid or Mark.Unknown });
    _context.Show(new ManifestSaveDialog(
        _file, _draft, update, unconfirmed, Lookup, () => Write(update)));
  }

  private void Write(PackageFileUpdate update) {
    IFileSystem fs = _context.FileSystem;
    void Written() {
      try {
        PackageFileStore.Write(fs, _file, update.Text);
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
        _context.ShowFailure(ManifestEditorText.SaveTitle, ex);
        return;
      }

      _context.Toast(ManifestEditorText.Saved(PackageFormat.FileName), ToastKind.Good);
      Close();
    }

    if (PackageFileStore.Unchanged(fs, _file)) {
      Written();
      return;
    }

    _context.Show(new ConfirmDialog(
        ManifestEditorText.SaveTitle,
        [new StyledLine(ManifestEditorText.ChangedOnDisk, Theme.Warning)],
        [CommonText.Overwrite, CommonText.Cancel],
        choice => {
          if (choice == 0) {
            Written();
          }
        }) {
      Selected = 1,
    });
  }

  /// Closes the editor, asking first whether to discard the changes.
  private void Cancel() => ConfirmDiscard(Close);

  /// Calls discarded at once when nothing changed, otherwise once the user said to discard
  /// the changes.
  public void ConfirmDiscard(Action discarded) {
    if (!Dirty) {
      discarded();
      return;
    }

    _context.Show(new ConfirmDialog(
        CommonText.UnsavedTitle,
        [new StyledLine(CommonText.DiscardQuestion)],
        [CommonText.Discard, CommonText.Cancel],
        choice => {
          if (choice == 0) {
            discarded();
          }
        }) {
      Selected = 1,
    });
  }

  /// Closes the editor without asking; what it reads online is abandoned.
  public void Close() {
    _cancel.Cancel();
    _closed();
  }

  #endregion
}
