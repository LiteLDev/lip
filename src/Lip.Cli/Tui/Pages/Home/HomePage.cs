using Lip.Cli.Servers;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Widgets;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod;
using Lip.Core.PublicApi;
using Lip.Core.Services;

namespace Lip.Cli.Tui.Pages.Home;

/// The Home page: an overview of the selected workspace, and the steps from an empty directory
/// to a running server, each of which Enter starts. It also makes the reachability check of
/// GitHub when the interface starts, and asks the user about it when it fails
/// (protocol/lip-tui.md, "GitHub reachability").
public sealed class HomePage : Page {
  /// The steps, in the order a new user takes them.
  private enum Step {
    CreateServer,
    InstallMods,
    StartServer,
  }

  private readonly ListView<Step> _steps;

  // The packages of the selected workspace, or null until they are read.
  private IReadOnlyList<InstalledPackage>? _packages;

  // Counts the reads of the packages: only the result of the last one is taken.
  private int _load;

  // Whether the last read of the packages has not ended yet.
  private bool _loading;

  // Whether the user moved the selection since the page was shown or the workspace changed:
  // a read that ends afterwards leaves the selection alone.
  private bool _moved;

  // Why GitHub was not reached, until the interface asks the user about it.
  private string? _githubFailure;

  // Why GitHub was not reached, while the Settings page is asked to take over the question and
  // is not shown yet: the page shown may have kept the user.
  private string? _githubHandover;

  public HomePage(AppContext context) : base(context) {
    _steps = new ListView<Step>(Row) {
      Items = [Step.CreateServer, Step.InstallMods, Step.StartServer],
    };
  }

  public override string Title => HomeText.Title;

  public override IReadOnlyList<Hint> Hints => [CommonText.HintMove, CommonText.HintOpen];

  private InstalledPackage? GamePackage => _packages?.FirstOrDefault(IsGame);

  private InstalledPackage? LoaderPackage => _packages?.FirstOrDefault(IsLoader);

  /// How many packages the user installed besides the game and the loader.
  private int ModCount =>
      _packages?.Count(p => p.Explicit && !IsGame(p) && !IsLoader(p)) ?? 0;

  /// The interface starts on this page, with the steps ready for Enter.
  public override void OnStart() {
    Context.FocusContent();
    CheckGithub();
  }

  public override void OnShown() {
    _moved = false;
    Load();
  }

  public override void OnWorkspaceChanged() {
    _packages = null;
    _moved = false;
    if (Context.CurrentPage == PageId.Home) {
      Load();
    }
  }

  public override void OnTick() => AskAboutGithubWhenIdle();

  public override void Draw(ScreenBuffer screen, Rect area, bool focused) {
    string[] labels =
        [HomeText.Workspace, HomeText.Game, HomeText.Loader, HomeText.Mods, HomeText.Server];
    int labelWidth = labels.Max(TextWidth.Of) + 2;
    StyledLine[] values = [
      new(FitEnd(Context.Workspace, area.Width - labelWidth)),
      Value(GamePackage?.Version, HomeText.NotInstalled),
      Value(
          LoaderPackage is { } loader ? $"{Id(loader).Split('/')[^1]} {loader.Version}" : null,
          HomeText.None),
      _packages is null ? Unknown : new StyledLine($"{ModCount}"),
      ServerState(),
    ];
    int column = area.X + labelWidth;
    int y = area.Y;
    for (int i = 0; i < labels.Length && y < area.Bottom; i++, y++) {
      screen.Write(area.X, y, labels[i], Theme.Muted, area.Right);
      screen.Write(column, y, values[i], area.Right);
    }

    y++;
    if (y < area.Bottom) {
      screen.WriteFit(area.X, y, area.Width, HomeText.NextSteps, Theme.Strong);
    }

    y++;
    int rows = Math.Min(_steps.Items.Count, area.Bottom - y);
    _steps.Draw(screen, new Rect(area.X, y, area.Width, rows), focused);
    y += rows + 1;
    foreach (string line in TextWidth.Wrap(Detail(_steps.Current), area.Width)) {
      if (y >= area.Bottom) {
        break;
      }

      screen.Write(area.X, y++, line, Theme.Muted, area.Right);
    }
  }

  public override bool OnKey(ConsoleKeyInfo key) {
    if (key.Key == ConsoleKey.Enter) {
      Open(_steps.Current);
      return true;
    }

    if (!_steps.OnKey(key)) {
      return false;
    }

    _moved = true;
    return true;
  }

  #region The steps

  /// Reads the packages of the selected workspace in the background, and then selects the
  /// first step that is not done, unless the user moved the selection meanwhile. When they
  /// cannot be read, the overview stays unknown, and no step is ticked or refused.
  private void Load() {
    LipClient client = Context.Client;
    int load = ++_load;
    _loading = true;
    Context.Background(
        () => client.ListPackages(),
        packages => {
          if (load != _load) {
            return;
          }

          _loading = false;
          _packages = packages;
          if (!_moved) {
            Step? next = _steps.Items.Cast<Step?>().FirstOrDefault(step => !Done(step!.Value));
            _steps.Select(step => step == (next ?? Step.StartServer));
          }
        },
        failure => {
          if (load != _load) {
            return;
          }

          _loading = false;
          _packages = null;
          Context.Toast(failure.Message, ToastKind.Bad);
        });
  }

  /// The package id of an installed package: its identity without the variant.
  private static string Id(InstalledPackage package) {
    int variant = package.Identity.IndexOf('#');
    return variant < 0 ? package.Identity : package.Identity[..variant];
  }

  /// Whether the package is the game, whatever its build.
  private static bool IsGame(InstalledPackage package) =>
      PackageNames.AreEqual(Id(package), BedrockServer.Repository);

  private static bool IsLoader(InstalledPackage package) =>
      GameSetup.IsLoaderRepository(Id(package));

  private bool Done(Step step) =>
      step switch {
        Step.CreateServer => GamePackage is not null,
        Step.InstallMods => ModCount > 0,
        _ => Context.ServerRunning,
      };

  /// Goes straight into what the step stands for.
  private void Open(Step step) {
    switch (step) {
      case Step.CreateServer:
        Context.Navigate(PageId.Workspaces, PageRequest.NewServer);
        break;
      case Step.InstallMods:
        Context.Navigate(PageId.GetMods);
        break;
      case Step.StartServer when Context.ServerRunning:
        Context.Navigate(PageId.Server);
        break;
      case Step.StartServer when _loading && _packages is null:
        // Whether there is a game to start is known in a moment.
        break;
      case Step.StartServer when _packages is not null && GamePackage is null:
        // Without a game there is nothing to start: back to the step that installs one.
        Context.Toast(HomeText.NeedServer, ToastKind.Warning);
        _steps.Select(s => s == Step.CreateServer);
        break;
      default:
        Context.Navigate(PageId.Server, PageRequest.StartServer);
        break;
    }
  }

  private StyledLine Row(Step step) =>
      new StyledLine()
          .Add(Done(step) ? "✓ " : "  ", Theme.Good)
          .Add($"{(int)step + 1}  ", Theme.Muted)
          .Add(step switch {
            Step.CreateServer => HomeText.CreateServer,
            Step.InstallMods => HomeText.InstallMods,
            _ => HomeText.StartServer,
          });

  /// One sentence about the selected step.
  private string Detail(Step step) =>
      step switch {
        Step.CreateServer when Done(step) => HomeText.CreateServerDoneDetail,
        Step.CreateServer => HomeText.CreateServerDetail,
        Step.InstallMods => HomeText.InstallModsDetail,
        _ when Done(step) => HomeText.OpenServerDetail,
        _ when _packages is not null && GamePackage is null => HomeText.NeedServer,
        _ => HomeText.StartServerDetail,
      };

  #endregion

  #region The overview

  private static StyledLine Unknown => new("…", Theme.Muted);

  /// A value of the overview: unknown until the packages are read, then the value, or what
  /// stands for none.
  private StyledLine Value(string? value, string none) =>
      _packages is null ? Unknown
          : value is null ? new StyledLine(none, Theme.Muted)
          : new StyledLine(value);

  private StyledLine ServerState() {
    WorkspaceServer? server = Context.Server;
    // A server that another lip runs is known to run, and no more.
    return server is not { Running: true }
        ? Context.ServerRunning
            ? new StyledLine("● " + CommonText.ServerRunning, Theme.Good)
            : new StyledLine(CommonText.ServerStopped, Theme.Muted)
        : server.Restarting ? new StyledLine("● " + CommonText.ServerRestarting, Theme.Warning)
        : server.Stopping ? new StyledLine("● " + CommonText.ServerStopping, Theme.Warning)
        : new StyledLine("● " + CommonText.ServerRunning, Theme.Good);
  }

  /// The text cut to its last width columns, starting with an ellipsis when it was cut: the end
  /// of a path says more than its start.
  private static string FitEnd(string text, int width) {
    if (TextWidth.Of(text) <= width) {
      return text;
    }

    int start = text.Length;
    int used = 1;
    while (start > 0) {
      int at = char.IsLowSurrogate(text[start - 1]) && start > 1 ? start - 2 : start - 1;
      int columns = TextWidth.Of(text[at..start]);
      if (used + columns > width) {
        break;
      }

      used += columns;
      start = at;
    }

    return width < 1 ? "" : "…" + text[start..];
  }

  #endregion

  #region GitHub reachability

  /// Starts the check, once, unless github_proxy is configured; the interface goes on
  /// meanwhile. The check belongs to the start of the interface and not to a workspace, so
  /// another workspace being selected does not drop its result.
  private void CheckGithub() {
    if (Context.GithubProbe is not { } probe) {
      return;
    }

    LipClient client = Context.Client;
    Context.Background(
        async () => await client.GithubProxy() is null ? await probe.Check(null) : null,
        failure => {
          if (failure is not null) {
            _githubFailure = failure;
            AskAboutGithubWhenIdle();
          }
        },
        // A failure of the check is no failure of the interface.
        ex => RunTrace.Write("github", "The GitHub check did not run: " + ex),
        anyWorkspace: true);
  }

  /// Asks the user about GitHub once nothing else is open, so that the question interrupts
  /// nothing.
  private void AskAboutGithubWhenIdle() {
    if (_githubHandover is { } handover) {
      if (Context.CurrentPage == PageId.Settings) {
        _githubHandover = null;
      } else if (Context.Idle) {
        // The page shown kept the user, so the Settings page never took over: ask again.
        _githubHandover = null;
        _githubFailure = handover;
      }
    }

    if (_githubFailure is not { } failure || !Context.Idle) {
      return;
    }

    _githubFailure = null;
    Context.Show(new GithubDialog(failure, answer => Answer(answer, failure)));
  }

  private void Answer(GithubAnswer answer, string failure) {
    switch (answer) {
      case GithubAnswer.Proxy:
        // The Settings page asks for the proxy, tests it and saves it. A page with unsaved
        // changes asks the user first, and may keep them.
        Context.Navigate(PageId.Settings, PageRequest.GithubProxy);
        if (Context.CurrentPage != PageId.Settings) {
          _githubHandover = failure;
        }

        break;
      case GithubAnswer.Direct:
        // As lip config set github_proxy "" does: configured, so lip does not ask again.
        LipClient client = Context.Client;
        bool started = Context.RunOperation(
            CommonText.ConnectDirectly,
            _ => client.ConfigSet(ConfigService.GithubProxyKey, ""),
            _ => Context.Toast(HomeText.GithubDirectSaved, ToastKind.Good));
        if (!started) {
          // Another operation runs: the answer was not carried out, so ask again after it.
          _githubFailure = failure;
        }

        break;
    }
  }

  #endregion
}
