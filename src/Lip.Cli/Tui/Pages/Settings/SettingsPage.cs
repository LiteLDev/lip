using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Widgets;
using Lip.Core.Infrastructure;
using Lip.Core.PublicApi;
using Lip.Core.Services;
using Semver;

namespace Lip.Cli.Tui.Pages.Settings;

/// The Settings page: one list of settings, each with its value, and one sentence about the
/// selected one below the list. It covers lip config for every key (language, github_proxy,
/// restart), lip cache clean, lip self-update, lip daemon status and the version of lip.
public sealed class SettingsPage : Page {
  /// The rows of the list, in their order.
  private enum Row {
    Language,
    Proxy,
    Restart,
    Cache,
    CheckUpdates,
    InstallUpdate,
    Daemon,
    About,
  }

  /// What is known of the releases of lip.
  private enum UpdateState {
    Unknown,
    UpToDate,
    Available,
    Installed,
    InstallerStarted,
  }

  private static readonly Row[] _allRows = Enum.GetValues<Row>();

  private readonly ListView<Row> _list;

  // The value of github_proxy: null when it has none, "" for a direct connection.
  private string? _proxy;
  private bool _proxyLoaded;
  private string? _proxyError;

  // Whether restart is on-failure in the selected workspace.
  private bool _restart;
  private bool _restartLoaded;
  private string? _restartError;

  // Counts what makes a value that is still being read stale: another read, or a change.
  private int _reads;

  // The changes of the configuration, which are written one at a time, in their order.
  private Task _changes = Task.CompletedTask;

  private UpdateState _update;
  private SemVersion? _latest;

  private DaemonState? _daemon;
  private bool _daemonLoaded;
  private string? _daemonError;
  private int _daemonReads;

  private string? _cachePath;

  // Where the values started, and how many columns they had, when the page was last drawn.
  private int _labelWidth;
  private int _valueWidth = int.MaxValue;

  public SettingsPage(AppContext context) : base(context) {
    _list = new ListView<Row>(Render) { Wrap = true };
    ShowRows();
  }

  public override string Title => SettingsText.Title;

  public override IReadOnlyList<Hint> Hints =>
      !_list.HasCurrent ? [] : _list.Current switch {
        Row.Language => [CommonText.HintMove, CommonText.HintSelect],
        Row.Proxy => [CommonText.HintMove, CommonText.HintActions],
        Row.Restart => [CommonText.HintMove, new Hint("Enter/Space", CommonText.HintToggle.Text)],
        Row.Daemon => [CommonText.HintMove, new Hint("Enter", SettingsText.Refresh)],
        Row.About => [CommonText.HintMove],
        _ => [CommonText.HintMove, CommonText.HintSelect],
      };

  public override void OnShown() {
    LoadConfig();
    LoadDaemon();
  }

  public override void OnWorkspaceChanged() {
    _restartLoaded = false;
    _restartError = null;
    if (Context.CurrentPage == PageId.Settings) {
      LoadConfig();
    }
  }

  public override void OnRequest(string request) {
    if (request == PageRequest.GithubProxy) {
      _list.Select(row => row == Row.Proxy);
      PromptProxy(_proxy ?? "");
    }
  }

  public override void Draw(ScreenBuffer screen, Rect area, bool focused) {
    if (area.Width < 4 || area.Height < 1) {
      return;
    }

    // After the widest label, whichever rows are shown, so that the values do not move.
    _labelWidth = _allRows.Max(row => TextWidth.Of(Label(row))) + 3;
    _valueWidth = Math.Max(1, area.Width - _labelWidth - 1);

    // The details area keeps four lines below its rule; the list scrolls when it must.
    int listHeight = Math.Min(_list.Items.Count, Math.Max(1, area.Height - 5));
    _list.Draw(screen, new Rect(area.X, area.Y, area.Width, listHeight), focused);
    if (!_list.HasCurrent) {
      return;
    }

    // The details area: one sentence about the selected row, then what its value leaves out.
    int y = area.Y + listHeight;
    if (y >= area.Bottom) {
      return;
    }

    screen.Write(area.X, y++, new string('─', area.Width), Theme.Border, area.Right);
    int width = area.Width - 2;
    foreach (StyledLine line in Details(_list.Current, width)) {
      if (y >= area.Bottom) {
        break;
      }

      screen.Write(area.X + 1, y++, line, area.Right - 1);
    }
  }

  public override bool OnKey(ConsoleKeyInfo key) {
    if (!_list.HasCurrent) {
      return false;
    }

    Row row = _list.Current;
    if (key.Key == ConsoleKey.Enter || (key.Key == ConsoleKey.Spacebar && row == Row.Restart)) {
      return Activate(row);
    }

    return _list.OnKey(key);
  }

  #region Rows

  private static string Label(Row row) =>
      row switch {
        Row.Language => SettingsText.Language,
        Row.Proxy => SettingsText.GithubProxy,
        Row.Restart => SettingsText.AutoRestart,
        Row.Cache => SettingsText.ClearCache,
        Row.CheckUpdates => SettingsText.CheckUpdates,
        Row.InstallUpdate => SettingsText.InstallUpdate,
        Row.Daemon => SettingsText.Daemon,
        _ => SettingsText.About,
      };

  private static string Detail(Row row) =>
      row switch {
        Row.Language => SettingsText.LanguageDetail,
        Row.Proxy => SettingsText.GithubProxyDetail,
        Row.Restart => SettingsText.AutoRestartDetail,
        Row.Cache => SettingsText.ClearCacheDetail,
        Row.CheckUpdates => SettingsText.CheckUpdatesDetail,
        Row.InstallUpdate => SettingsText.InstallUpdateDetail,
        Row.Daemon => SettingsText.DaemonDetail,
        _ => SettingsText.AboutDetail,
      };

  /// The cache directory of lip.
  private string CachePath => _cachePath ??= LipDirectories.Cache;

  /// Shows the rows that there are now: Install update only while a newer release is known.
  private void ShowRows() {
    Row? current = _list.HasCurrent ? _list.Current : null;
    _list.Items = [
      .. _allRows.Where(row => row != Row.InstallUpdate || _update == UpdateState.Available),
    ];
    if (current is { } selected && !_list.Select(row => row == selected)) {
      // Install update was selected and is gone.
      _list.Select(row => row == Row.CheckUpdates);
    }
  }

  /// The value of a row, whatever its width.
  private StyledLine Value(Row row) {
    switch (row) {
      case Row.Language:
        return new StyledLine(
            Context.LanguageSetting is { } language
                ? Languages.NativeName(language)
                : SettingsText.FollowSystem);
      case Row.Proxy:
        if (!_proxyLoaded) {
          return Pending(_proxyError);
        }

        return _proxy switch {
          null => new StyledLine(SettingsText.NotSet, Theme.Muted),
          "" => new StyledLine(SettingsText.Direct),
          _ => new StyledLine(_proxy),
        };
      case Row.Restart:
        if (!_restartLoaded) {
          return Pending(_restartError);
        }

        return _restart
            ? new StyledLine().Add("[x] ", Theme.Good).Add(SettingsText.On)
            : new StyledLine().Add("[ ] ", Theme.Muted).Add(SettingsText.Off);
      case Row.Cache:
        return new StyledLine(CachePath);
      case Row.CheckUpdates:
        string latest = _latest?.ToString() ?? Context.Version;
        return _update switch {
          UpdateState.UpToDate =>
              new StyledLine($"{Context.Version} · ").Add(SettingsText.UpToDate, Theme.Good),
          UpdateState.Available =>
              new StyledLine($"{Context.Version} → ").Add(latest, Theme.Warning),
          UpdateState.Installed =>
              new StyledLine($"{latest} · ").Add(SettingsText.RestartLip, Theme.Warning),
          UpdateState.InstallerStarted =>
              new StyledLine($"{latest} · ").Add(SettingsText.InstallerRunning, Theme.Warning),
          _ => new StyledLine(Context.Version),
        };
      case Row.InstallUpdate:
        return new StyledLine(_latest?.ToString() ?? "", Theme.Warning);
      case Row.Daemon:
        if (!_daemonLoaded) {
          return Pending(_daemonError);
        }

        return _daemon is { } daemon
            ? new StyledLine(CommonText.ServerRunning, Theme.Good)
                .Add($" · {daemon.Version} · PID {daemon.ProcessId}")
            : new StyledLine(SettingsText.NotRunning, Theme.Muted);
      default:
        return new StyledLine($"lip {Context.Version}");
    }
  }

  /// The value of a row that is being read, or that could not be read.
  private static StyledLine Pending(string? error) =>
      error is null
          ? new StyledLine(CommonText.Loading, Theme.Muted)
          : new StyledLine(SettingsText.Unreadable, Theme.Bad);

  /// A row of the list: its label, and its value cut to the room that it has.
  private StyledLine Render(Row row) {
    string label = Label(row);
    int padding = Math.Max(1, _labelWidth - 1 - TextWidth.Of(label));
    StyledLine line = new(" " + label + new string(' ', padding));
    StyledLine value = Value(row);
    if (TextWidth.Of(value.PlainText) <= _valueWidth) {
      line.Spans.AddRange(value.Spans);
      return line;
    }

    return line.Add(
        TextWidth.Fit(value.PlainText, _valueWidth),
        value.Spans.Count > 0 ? value.Spans[0].Style : default);
  }

  /// What the details area shows for a row.
  private List<StyledLine> Details(Row row, int width) {
    List<StyledLine> lines = [
      .. TextWidth.Wrap(Detail(row), width).Select(line => new StyledLine(line, Theme.Muted)),
    ];
    string? error = row switch {
      Row.Proxy when !_proxyLoaded => _proxyError,
      Row.Restart when !_restartLoaded => _restartError,
      Row.Daemon when !_daemonLoaded => _daemonError,
      _ => null,
    };
    if (error is not null) {
      lines.AddRange(TextWidth.Wrap(error, width).Select(line => new StyledLine(line, Theme.Bad)));
      return lines;
    }

    if (row == Row.Daemon && _daemonLoaded && _daemon is { } daemon) {
      // The servers of this process are those of the daemon only when the daemon carries out
      // the interface.
      string counts = Context.ManagedServers
          ? $"{SettingsText.DaemonCommands(daemon.Commands)} · "
              + SettingsText.DaemonServers(Context.Servers.Running().Count)
          : SettingsText.DaemonCommands(daemon.Commands);
      lines.AddRange(TextWidth.Wrap(counts, width).Select(line => new StyledLine(line)));
    }

    // A value that the list had to cut is shown whole.
    string value = Value(row).PlainText;
    if (TextWidth.Of(value) > _valueWidth) {
      lines.AddRange(TextWidth.Wrap(value, width).Select(line => new StyledLine(line)));
    }

    return lines;
  }

  /// Carries out what Enter does on a row; false when it does nothing.
  private bool Activate(Row row) {
    switch (row) {
      case Row.Language:
        ChooseLanguage();
        return true;
      case Row.Proxy:
        bool known = _proxyLoaded;
        Context.ShowMenu(
            SettingsText.GithubProxy,
            new MenuItem(SettingsText.SetProxy, () => PromptProxy(_proxy ?? "")),
            new MenuItem(SettingsText.ConnectDirectly, () => SaveProxy(""), !known || _proxy != ""),
            new MenuItem(SettingsText.Reset, () => SaveProxy(null), !known || _proxy is not null));
        return true;
      case Row.Restart:
        ToggleRestart();
        return true;
      case Row.Cache:
        ClearCache();
        return true;
      case Row.CheckUpdates:
        // Once a release is installed, lip that runs is still the old one: a check would
        // offer the same release again.
        string installed = _latest?.ToString() ?? "";
        if (_update == UpdateState.Installed) {
          Context.Message(SettingsText.InstallUpdate, SettingsText.Installed(installed));
        } else if (_update == UpdateState.InstallerStarted) {
          Context.Message(SettingsText.InstallUpdate, SettingsText.InstallerStarted(installed));
        } else {
          CheckForUpdates();
        }

        return true;
      case Row.InstallUpdate:
        if (_latest is { } latest) {
          Context.Confirm(
              SettingsText.InstallUpdate, SettingsText.InstallQuestion(latest.ToString()),
              SettingsText.Install, InstallUpdate);
        }

        return true;
      case Row.Daemon:
        LoadDaemon();
        return true;
      default:
        return false;
    }
  }

  #endregion

  #region The configuration

  /// Reads github_proxy and the restart policy of the selected workspace, as lip config get does.
  private void LoadConfig() {
    LipClient client = Context.Client;
    int read = ++_reads;
    Context.Background(
        () => client.ConfigGet(ConfigService.GithubProxyKey),
        value => {
          if (read == _reads) {
            (_proxy, _proxyLoaded, _proxyError) = (value, true, null);
          }
        },
        ex => {
          if (read == _reads) {
            (_proxyLoaded, _proxyError) = (false, ex.Message);
          }
        });
    Context.Background(
        () => client.ConfigGet(ConfigService.RestartKey, workspace: true),
        value => {
          if (read == _reads) {
            _restart = value == ConfigService.RestartOnFailure;
            (_restartLoaded, _restartError) = (true, null);
          }
        },
        ex => {
          if (read == _reads) {
            (_restartLoaded, _restartError) = (false, ex.Message);
          }
        });
  }

  /// Changes the configuration after the changes made before. Once the last of them is made
  /// the configuration is read again, and its failure, if any, is shown in a dialog with the
  /// title; saved is called when the change was made, whatever came after it.
  private void Change(string title, Func<LipClient, Task> change, Action? saved = null) {
    LipClient client = Context.Client;
    Task before = _changes;
    _reads++;
    Task current = Task.Run(async () => {
      try {
        await before;
      }
      catch (Exception) {
        // That change failed on its own account.
      }

      await change(client);
    });
    _changes = current;
    Run(
        async () => {
          await current;
          return true;
        },
        _ => {
          saved?.Invoke();
          if (_changes == current) {
            LoadConfig();
          }
        },
        ex => {
          // Of changes made one after the other, such as a toggle switched several times, only
          // the last says that it failed.
          if (_changes == current) {
            Context.ShowFailure(title, ex);
            LoadConfig();
          }
        });
  }

  /// Does work in the background, then calls done with its result, or failed, on the thread of
  /// the interface, also when another workspace was selected meanwhile: what the page shows
  /// besides the restart policy is not of a workspace.
  private void Run<T>(Func<Task<T>> work, Action<T> done, Action<Exception> failed) {
    _ = Task.Run(async () => {
      try {
        T result = await work();
        Context.Post(() => done(result));
      }
      catch (Exception ex) {
        Context.Post(() => failed(ex));
      }
    });
  }

  private void ChooseLanguage() {
    Language?[] settings = [null, .. Languages.All.Select(language => (Language?)language)];
    Context.Show(new ChoiceDialog(
        SettingsText.Language,
        [
          .. settings.Select(setting => new ChoiceItem(
              setting is { } language
                  ? Languages.NativeName(language)
                  : SettingsText.FollowSystem)),
        ],
        chosen => Context.SetLanguage(settings[chosen]),
        selected: Array.IndexOf(settings, Context.LanguageSetting)));
  }

  private void ToggleRestart() {
    if (!_restartLoaded && _restartError is null) {
      // Still being read: there is nothing to switch yet.
      return;
    }

    bool on = !(_restartLoaded && _restart);
    (_restart, _restartLoaded, _restartError) = (on, true, null);
    Change(SettingsText.AutoRestart, client => client.ConfigSet(
        ConfigService.RestartKey,
        on ? ConfigService.RestartOnFailure : ConfigService.RestartNever,
        workspace: true));
  }

  #endregion

  #region The GitHub proxy

  /// Asks for a proxy, tests it, and saves it when it reaches GitHub, or when the user keeps it
  /// although it does not (protocol/lip-tui.md, "GitHub reachability").
  private void PromptProxy(string value) {
    Context.Show(new PromptDialog(
        SettingsText.GithubProxy,
        [new PromptField(SettingsText.ProxyUrl, value, SettingsText.ProxyPlaceholder)],
        values => TestProxy(values[0]),
        validate: values =>
            values[0].Length == 0 ? CommonText.Required
                : Uri.TryCreate(values[0], UriKind.Absolute, out _) ? null
                : SettingsText.NotAUrl));
  }

  private void TestProxy(string proxy) {
    if (Context.GithubProbe is not { } probe) {
      SaveProxy(proxy);
      return;
    }

    Context.RunOperation<(bool Cancelled, string? Failure)>(
        SettingsText.TestingProxy,
        async token => {
          try {
            return (false, await probe.Check(proxy, token));
          }
          catch (OperationCanceledException) {
            return (true, null);
          }
          catch (Exception ex) {
            // Such as a URI that no request can be sent to: the proxy does not reach GitHub.
            return (false, ex.Message);
          }
        },
        result => {
          if (result.Cancelled) {
            // Back to what was entered, which is not lost.
            PromptProxy(proxy);
            return;
          }

          if (result.Failure is null) {
            SaveProxy(proxy);
            return;
          }

          Context.Show(new ConfirmDialog(
              SettingsText.ProxyFailed,
              [new StyledLine(result.Failure, Theme.Warning)],
              [SettingsText.EnterAnother, SettingsText.SaveAnyway, CommonText.Cancel],
              choice => {
                switch (choice) {
                  case 0:
                    PromptProxy(proxy);
                    break;
                  case 1:
                    SaveProxy(proxy);
                    break;
                }
              },
              border: Theme.Warning));
        },
        cancellable: true);
  }

  /// Sets github_proxy, as lip config set does, or removes it when value is null, as lip config
  /// delete does.
  private void SaveProxy(string? value) {
    (_proxy, _proxyLoaded, _proxyError) = (value, true, null);
    Change(
        SettingsText.GithubProxy,
        client => value is null
            ? client.ConfigDelete(ConfigService.GithubProxyKey)
            : client.ConfigSet(ConfigService.GithubProxyKey, value),
        () => Context.Toast(SettingsText.Saved, ToastKind.Good));
  }

  #endregion

  #region The cache, the updates and the daemon

  private void ClearCache() {
    LipClient client = Context.Client;
    Context.Confirm(
        SettingsText.ClearCache, SettingsText.ClearCacheQuestion, SettingsText.Clear,
        () => Context.RunOperation(
            SettingsText.ClearCache,
            _ => client.CacheClean(),
            done => Context.ShowResult(
                SettingsText.ClearCache, done, SettingsText.CacheCleared)));
  }

  /// lip self-update --check: when there is a newer release, offers to install it.
  private void CheckForUpdates() {
    Context.RunOperation(
        SettingsText.CheckUpdates,
        _ => Context.SelfUpdate(checkOnly: true),
        result => {
          if (result.Outcome != SelfUpdateOutcome.Available || result.Latest is not { } latest) {
            SetUpdate(UpdateState.UpToDate, null);
            Context.Toast(SettingsText.UpToDate, ToastKind.Good);
            return;
          }

          SetUpdate(UpdateState.Available, latest);
          Context.Confirm(
              SettingsText.InstallUpdate, SettingsText.UpdateAvailable(latest.ToString()),
              SettingsText.Install, InstallUpdate, CommonText.Later);
        });
  }

  /// lip self-update: afterwards the user is told to start lip again, or to quit it so that
  /// the installer can replace it.
  private void InstallUpdate() {
    Context.RunOperation(
        SettingsText.InstallUpdate,
        _ => Context.SelfUpdate(checkOnly: false),
        result => {
          string version = (result.Latest ?? _latest)?.ToString() ?? "";
          switch (result.Outcome) {
            case SelfUpdateOutcome.Replaced:
              SetUpdate(UpdateState.Installed, result.Latest ?? _latest);
              Context.Message(SettingsText.InstallUpdate, SettingsText.Installed(version));
              break;
            case SelfUpdateOutcome.InstallerStarted:
              SetUpdate(UpdateState.InstallerStarted, result.Latest ?? _latest);
              Context.Message(SettingsText.InstallUpdate, SettingsText.InstallerStarted(version));
              break;
            default:
              SetUpdate(UpdateState.UpToDate, null);
              Context.Toast(SettingsText.UpToDate, ToastKind.Good);
              break;
          }
        });
  }

  private void SetUpdate(UpdateState state, SemVersion? latest) {
    (_update, _latest) = (state, latest);
    ShowRows();
  }

  /// Asks the daemon how it is, as lip daemon status does. Only the last answer asked for
  /// counts.
  private void LoadDaemon() {
    int read = ++_daemonReads;
    (_daemonLoaded, _daemonError) = (false, null);
    Run(
        () => Context.DaemonStatus(),
        state => {
          if (read == _daemonReads) {
            (_daemon, _daemonLoaded, _daemonError) = (state, true, null);
          }
        },
        ex => {
          if (read == _daemonReads) {
            (_daemon, _daemonLoaded, _daemonError) = (null, false, ex.Message);
          }
        });
  }

  #endregion
}
