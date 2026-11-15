using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lip.Core.Config;
using Lip.Core.PublicApi;

namespace Lip.Cli.Tui;

/// The Config tab: the configuration files of the server and of the installed mods
/// (protocol/lip-tui.md, "Configuration"). A file is read when it is selected, edited in memory,
/// and written only when the user saves it.
public sealed partial class TuiApp {
  private const string NotYetGenerated =
      "This file does not exist yet: it is generated when the server first starts.";

  private enum ConfigFileKind {
    Properties,
    Allowlist,
    Permissions,
    Json,
    Text,
    // A mod without configuration files.
    None,
  }

  /// A row of the Files pane.
  private sealed record ConfigFileItem(string Label, string? Path, ConfigFileKind Kind);

  /// A row of the Entries pane: a name, its value, and lines about it.
  private sealed record EntryRow(
      string Name, string Value, Style ValueStyle, IReadOnlyList<string> About);

  private List<ConfigFileItem> _configFiles = [];
  private string? _configFilesError;
  private bool _configFilesLoaded;
  private int _configFileSelected;
  private int _configFileTop;
  private bool _configEntriesFocused;
  private ConfigDocument? _configDocument;
  // The file whose document is shown or being read.
  private ConfigFileItem? _configDocumentItem;

  private bool ConfigDirty => _configDocument?.Dirty == true;

  private (string, string)[] ConfigHints() {
    List<(string, string)> hints = [];
    if (!_configEntriesFocused) {
      hints.AddRange([("↑/↓", "select"), ("Enter/→", "entries")]);
    } else if (_configDocument is { } document) {
      hints.AddRange(document.Hints);
      hints.Add(("Esc", document.CanGoUp ? "up" : "files"));
    }

    if (ConfigDirty) {
      hints.Add(("s", "save"));
    }

    hints.AddRange([("r", "reload"), ("Tab", "next tab"), ("q", "quit")]);
    return [.. hints];
  }

  private void LoadConfigFiles() {
    // What is read for a workspace that is no longer selected is dropped.
    LipClient client = _client;
    _ = Task.Run(async () => {
      try {
        IReadOnlyList<ModConfig> mods = await client.ListModConfigs();
        List<ConfigFileItem> files = [
          new("Game · server.properties", ServerProperties.FileName, ConfigFileKind.Properties),
          new("Game · allowlist.json", PlayerLists.AllowlistFileName, ConfigFileKind.Allowlist),
          new(
              "Game · permissions.json", PlayerLists.PermissionsFileName,
              ConfigFileKind.Permissions),
        ];
        foreach (ModConfig mod in mods) {
          if (mod.Files.Count == 0) {
            files.Add(new ConfigFileItem(mod.ModName, null, ConfigFileKind.None));
          }

          string prefix = $"mods/{mod.ModName}/";
          foreach (string file in mod.Files) {
            files.Add(new ConfigFileItem(
                $"{mod.ModName} · {file[prefix.Length..]}", file,
                file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                    ? ConfigFileKind.Json
                    : ConfigFileKind.Text));
          }
        }

        Post(() => {
          if (client == _client) {
            ConfigFilesLoaded(files, null);
          }
        });
      }
      catch (Exception ex) {
        Post(() => {
          if (client == _client) {
            ConfigFilesLoaded([], ex.Message);
          }
        });
      }
    });
  }

  private void ConfigFilesLoaded(List<ConfigFileItem> files, string? error) {
    _configFiles = files;
    _configFilesError = error;
    _configFilesLoaded = true;
    _configFileSelected = Math.Clamp(_configFileSelected, 0, Math.Max(0, files.Count - 1));
    // An edited file stays open; another one is read again.
    int open = ConfigDirty ? files.IndexOf(_configDocumentItem!) : -1;
    if (open >= 0) {
      _configFileSelected = open;
      _configDocumentItem = files[open];
    } else {
      OpenConfigFile();
    }
  }

  /// Reads the selected file into a document.
  private void OpenConfigFile() {
    ConfigFileItem? item =
        _configFileSelected < _configFiles.Count ? _configFiles[_configFileSelected] : null;
    _configDocumentItem = item;
    _configDocument = null;
    if (item is null) {
      return;
    }

    if (item.Path is null) {
      _configDocument = new MessageDocument(
          "This mod has no configuration file yet. A mod usually generates its configuration"
              + " files when the server first starts with it.");
      return;
    }

    _ = Task.Run(async () => {
      ConfigDocument document;
      try {
        string? text = await _client.ReadConfigFile(item.Path);
        document = OpenDocument(item, text);
      }
      catch (Exception ex) {
        document = new MessageDocument("Could not read the file: " + ex.Message);
      }

      Post(() => {
        if (ReferenceEquals(_configDocumentItem, item)) {
          _configDocument = document;
        }
      });
    });
  }

  private static ConfigDocument OpenDocument(ConfigFileItem item, string? text) {
    if (text is null && item.Kind is not (ConfigFileKind.Allowlist or ConfigFileKind.Permissions)) {
      return new MessageDocument(NotYetGenerated);
    }

    try {
      return item.Kind switch {
        ConfigFileKind.Properties => new PropertiesDocument(text!),
        ConfigFileKind.Allowlist => new PlayerListDocument(PlayerLists.Parse(text), true),
        ConfigFileKind.Permissions => new PlayerListDocument(PlayerLists.Parse(text), false),
        ConfigFileKind.Json => new JsonTreeDocument(JsonConfigFile.Parse(text!)),
        _ => new TextDocument(text!, null),
      };
    }
    catch (JsonException ex) {
      return new TextDocument(text!, "lip cannot read this file as JSON: " + ex.Message);
    }
  }

  /// Runs then, after asking whether to save or discard the changes of the open file when it
  /// has any.
  private void LeaveConfigFile(Action then) {
    if (!ConfigDirty) {
      then();
      return;
    }

    Push(new MessageDialog(
        this, "Unsaved changes",
        [new StyledLine($"{_configDocumentItem!.Path} has changes that are not saved.")],
        ["Save", "Discard", "Cancel"],
        choice => {
          switch (choice) {
            case 0:
              SaveConfigFile(then);
              break;
            case 1:
              _configDocument = null;
              OpenConfigFile();
              then();
              break;
          }
        }));
  }

  private void SaveConfigFile(Action? then = null) {
    if (_configDocument is not { Dirty: true } document
        || _configDocumentItem?.Path is not { } path) {
      then?.Invoke();
      return;
    }

    string text = document.Serialize();
    Start($"Save {path}", _ => _client.WriteConfigFile(path, text), _ => {
      document.MarkSaved(text);
      _status = new StyledLine(
          ServerRunning
              ? $"Saved {path}. The server is running: it takes effect after a restart."
              : $"Saved {path}.",
          Theme.Good);
      then?.Invoke();
    });
  }

  private void DrawConfig(Rect body) {
    int filesWidth = Math.Clamp(body.Width * 2 / 5, 30, 48);
    Rect files = Panel(
        _screen, new Rect(body.X, body.Y, filesWidth, body.Height), "Files",
        focused: !_configEntriesFocused);
    string title = _configDocumentItem?.Path ?? _configDocumentItem?.Label ?? "Entries";
    if (_configDocument is { Location.Length: > 0 } located) {
      title += " › " + located.Location;
    }

    if (ConfigDirty) {
      title += " (changed)";
    }

    Rect entries = Panel(
        _screen, new Rect(body.X + filesWidth, body.Y, body.Width - filesWidth, body.Height),
        title, focused: _configEntriesFocused);

    if (_configFilesError is { } error) {
      int y = files.Y;
      string text = "Could not read the workspace: " + error;
      foreach (string line in TextWidth.Wrap(text, files.Width)) {
        _screen.Write(files.X, y++, line, Theme.Bad, files.Right);
      }
    } else if (!_configFilesLoaded) {
      _screen.WriteFit(files.X, files.Y, files.Width, "Reading...", Theme.Muted);
    }

    _configFileTop = ScrollTo(_configFileSelected, _configFileTop, files.Height);
    for (int i = 0; i < files.Height && _configFileTop + i < _configFiles.Count; i++) {
      ConfigFileItem item = _configFiles[_configFileTop + i];
      int y = files.Y + i;
      _screen.WriteFit(
          files.X, y, files.Width, item.Label,
          item.Kind == ConfigFileKind.None ? Theme.Muted : default);
      if (_configFileTop + i == _configFileSelected) {
        _screen.Restyle(
            files.X - 1, y, files.Width + 2,
            _configEntriesFocused ? Theme.Strong with { Inverse = true } : Theme.Selected);
      }
    }

    DrawConfigEntries(entries);
  }

  private void DrawConfigEntries(Rect area) {
    int footer = area.Bottom - 1;
    _screen.WriteFit(
        area.X, footer, area.Width,
        ServerRunning
            ? "The server is running: saved changes take effect when it is started again."
            : "Saved changes take effect when the server is next started.",
        ServerRunning ? Theme.Warning : Theme.Muted);

    if (_configDocument is not { } document) {
      if (_configDocumentItem is not null) {
        _screen.WriteFit(area.X, area.Y, area.Width, "Reading...", Theme.Muted);
      }

      return;
    }

    int top = area.Y;
    foreach (StyledLine line in document.Message ?? []) {
      foreach (StyledLine row in TextWidth.Wrap(line, area.Width)) {
        if (top < footer - 1) {
          _screen.Write(area.X, top++, row, area.Right);
        }
      }
    }

    IReadOnlyList<EntryRow> rows = document.Rows;
    if (rows.Count == 0) {
      if (document.Message is null) {
        _screen.WriteFit(area.X, top, area.Width, document.Empty, Theme.Muted);
      }

      return;
    }

    document.Selected = Math.Clamp(document.Selected, 0, rows.Count - 1);
    List<string> about = [
      .. rows[document.Selected].About.SelectMany(line => TextWidth.Wrap(line, area.Width)),
    ];
    int aboutRows = Math.Min(about.Count, Math.Max(0, (area.Height - 4) / 3));
    int listBottom = footer - (aboutRows > 0 ? aboutRows + 2 : 1);
    int listRows = Math.Max(1, listBottom - top);
    int nameWidth = Math.Min(40, area.Width / 2);
    document.Top = ScrollTo(document.Selected, document.Top, listRows);
    for (int i = 0; i < listRows && document.Top + i < rows.Count; i++) {
      EntryRow row = rows[document.Top + i];
      int y = top + i;
      if (row.Name.Length > 0) {
        _screen.WriteFit(area.X, y, nameWidth - 1, row.Name);
        _screen.WriteFit(area.X + nameWidth, y, area.Width - nameWidth, row.Value, row.ValueStyle);
      } else {
        _screen.WriteFit(area.X, y, area.Width, row.Value, row.ValueStyle);
      }

      if (document.Top + i == document.Selected && _configEntriesFocused && document.Selectable) {
        _screen.Restyle(area.X - 1, y, area.Width + 2, Theme.Selected);
      }
    }

    if (aboutRows > 0) {
      int y = footer - aboutRows - 1;
      _screen.Write(area.X, y - 1, new string('─', area.Width), Theme.Border);
      foreach (string line in about.Take(aboutRows)) {
        _screen.Write(area.X, y++, line, Theme.Muted, area.Right);
      }
    }
  }

  private void ConfigKey(ConsoleKeyInfo key) {
    switch (key.KeyChar) {
      case 's':
        SaveConfigFile();
        return;
      case 'r':
        LeaveConfigFile(LoadConfigFiles);
        return;
    }

    if (!_configEntriesFocused) {
      int selected = _configFileSelected;
      if (MoveSelection(key, ref selected, _configFiles.Count, 10, wrap: true)) {
        if (selected != _configFileSelected) {
          LeaveConfigFile(() => {
            _configFileSelected = selected;
            OpenConfigFile();
          });
        }

        return;
      }

      if (key.Key is ConsoleKey.Enter or ConsoleKey.Spacebar or ConsoleKey.RightArrow
          && _configDocument is not null) {
        _configEntriesFocused = true;
      }

      return;
    }

    if (_configDocument is not { } document) {
      _configEntriesFocused = false;
      return;
    }

    bool back = key.Key is ConsoleKey.Escape or ConsoleKey.LeftArrow or ConsoleKey.Backspace;
    if (back) {
      if (!document.GoUp()) {
        _configEntriesFocused = false;
      }

      return;
    }

    if (MoveSelection(key, ref document.Selected, document.Rows.Count, 10, wrap: true)) {
      return;
    }

    document.OnKey(this, key);
  }

  /// A file of the Config tab as it is edited.
  private abstract class ConfigDocument {
    public int Selected;
    public int Top;
    private bool? _dirty;

    public virtual bool Dirty => false;

    public virtual bool Selectable => true;

    /// Where in the file the rows are, for a file with levels.
    public virtual string Location => "";

    public virtual bool CanGoUp => false;

    /// Lines shown above the rows, or instead of them.
    public virtual IReadOnlyList<StyledLine>? Message => null;

    public virtual string Empty => "The file has no entry.";

    public virtual IReadOnlyList<EntryRow> Rows => [];

    public virtual (string, string)[] Hints => [("↑/↓", "select")];

    /// Goes up a level; false at the top.
    public virtual bool GoUp() => false;

    public virtual void OnKey(TuiApp app, ConsoleKeyInfo key) {
    }

    public virtual string Serialize() => throw new InvalidOperationException();

    /// The document is now what text holds.
    public virtual void MarkSaved(string text) {
    }

    /// Whether compute says the document has changes, computed again only after Touch: the
    /// screen asks several times a frame.
    protected bool CachedDirty(Func<bool> compute) => _dirty ??= compute();

    /// The document changed.
    protected void Touch() => _dirty = null;

    /// Asks for a value with a single field.
    protected static void Prompt(
        TuiApp app, string title, string label, string value, Action<string> submit) =>
        app.Push(new PromptDialog(app, title, [new PromptField(label, value)], v => submit(v[0])));

    protected static void Warn(TuiApp app, string message) =>
        app._status = new StyledLine(message, Theme.Warning);
  }

  /// Something to say instead of a file: that it does not exist, or could not be read.
  private sealed class MessageDocument(string message) : ConfigDocument {
    public override IReadOnlyList<StyledLine>? Message => [new StyledLine(message, Theme.Muted)];

    public override bool Selectable => false;

    public override (string, string)[] Hints => [];
  }

  /// A file that is shown and not edited.
  private sealed class TextDocument(string text, string? problem) : ConfigDocument {
    private readonly List<EntryRow> _rows = [
      .. text.Split('\n').Select(line =>
          new EntryRow("", line.TrimEnd('\r'), default, [])),
    ];

    public override IReadOnlyList<StyledLine>? Message => [
      new StyledLine(
          problem ?? "This file is shown as it is; edit it with a text editor.", Theme.Muted),
      new StyledLine(),
    ];

    public override bool Selectable => false;

    public override IReadOnlyList<EntryRow> Rows => _rows;

    public override (string, string)[] Hints => [("↑/↓", "scroll")];
  }

  /// server.properties: each property with the editor of its type.
  private sealed class PropertiesDocument(string text) : ConfigDocument {
    private ServerProperties _properties = ServerProperties.Parse(text);

    public override bool Dirty => _properties.Changed;

    public override IReadOnlyList<EntryRow> Rows => [
      .. _properties.Properties.Select(p => new EntryRow(
          p.Key,
          p.Value.Length == 0 ? "(empty)" : p.Value,
          p.Changed ? Theme.Accent : p.Value.Length == 0 ? Theme.Muted : default,
          [.. p.Comment.Where(line => line.Length > 0)])),
    ];

    public override string Empty => "The file has no property.";

    public override (string, string)[] Hints {
      get {
        ServerProperty? property = Current;
        string edit = property?.Type.Kind switch {
          PropertyKind.Boolean => "toggle",
          PropertyKind.Choice => "choose",
          _ => "edit",
        };
        return property is { Changed: true }
            ? [("Enter", edit), ("Del", "undo the change")]
            : [("Enter", edit)];
      }
    }

    private ServerProperty? Current =>
        Selected < _properties.Properties.Count ? _properties.Properties[Selected] : null;

    public override void OnKey(TuiApp app, ConsoleKeyInfo key) {
      if (Current is not { } property) {
        return;
      }

      if (key.Key == ConsoleKey.Delete) {
        ServerProperties.Reset(property);
        return;
      }

      if (key.Key is not (ConsoleKey.Enter or ConsoleKey.Spacebar)) {
        return;
      }

      PropertyType type = property.Type;
      switch (type.Kind) {
        case PropertyKind.Boolean:
          _properties.Set(property, property.Value == "true" ? "false" : "true");
          break;
        case PropertyKind.Choice:
          app.Push(new ListDialog(
              app, property.Key,
              [
                .. type.Choices.Select(choice => new StyledLine(
                    choice, choice == property.Value ? Theme.Accent : default)),
              ],
              chosen => _properties.Set(property, type.Choices[chosen])));
          break;
        default:
          string label = type.Kind == PropertyKind.Integer
              ? $"The value of {property.Key}, an integer"
              : $"The value of {property.Key}";
          Prompt(app, $"Set {property.Key}", label, property.Value, value => {
            if (type.Check(value) is { } error) {
              Warn(app, $"{property.Key} is not set: {error}");
            } else {
              _properties.Set(property, value);
            }
          });
          break;
      }
    }

    public override string Serialize() => _properties.Serialize();

    public override void MarkSaved(string text) {
      int selected = Selected;
      _properties = ServerProperties.Parse(text);
      Selected = selected;
    }
  }

  /// allowlist.json or permissions.json: the players, added, removed and changed.
  private sealed class PlayerListDocument(JsonArray players, bool allowlist) : ConfigDocument {
    private string _saved = JsonConfigFile.Serialize(players);

    public override bool Dirty => CachedDirty(() => JsonConfigFile.Serialize(players) != _saved);

    public override string Empty =>
        allowlist ? "No player is on the allowlist. Press a to add one."
            : "No player has a permission of their own. Press a to add one.";

    public override IReadOnlyList<EntryRow> Rows => [
      .. players.Select(player => allowlist
          ? new EntryRow(
              PlayerLists.Text(player, "name"),
              (PlayerLists.Text(player, "xuid") is { Length: > 0 } xuid ? "xuid " + xuid : "")
                  + (IgnoresLimit(player) ? "  ignores the player limit" : ""),
              default,
              ["Enter: whether the player may join when the server is full."])
          : new EntryRow(
              PlayerLists.Text(player, "xuid"), PlayerLists.Text(player, "permission"), default,
              ["The permission of the player with this xuid: operator, member or visitor."])),
    ];

    public override (string, string)[] Hints =>
        players.Count == 0
            ? [("a", "add")]
            : [("Enter", allowlist ? "toggle the limit" : "permission"), ("a", "add"),
              ("Del", "remove")];

    private static bool IgnoresLimit(JsonNode? player) =>
        player is JsonObject o && o["ignoresPlayerLimit"]?.GetValueKind() == JsonValueKind.True;

    public override void OnKey(TuiApp app, ConsoleKeyInfo key) {
      JsonNode? player = Selected < players.Count ? players[Selected] : null;
      if (key.KeyChar == 'a') {
        Add(app);
        return;
      }

      if (player is not JsonObject entry) {
        return;
      }

      switch (key.Key) {
        case ConsoleKey.Delete:
          players.RemoveAt(Selected);
          Touch();
          break;
        case ConsoleKey.Enter or ConsoleKey.Spacebar when allowlist:
          entry["ignoresPlayerLimit"] = !IgnoresLimit(entry);
          Touch();
          break;
        case ConsoleKey.Enter or ConsoleKey.Spacebar:
          ChoosePermission(app, PlayerLists.Text(entry, "xuid"), permission => {
            entry["permission"] = permission;
            Touch();
          });
          break;
      }
    }

    private void Add(TuiApp app) {
      if (allowlist) {
        app.Push(new PromptDialog(
            app, "Add a player to the allowlist",
            [
              new PromptField("The name of the player (gamertag)"),
              new PromptField("The xuid of the player; may be left empty"),
            ],
            values => {
              if (values[0].Length == 0) {
                Warn(app, "No player was added: the name is empty.");
                return;
              }

              PlayerLists.Allow(players, values[0], values[1]);
              Selected = players.Count - 1;
              Touch();
            }));
        return;
      }

      Prompt(app, "Give a player a permission", "The xuid of the player", "", xuid => {
        if (xuid.Length == 0) {
          Warn(app, "No player was added: the xuid is empty.");
          return;
        }

        ChoosePermission(app, xuid, permission => {
          PlayerLists.SetPermission(players, xuid, permission);
          Selected = players.IndexOf(players.First(p => PlayerLists.Text(p, "xuid") == xuid));
          Touch();
        });
      });
    }

    private static void ChoosePermission(TuiApp app, string xuid, Action<string> chosen) =>
        app.Push(new ListDialog(
            app, $"Permission of {xuid}",
            [.. PlayerLists.Permissions.Select(p => new StyledLine(p))],
            i => chosen(PlayerLists.Permissions[i])));

    public override string Serialize() => JsonConfigFile.Serialize(players);

    public override void MarkSaved(string text) {
      _saved = text;
      Touch();
    }
  }

  /// A JSON file of a mod, a tree of objects and arrays whose values are edited in place.
  private sealed class JsonTreeDocument(JsonNode root) : ConfigDocument {
    private readonly List<(JsonNode Node, string Name, int Selected)> _path = [];
    private string _saved = JsonConfigFile.Serialize(root);

    public override bool Dirty => CachedDirty(() => JsonConfigFile.Serialize(root) != _saved);

    private JsonNode Current => _path.Count == 0 ? root : _path[^1].Node;

    public override string Location => string.Join(" › ", _path.Select(level => level.Name));

    public override bool CanGoUp => _path.Count > 0;

    public override string Empty =>
        Current is JsonArray ? "The array is empty. Press a to add a value." : "Nothing here.";

    public override IReadOnlyList<EntryRow> Rows => Current switch {
      JsonObject o => [.. o.Select(member => Row(member.Key, member.Value, o))],
      JsonArray a => [.. a.Select((value, i) => Row($"[{i}]", value, null))],
      _ => [Row("(value)", Current, null)],
    };

    public override (string, string)[] Hints {
      get {
        List<(string, string)> hints = [("Enter", "open or edit")];
        if (Current is JsonArray array) {
          hints.Add(("a", "add"));
          if (array.Count > 0) {
            hints.Add(("Del", "remove"));
          }
        }

        return [.. hints];
      }
    }

    private static EntryRow Row(string name, JsonNode? value, JsonObject? container) {
      bool readOnly = JsonConfigFile.IsReadOnly(container, name);
      (string text, Style style) = value switch {
        JsonObject o => ($"{{…}} {Count(o.Count, "member")}", Theme.Accent),
        JsonArray a => ($"[…] {Count(a.Count, "value")}", Theme.Accent),
        null => ("null", Theme.Muted),
        _ when value.GetValueKind() == JsonValueKind.String =>
            ("\"" + value.GetValue<string>() + "\"", default),
        _ => (value.ToJsonString(), Theme.Strong),
      };
      return new EntryRow(
          name, text, readOnly ? Theme.Muted : style,
          readOnly ? ["The version of the file, which the mod keeps up to date itself."] : []);
    }

    private static string Count(int count, string what) =>
        string.Create(
            CultureInfo.InvariantCulture, $"{count} {what}{(count == 1 ? "" : "s")}");

    public override bool GoUp() {
      if (_path.Count == 0) {
        return false;
      }

      Selected = _path[^1].Selected;
      _path.RemoveAt(_path.Count - 1);
      return true;
    }

    public override void OnKey(TuiApp app, ConsoleKeyInfo key) {
      JsonNode current = Current;
      if (key.KeyChar == 'a' && current is JsonArray added) {
        Prompt(app, "Add a value", "The value, as JSON; any other text is a string", "", text => {
          added.Add(JsonConfigFile.ParseValue(text));
          Selected = added.Count - 1;
          Touch();
        });
        return;
      }

      if (key.Key == ConsoleKey.Delete && current is JsonArray removed
          && Selected < removed.Count) {
        removed.RemoveAt(Selected);
        Touch();
        return;
      }

      if (key.Key is not (ConsoleKey.Enter or ConsoleKey.Spacebar)) {
        return;
      }

      (string name, JsonNode? value, Action<JsonNode?> set) = current switch {
        JsonObject o when Selected < o.Count => Member(o, o.GetAt(Selected).Key),
        JsonArray a when Selected < a.Count => Element(a, Selected),
        JsonObject or JsonArray => ("", null, _ => { }),
        _ => ("(value)", current, (Action<JsonNode?>)(v => root = v ?? JsonValue.Create("")!)),
      };
      if (name.Length == 0) {
        return;
      }

      if (current is JsonObject parent && JsonConfigFile.IsReadOnly(parent, name)) {
        Warn(app, $"\"{name}\" is the version of the file, which the mod keeps up to date.");
        return;
      }

      switch (value) {
        case JsonObject or JsonArray:
          _path.Add((value, name, Selected));
          Selected = 0;
          return;
        case JsonValue v when v.GetValueKind() is JsonValueKind.True or JsonValueKind.False:
          set(JsonValue.Create(v.GetValueKind() != JsonValueKind.True));
          Touch();
          return;
      }

      Prompt(app, $"Set {name}", $"The value of {name}", JsonConfigFile.EditText(value), text => {
        if (JsonConfigFile.TryEdit(value, text, out JsonNode? edited, out string? error)) {
          set(edited);
          Touch();
        } else {
          Warn(app, $"{name} is not set: {error}");
        }
      });
    }

    private static (string, JsonNode?, Action<JsonNode?>) Member(JsonObject o, string name) =>
        (name, o[name], value => o[name] = value);

    private static (string, JsonNode?, Action<JsonNode?>) Element(JsonArray a, int index) =>
        ($"[{index}]", a[index], value => a[index] = value);

    public override string Serialize() => JsonConfigFile.Serialize(root);

    public override void MarkSaved(string text) {
      _saved = text;
      Touch();
    }
  }
}
