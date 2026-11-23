using System.Text.Json.Nodes;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Widgets;
using Lip.Core.Catalog;
using Lip.Core.Json;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Editing;
using Lip.Core.PublicApi;
using Semver;

namespace Lip.Cli.Tui.Pages.Tools;

/// Asks to confirm what the lip.toml editor writes: the values that change, whether the text of
/// the file is kept, and what is expected when the package is installed on a server with the
/// BDS and the LeviLamina that the user chooses (InstallPrediction), each package checked in the
/// background. ↑↓ and Tab move between the choices and the buttons, ←→ change them; Save writes,
/// Esc or Back goes back to the editor.
public sealed class ManifestSaveDialog : Dialog {
  // The rows of changes shown at most; the rest are counted.
  private const int MostChanges = 8;

  private readonly PackageDraft _draft;
  private readonly PackageFileUpdate _update;
  private readonly bool _unconfirmed;
  private readonly bool _newFile;
  private readonly Func<string, Task<PackageLookup>> _lookup;
  private readonly Action _confirmed;
  private readonly List<StyledLine> _changes;
  private readonly IReadOnlyList<string> _variants;
  private readonly ButtonRow _buttons;

  private bool _started;
  private IReadOnlyList<GameVersion>? _games;
  private string? _gamesError;
  private int _game;
  private CatalogWorkspace? _installed;
  private IReadOnlyList<SemVersion>? _loaders;
  private int _loader;
  private int _variant;
  // Which choice has the focus: the game, the loader, the variant, or the buttons last.
  private int _focus;
  private int _generation;
  private List<(string Package, Prediction? Prediction)> _predictions = [];
  private CancellationTokenSource? _loadersRead;

  public ManifestSaveDialog(
      PackageFileText file, PackageDraft draft, PackageFileUpdate update, bool unconfirmed,
      Func<string, Task<PackageLookup>> lookup, Action confirmed) {
    _draft = draft;
    _update = update;
    _unconfirmed = unconfirmed;
    _newFile = !file.Exists;
    _lookup = lookup;
    _confirmed = confirmed;
    _changes = Changes(file.Written ?? [], draft.ToJson(file.Written));
    _variants = [.. draft.Variants.Select(v => v.Name)];
    _variant = Math.Max(0, _variants.ToList().IndexOf(""));
    _buttons = new ButtonRow(
        new Button(() => CommonText.Save, Confirm), new Button(() => CommonText.Cancel, Close));
    _focus = Choices;
  }

  /// How many choices there are above the buttons.
  private int Choices => _variants.Count > 1 ? 3 : 2;

  public override IReadOnlyList<Hint> Hints => [
    CommonText.HintMoveFields,
    _focus < Choices ? CommonText.HintChange : CommonText.HintSwitch,
    CommonText.HintSelect,
    CommonText.HintCancel,
  ];

  private SemVersion? Game => _games is { Count: > 0 } games ? games[_game].Version : null;

  private SemVersion? Loader =>
      _loaders is { } loaders && _loader < loaders.Count ? loaders[_loader] : null;

  public override void OnTick() => Start();

  /// Reads the versions of the game, and what the workspace has, once.
  private void Start() {
    if (_started || Owner is null) {
      return;
    }

    _started = true;
    LipClient client = Context.Client;
    IServerSetup setup = Context.ServerSetup;
    Context.Background(
        async () => {
          IReadOnlyList<GameVersion> games = await setup.GameVersions(client);
          CatalogWorkspace installed;
          try {
            installed = CatalogWorkspace.Of(await client.ListPackages());
          }
          catch (Exception ex) when (ex is not OperationCanceledException) {
            installed = new CatalogWorkspace(null, null);
          }

          return (games, installed);
        },
        read => {
          _games = [.. read.games.Where(g => g.Windows)];
          _installed = read.installed;
          int has = _games.ToList().FindIndex(g => g.Version.ToString() == read.installed.Game);
          _game = Math.Max(0, has);
          ReadLoaders();
        },
        ex => {
          _gamesError = ex.Message;
          ReadLoaders();
        });
  }

  /// Reads the versions of LeviLamina for the game chosen.
  private void ReadLoaders() {
    _loadersRead?.Cancel();
    _loaders = null;
    if (Game is not { } game) {
      // Without a game there is no LeviLamina to choose; the package itself is predicted.
      _loaders = [];
      _loader = 0;
      Predict();
      return;
    }

    CancellationTokenSource read = new();
    _loadersRead = read;
    LipClient client = Context.Client;
    IServerSetup setup = Context.ServerSetup;
    Context.Background(
        () => setup.LoaderVersions(client, game, read.Token),
        loaders => {
          if (_loadersRead != read) {
            return;
          }

          _loaders = loaders;
          _loader = DefaultLoader(loaders);
          Predict();
        },
        _ => {
          if (_loadersRead == read) {
            _loaders = [];
            _loader = 0;
            Predict();
          }
        });
  }

  /// The LeviLamina offered first: that of the workspace, otherwise the newest in the range
  /// that the package depends on it with, otherwise the newest.
  private int DefaultLoader(IReadOnlyList<SemVersion> loaders) {
    int has = loaders.ToList().FindIndex(l => l.ToString() == _installed?.Loader);
    if (has >= 0) {
      return has;
    }

    string variant = _variants.Count == 0 ? "" : _variants[_variant];
    DependencyDraft? dependency = InstallPrediction.Entry(_draft, variant)?.Dependencies
        .FirstOrDefault(d => GameSetup.IsLoaderRepository(d.Id));
    return dependency is not null && VersionRange.TryParse(dependency.Range, out VersionRange? range)
        ? Math.Max(0, loaders.ToList().FindIndex(range!.IsSatisfiedBy))
        : 0;
  }

  /// Works out what is expected for the choices, the package itself at once, each dependency
  /// once what is known of it is read.
  private void Predict() {
    int generation = ++_generation;
    string variant = _variants.Count == 0 ? "" : _variants[_variant];
    Prediction self = InstallPrediction.Self(_draft, variant);
    _predictions = [(self.Package, self)];
    if (Game is not { } game || _loaders is null) {
      return;
    }

    SemVersion? loader = Loader;
    EntryDraft? entry = InstallPrediction.Entry(_draft, variant);
    foreach (DependencyDraft dependency in entry?.Dependencies ?? []) {
      int index = _predictions.Count;
      _predictions.Add((dependency.Package, null));
      Task<PackageLookup> lookup = _lookup(dependency.Id);
      Context.Background(
          () => lookup,
          found => {
            if (generation == _generation) {
              _predictions[index] = (dependency.Package,
                  InstallPrediction.Dependency(dependency, game, loader, found));
            }
          },
          ex => {
            if (generation == _generation) {
              _predictions[index] = (dependency.Package, new Prediction(
                  dependency.Package, PredictionOutcome.Unknown, PredictionReason.CannotCheck,
                  ex.Message));
            }
          });
    }
  }

  public override void Draw(ScreenBuffer screen) {
    Start();
    int width = Ui.DialogWidth(screen, 96);
    int textWidth = width - 4;
    List<StyledLine> top = [];
    top.Add(new StyledLine(ManifestEditorText.Changes, Theme.Strong));
    if (_newFile) {
      top.Add(new StyledLine(ManifestEditorText.NewFile, Theme.Muted));
    }

    if (_changes.Count == 0) {
      top.Add(new StyledLine(ManifestEditorText.NoChanges, Theme.Muted));
    }

    top.AddRange(_changes.Take(MostChanges));
    if (_changes.Count > MostChanges) {
      top.Add(new StyledLine($"… +{_changes.Count - MostChanges}", Theme.Muted));
    }

    if (!_newFile && _update.Changed) {
      top.Add(_update.KeepsText
          ? new StyledLine(ManifestEditorText.KeepsText, Theme.Muted)
          : new StyledLine(ManifestEditorText.Rewrites, Theme.Warning));
    }

    if (_unconfirmed) {
      top.Add(new StyledLine("? " + ManifestEditorText.RemoteWarnings, Theme.Warning));
    }

    List<StyledLine> wrapped = [.. top.SelectMany(line => TextWidth.Wrap(line, textWidth))];
    List<string> about = TextWidth.Wrap(ManifestEditorText.PredictionAbout, textWidth);
    // The prediction and the buttons keep their rows; the changes take what is left.
    int below = 1 + 1 + about.Count + Choices + _predictions.Count + 2;
    Rect inner = Ui.DialogFrame(
        screen, ManifestEditorText.SaveTitle, width, wrapped.Count + below + 2);
    int changeRows = Math.Max(1, inner.Height - below);
    int y = inner.Y;
    foreach (StyledLine line in wrapped.Take(changeRows)) {
      screen.Write(inner.X, y++, line, inner.Right);
    }

    screen.Write(inner.X, y++, new string('─', inner.Width), Theme.Border);
    screen.Write(inner.X, y++, ManifestEditorText.Prediction, Theme.Strong, inner.Right);
    foreach (string line in about) {
      screen.Write(inner.X, y++, line, Theme.Muted, inner.Right);
    }

    int labels = 14;
    y = DrawChoice(screen, inner, y, 0, labels, ManifestEditorText.Bds,
        _gamesError is not null ? ManifestEditorText.NoGameVersions
            : Game?.ToString() ?? ManifestEditorText.LoadingVersions);
    y = DrawChoice(screen, inner, y, 1, labels, ManifestEditorText.LeviLamina,
        _loaders is null ? ManifestEditorText.LoadingVersions
            : Loader?.ToString() ?? ManifestEditorText.NoLoader);
    if (_variants.Count > 1) {
      string name = _variants[_variant];
      y = DrawChoice(screen, inner, y, 2, labels, ManifestEditorText.VariantChoice,
          name.Length == 0 ? ManifestEditorText.DefaultVariant : name);
    }

    int nameWidth = Math.Min(40, Math.Max(16, inner.Width / 3));
    foreach ((string package, Prediction? prediction) in _predictions) {
      if (y >= inner.Bottom - 2) {
        break;
      }

      (string mark, Style style) = prediction?.Outcome switch {
        null => ("⋯", Theme.Accent),
        PredictionOutcome.Installs => ("✓", Theme.Good),
        PredictionOutcome.Fails => ("✗", Theme.Bad),
        _ => ("?", Theme.Warning),
      };
      screen.Write(inner.X, y, mark, style);
      screen.Write(inner.X + 2, y, PackageCells.PadMiddle(package, nameWidth), default, inner.Right);
      screen.WriteFit(
          inner.X + 3 + nameWidth, y, inner.Width - 3 - nameWidth,
          prediction is null ? CommonText.Loading : ManifestEditorText.Predicted(prediction),
          prediction is null ? Theme.Muted : style);
      y++;
    }

    screen.Write(inner.X, inner.Bottom - 2, new string('─', inner.Width), Theme.Border);
    _buttons.Draw(screen, inner.X, inner.Bottom - 1, _focus == Choices, inner.Right);
  }

  private int DrawChoice(
      ScreenBuffer screen, Rect inner, int y, int index, int labels, string label, string value) {
    if (y >= inner.Bottom - 2) {
      return y;
    }

    bool current = _focus == index;
    screen.Write(inner.X, y, current ? "> " : "  ", Theme.Accent);
    screen.WriteFit(inner.X + 2, y, labels - 2, label, current ? Theme.Strong : Theme.Muted);
    screen.WriteFit(
        inner.X + labels, y, inner.Width - labels, $"‹ {value} ›", current ? Theme.Accent : default);
    return y + 1;
  }

  public override void OnKey(ConsoleKeyInfo key) {
    switch (key.Key) {
      case ConsoleKey.Escape:
        Close();
        return;
      case ConsoleKey.UpArrow:
        _focus = Math.Max(0, _focus - 1);
        return;
      case ConsoleKey.DownArrow:
        _focus = Math.Min(Choices, _focus + 1);
        return;
      case ConsoleKey.Tab:
        _focus = (_focus + (Ui.IsBackTab(key) ? Choices : 1)) % (Choices + 1);
        return;
      case ConsoleKey.Enter when _focus < Choices:
        _focus = Choices;
        return;
    }

    if (_focus == Choices) {
      _buttons.OnKey(key);
      return;
    }

    int step = key.Key switch {
      ConsoleKey.LeftArrow => -1,
      ConsoleKey.RightArrow or ConsoleKey.Spacebar => 1,
      _ => 0,
    };
    if (step == 0) {
      return;
    }

    switch (_focus) {
      case 0 when _games is { Count: > 0 } games:
        _game = (_game + games.Count + step) % games.Count;
        ReadLoaders();
        Predict();
        break;
      case 1 when _loaders is { } loaders:
        // The versions, then none.
        _loader = (_loader + loaders.Count + 1 + step) % (loaders.Count + 1);
        Predict();
        break;
      case 2:
        _variant = (_variant + _variants.Count + step) % _variants.Count;
        Predict();
        break;
    }
  }

  private void Confirm() {
    _loadersRead?.Cancel();
    Close();
    _confirmed();
  }

  /// The values that change from before to after, one line each: ~ for a value that changes,
  /// + for one that is added, - for one that is removed.
  private static List<StyledLine> Changes(JsonObject before, JsonObject after) {
    Dictionary<string, string> old = [];
    Dictionary<string, string> now = [];
    Flatten(before, "", old);
    Flatten(after, "", now);
    List<StyledLine> lines = [];
    foreach ((string path, string value) in now) {
      if (!old.TryGetValue(path, out string? was)) {
        lines.Add(new StyledLine($"+ {path} = {value}", Theme.Good));
      } else if (was != value) {
        lines.Add(new StyledLine($"~ {path}: {was} → {value}", Theme.Warning));
      }
    }

    foreach (string path in old.Keys.Where(p => !now.ContainsKey(p))) {
      lines.Add(new StyledLine($"- {path}", Theme.Bad));
    }

    return lines;
  }

  /// The values of a document by their paths, each written as in TOML: tables and arrays of
  /// tables are gone through, any other value is one.
  private static void Flatten(JsonNode? node, string path, Dictionary<string, string> values) {
    switch (node) {
      case JsonObject obj when obj.Count > 0 || path.Length == 0:
        foreach ((string key, JsonNode? value) in obj) {
          string name = TomlJson.QuoteKey(key);
          Flatten(value, path.Length == 0 ? name : $"{path}.{name}", values);
        }

        break;
      case JsonArray array when array.Count > 0 && array.All(item => item is JsonObject):
        for (int i = 0; i < array.Count; i++) {
          Flatten(array[i], $"{path}[{i}]", values);
        }

        break;
      case null:
        break;
      default:
        values[path] = TomlJson.Inline(node);
        break;
    }
  }
}
