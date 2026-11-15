using System.IO.Abstractions;
using System.Text.Json;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod;
using Lip.Core.Services;

namespace Lip.Cli.Tui;

/// The Settings tab (lip config and lip cache clean) and the Develop tab (lip pkg).
public sealed partial class TuiApp {
  private (string, string)[] SettingsHints =>
      _cacheFocused
          ? [("Enter", "clean the cache"), ("←", "configuration"), ("Tab", "next tab"), ("q", "quit")]
          : [
            ("Enter", "edit"), ("Del", "reset"), ("→", "cache"),
            ("c", "clean the cache"), ("Tab", "next tab"), ("q", "quit"),
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

  // The keys of the user configuration, then those of the workspace configuration
  // (protocol/config.toml.md), with their values: null when a key has none.
  private List<ConfigRow> _config = [];
  private string? _configError;
  private int _configSelected;
  // Whether the keys of the Settings tab go to its Cache pane.
  private bool _cacheFocused;
  private int _developSelected;

  private sealed record ConfigRow(bool Workspace, string Key, string? Value);

  private void LoadConfig() {
    _ = Task.Run(async () => {
      try {
        IDictionary<string, string?> user = await _client.ConfigList();
        IDictionary<string, string?> workspace = await _client.ConfigList(workspace: true);
        Post(() => {
          _config = [
            .. user.OrderBy(v => v.Key, StringComparer.Ordinal)
                .Select(v => new ConfigRow(false, v.Key, v.Value)),
            .. workspace.OrderBy(v => v.Key, StringComparer.Ordinal)
                .Select(v => new ConfigRow(true, v.Key, v.Value)),
          ];
          _configError = null;
          _configSelected = Math.Clamp(_configSelected, 0, Math.Max(0, _config.Count - 1));
        });
      }
      catch (Exception ex) {
        Post(() => _configError = ex.Message);
      }
    });
  }

  /// A value as the Settings tab shows it: a key without a value and an empty github_proxy, with
  /// which lip connects to GitHub directly, are told apart.
  private static (string Text, Style Style) DescribeValue(ConfigRow row) =>
      row.Value switch {
        null => ("(not set)", Theme.Muted),
        "" when row.Key == ConfigService.GithubProxyKey =>
            ("(empty: connect to GitHub directly)", Theme.Muted),
        "" => ("(empty)", Theme.Muted),
        _ => (row.Value, default),
      };

  private void DrawSettings(Rect body) {
    int configWidth = Math.Max(30, body.Width * 3 / 5);
    Rect config = Panel(
        _screen, new Rect(body.X, body.Y, configWidth, body.Height), "Configuration",
        focused: !_cacheFocused);
    Rect cache = Panel(
        _screen, new Rect(body.X + configWidth, body.Y, body.Width - configWidth, body.Height),
        "Cache", focused: _cacheFocused);

    int keyWidth = Math.Min(24, config.Width / 2);
    int y = config.Y;
    for (int i = 0; i < _config.Count && y < config.Bottom; i++) {
      if (i == 0 || _config[i].Workspace != _config[i - 1].Workspace) {
        if (i > 0) {
          y++;
        }

        string title = _config[i].Workspace
            ? $"Workspace · {Path.Join(_workspace, ".lip", "config.toml")}"
            : $"User · {Path.Join(LipDirectories.Config, "config.toml")}";
        _screen.WriteFit(config.X, y++, config.Width, title, Theme.Muted);
        if (y >= config.Bottom) {
          break;
        }
      }

      (string text, Style style) = DescribeValue(_config[i]);
      _screen.WriteFit(config.X, y, keyWidth - 1, _config[i].Key);
      _screen.WriteFit(config.X + keyWidth, y, config.Width - keyWidth, text, style);
      if (i == _configSelected && !_cacheFocused) {
        _screen.Restyle(config.X - 1, y, config.Width + 2, Theme.Selected);
      }

      y++;
    }

    if (_configError is { } error) {
      y++;
      foreach (string line in TextWidth.Wrap(error, config.Width)) {
        if (y >= config.Bottom) {
          break;
        }

        _screen.Write(config.X, y++, line, Theme.Bad, config.Right);
      }
    }

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

    _screen.Write(
        cache.X, row + 1, "[ Clean the cache ]", _cacheFocused ? Theme.Selected : Theme.Strong,
        cache.Right);
  }

  private void SettingsKey(ConsoleKeyInfo key) {
    if (key.KeyChar == 'c') {
      CleanCache();
      return;
    }

    if (_cacheFocused) {
      switch (key.Key) {
        case ConsoleKey.Enter or ConsoleKey.Spacebar:
          CleanCache();
          break;
        case ConsoleKey.LeftArrow or ConsoleKey.Escape:
          _cacheFocused = false;
          break;
      }

      return;
    }

    if (key.Key == ConsoleKey.RightArrow) {
      _cacheFocused = true;
      return;
    }

    if (MoveSelection(key, ref _configSelected, _config.Count, 10, wrap: true)) {
      return;
    }

    if (_configSelected >= _config.Count) {
      return;
    }

    (bool workspace, string name, string? value) = _config[_configSelected];
    switch (key.Key) {
      case ConsoleKey.Delete:
        Start(
            $"Reset {name}", _ => _client.ConfigDelete(name, workspace), _ => LoadConfig());
        break;
      case ConsoleKey.Enter or ConsoleKey.Spacebar when name == ConfigService.RestartKey:
        string[] choices = [ConfigService.RestartNever, ConfigService.RestartOnFailure];
        Push(new ListDialog(
            this, $"Set {name}",
            [
              new StyledLine("never: the server is not restarted when it exits"),
              new StyledLine("on-failure: the server is restarted when it fails"),
            ],
            choice => Start(
                $"Set {name}",
                _ => _client.ConfigSet(name, choices[choice], workspace),
                _ => LoadConfig())));
        break;
      case ConsoleKey.Enter or ConsoleKey.Spacebar:
        Push(new PromptDialog(
            this, $"Set {name}",
            [
              new PromptField(
                  name == ConfigService.GithubProxyKey
                      ? "A URL prefix for GitHub; empty connects to GitHub directly"
                      : $"The value of {name}",
                  value ?? ""),
            ],
            values => Start(
                $"Set {name}",
                _ => _client.ConfigSet(name, values[0], workspace),
                _ => LoadConfig())));
        break;
    }
  }

  private void CleanCache() {
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
    if (MoveSelection(key, ref _developSelected, _developActions.Length, 10, wrap: true)
        || key.Key is not (ConsoleKey.Enter or ConsoleKey.Spacebar)) {
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
