using System.IO.Abstractions;
using System.Text.Json;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod;

namespace Lip.Cli.Tui;

/// The Settings tab (lip config and lip cache clean) and the Develop tab (lip pkg).
public sealed partial class TuiApp {
  private static readonly (string, string)[] SettingsHints = [
    ("Enter", "edit"), ("Del", "reset to default"), ("c", "clean the cache"),
    ("Tab", "next tab"), ("q", "quit"),
  ];

  private static readonly (string, string)[] DevelopHints = [
    ("Enter", "run"), ("↑/↓", "select"), ("Tab", "next tab"), ("q", "quit"),
  ];

  private static readonly (string Name, string Command, string About)[] _developActions = [
    ("Create lip.toml", "lip pkg init",
        "Writes a lip.toml for a new package in this directory, asking for its package id."),
    ("Migrate tooth.json", "lip pkg migrate",
        "Migrates the tooth.json in this directory to a lip.toml, asking for what is missing."),
    ("Pack a ZIP archive", "lip pkg pack",
        "Packs a directory into the ZIP archive of a package."),
    ("View a package", "lip pkg view",
        "Shows the package file and the versions of a package, as JSON."),
  ];

  private static readonly JsonSerializerOptions _indented = new() {
    WriteIndented = true,
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
  };

  private List<KeyValuePair<string, string>> _config = [];
  private string? _configError;
  private int _configSelected;
  private int _developSelected;

  private void LoadConfig() {
    _ = Task.Run(async () => {
      try {
        IDictionary<string, string> values = await _client.ConfigList();
        Post(() => {
          _config = [.. values.OrderBy(v => v.Key, StringComparer.Ordinal)];
          _configError = null;
          _configSelected = Math.Clamp(_configSelected, 0, Math.Max(0, _config.Count - 1));
        });
      }
      catch (Exception ex) {
        Post(() => _configError = ex.Message);
      }
    });
  }

  private void DrawSettings(Rect body) {
    int configWidth = Math.Max(30, body.Width * 3 / 5);
    Rect config = Panel(
        _screen, new Rect(body.X, body.Y, configWidth, body.Height), "Configuration",
        focused: true);
    Rect cache = Panel(
        _screen, new Rect(body.X + configWidth, body.Y, body.Width - configWidth, body.Height),
        "Cache");

    int keyWidth = Math.Min(24, config.Width / 2);
    _screen.Write(config.X, config.Y, "Key", Theme.Muted);
    _screen.Write(config.X + keyWidth, config.Y, "Value", Theme.Muted);
    for (int i = 0; i < _config.Count && config.Y + 1 + i < config.Bottom - 2; i++) {
      int y = config.Y + 1 + i;
      _screen.WriteFit(config.X, y, keyWidth - 1, _config[i].Key);
      _screen.WriteFit(
          config.X + keyWidth, y, config.Width - keyWidth,
          _config[i].Value.Length == 0 ? "(not set)" : _config[i].Value,
          _config[i].Value.Length == 0 ? Theme.Muted : default);
      if (i == _configSelected) {
        _screen.Restyle(config.X - 1, y, config.Width + 2, Theme.Selected);
      }
    }

    if (_configError is { } error) {
      int y = config.Y + 3;
      foreach (string line in TextWidth.Wrap(error, config.Width)) {
        _screen.Write(config.X, y++, line, Theme.Bad, config.Right);
      }
    }

    _screen.WriteFit(
        config.X, config.Bottom - 1, config.Width,
        Path.Join(LipDirectories.Config, "config.toml"), Theme.Muted);

    int row = cache.Y;
    foreach (string line in TextWidth.Wrap(
        "Downloaded files are kept in the cache. Cleaning it deletes them all; workspaces and"
            + " the configuration are not affected.",
        cache.Width)) {
      _screen.Write(cache.X, row++, line, default, cache.Right);
    }

    row++;
    foreach (string line in TextWidth.Wrap(LipDirectories.Cache, cache.Width)) {
      _screen.Write(cache.X, row++, line, Theme.Muted, cache.Right);
    }

    _screen.Write(cache.X, row + 1, "[ c  Clean the cache ]", Theme.Strong, cache.Right);
  }

  private void SettingsKey(ConsoleKeyInfo key) {
    if (MoveSelection(key, ref _configSelected, _config.Count, 10)) {
      return;
    }

    if (key.KeyChar == 'c') {
      Push(new MessageDialog(
          this, "Clean the cache",
          [new StyledLine($"Delete {LipDirectories.Cache} and everything in it?")],
          ["Clean", "Cancel"],
          choice => {
            if (choice == 0) {
              Start("Clean the cache", _ => _client.CacheClean(), done =>
                  ShowResult("Clean the cache", done, "The cache is clean."));
            }
          }));
      return;
    }

    if (_configSelected >= _config.Count) {
      return;
    }

    (string name, string value) = _config[_configSelected];
    switch (key.Key) {
      case ConsoleKey.Delete:
        Start($"Reset {name}", _ => _client.ConfigDelete(name), _ => LoadConfig());
        break;
      case ConsoleKey.Enter:
        Push(new PromptDialog(
            this, $"Set {name}",
            [new PromptField($"The value of {name}; empty resets it to its default", value)],
            values => Start(
                $"Set {name}",
                _ => values[0].Length == 0
                    ? _client.ConfigDelete(name)
                    : _client.ConfigSet(name, values[0]),
                _ => LoadConfig())));
        break;
    }
  }

  private void DrawDevelop(Rect body) {
    int actionsWidth = Math.Min(34, body.Width / 2);
    Rect actions = Panel(
        _screen, new Rect(body.X, body.Y, actionsWidth, body.Height), "Package development",
        focused: true);
    (string name, string command, string about) = _developActions[_developSelected];
    Rect details = Panel(
        _screen, new Rect(body.X + actionsWidth, body.Y, body.Width - actionsWidth, body.Height),
        name);

    for (int i = 0; i < _developActions.Length; i++) {
      _screen.WriteFit(actions.X, actions.Y + i, actions.Width, _developActions[i].Name);
      if (i == _developSelected) {
        _screen.Restyle(actions.X - 1, actions.Y + i, actions.Width + 2, Theme.Selected);
      }
    }

    int y = details.Y;
    _screen.Write(details.X, y++, command, Theme.Accent, details.Right);
    y++;
    foreach (string line in TextWidth.Wrap(about, details.Width)) {
      _screen.Write(details.X, y++, line, default, details.Right);
    }

    y++;
    _screen.WriteFit(details.X, y++, details.Width, _workspace, Theme.Muted);
    foreach (string file in (string[])[PackageFormat.FileName, "tooth.json"]) {
      bool exists = File.Exists(Path.Join(_workspace, file));
      _screen.Write(
          details.X, y++, $"{file}: {(exists ? "found" : "none")}",
          exists ? Theme.Good : Theme.Muted, details.Right);
    }
  }

  private void DevelopKey(ConsoleKeyInfo key) {
    if (MoveSelection(key, ref _developSelected, _developActions.Length, 10)
        || key.Key != ConsoleKey.Enter) {
      return;
    }

    switch (_developSelected) {
      case 0:
        Start("Create lip.toml", _ => _client.Init(), done =>
            ShowResult("Create lip.toml", done, $"Wrote {PackageFormat.FileName}."));
        break;
      case 1:
        Start("Migrate tooth.json", _ => _client.Migrate(), done =>
            ShowResult("Migrate tooth.json", done, $"Wrote {PackageFormat.FileName}."));
        break;
      case 2:
        Push(new PromptDialog(
            this, "Pack a ZIP archive",
            [
              new PromptField("The directory whose files go into the archive", "."),
              new PromptField("The ZIP archive to write (-o)", "", "package.zip"),
            ],
            values => {
              if (values[1].Length == 0) {
                _status = new StyledLine("Name the ZIP archive to write.", Theme.Warning);
                return;
              }

              string directory = values[0].Length == 0 ? "." : values[0];
              Start(
                  "Pack a ZIP archive",
                  _ => Packer.WriteArchive(new FileSystem(), directory, values[1]),
                  done => ShowResult("Pack a ZIP archive", done, $"Packed {values[1]}."));
            }));
        break;
      default:
        Push(new PromptDialog(
            this, "View a package",
            [new PromptField("The package", "", "LeviLamina@1.9.7  ./mod.zip")],
            values => {
              if (values[0].Length == 0) {
                return;
              }

              string json = "";
              Start(
                  $"View {values[0]}",
                  async _ => json = await _client.View(values[0]),
                  _ => Push(new MessageDialog(
                      this, values[0],
                      [.. Indent(json).Split('\n').Select(line => new StyledLine(line))],
                      ["Close"])));
            }));
        break;
    }
  }

  private static string Indent(string json) {
    try {
      using JsonDocument document = JsonDocument.Parse(json);
      return JsonSerializer.Serialize(document.RootElement, _indented);
    }
    catch (JsonException) {
      return json;
    }
  }
}
