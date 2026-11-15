using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Lip.Cli.Servers;
using Lip.Cli.Tui;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Workspaces;
using Lip.Core.Catalog;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Install;
using Lip.Core.Mcmod.Lock;
using Lip.Core.PublicApi;
using Lip.Core.Services;
using Moq;
using Semver;
using TuiContext = Lip.Cli.Tui.AppContext;

namespace Lip.Cli.Tests.Tui;

/// What a test replaces of the interface that TuiHarness starts. Everything has a default
/// that touches neither the network nor the disk.
public sealed class TuiHarnessOptions {
  /// The client language (protocol/lip-tui.md, "Language").
  public Language? Language { get; init; } = Lip.Cli.Tui.I18n.Language.En;

  /// The size of the terminal.
  public int Width { get; init; } = 120;

  public int Height { get; init; } = 36;

  /// The file system of the workspaces and of the configuration: an empty one when not given.
  /// Fill it before the interface starts for what the interface reads when it starts.
  public MockFileSystem? FileSystem { get; init; }

  /// The working directory of lip: TuiHarness.DefaultRoot when not given.
  public string? WorkingDirectory { get; init; }

  /// Makes the client of a workspace: LipClient.Create on the file system when not given. Wrap
  /// it to fake or record the commands.
  public Func<IUserInteraction, Func<Stream>, string, LipClient>? CreateClient { get; init; }

  /// The text of the file of the catalogue at a URL: an empty catalogue when not given.
  public Func<string, string>? Catalog { get; init; }

  /// The reachability check of GitHub: not made when not given. new GithubProbe(send) fakes
  /// the request.
  public GithubProbe? GithubProbe { get; init; }

  /// The server setup, made for the harness so that it can reach the interaction of a client
  /// (TuiHarness.InteractionOf): that of LipClient when not given. See FakeServerSetup.
  public Func<TuiHarness, IServerSetup>? ServerSetup { get; init; }

  /// Whether the servers are managed servers, as when the daemon carries out the interface.
  public bool ManagedServers { get; init; }

  /// lip self-update: fails when not given.
  public Func<LipClient, bool, Task<SelfUpdateResult>>? SelfUpdate { get; init; }

  /// What lip daemon status finds: no daemon when not given.
  public Func<CancellationToken, Task<DaemonState?>>? DaemonStatus { get; init; }

  /// The pages of the sidebar: those of lip when not given.
  public IReadOnlyList<PageEntry>? Pages { get; init; }

  /// The servers of the process: the harness's own when not given.
  public ServerSessions? Servers { get; init; }

  /// Describes a failure: when not given, its message, the hint "a hint" and the report
  /// .lip/logs/report.log, writing nothing.
  public Func<Exception, string[], bool, string, Failure>? DescribeFailure { get; init; }
}

/// Runs the interface on a fake terminal, in this process, for a test: the test presses keys
/// and reads the screen as text. Dispose ends the interface.
public sealed class TuiHarness : IDisposable {
  /// The working directory of lip unless a test gives another.
  public static readonly string DefaultRoot = OperatingSystem.IsWindows() ? @"C:\ws" : "/ws";

  /// A lock file with a game, for workspaces that have a server.
  public const string StateWithGame = """
      { "format": 4, "side": "server", "platform": "win-x64", "packages": [ {
        "manifest": { "format": 4, "id": "github.com/LiteLDev/bds", "version": "1.21.0",
          "variants": { "": [ { "side": "server", "platform": "win-x64" } ] } },
        "variant": "", "explicit": true, "ref": "github.com/LiteLDev/bds@1.21.0",
        "files": [] } ] }
      """;

  private readonly Dictionary<LipClient, IUserInteraction> _interactions = [];
  private readonly Dictionary<LipClient, string> _clientWorkspaces = [];
  // The workspaces whose server another lip runs (SetServerRunning).
  private readonly HashSet<string> _runningElsewhere = new(WorkspaceList.PathComparer);
  private readonly TuiHarnessOptions _options;
  private static readonly string NewLine = Environment.NewLine;

  private TuiHarness(TuiHarnessOptions options) {
    _options = options;
    Fs = options.FileSystem ?? new MockFileSystem(new Dictionary<string, MockFileData>(), DefaultRoot);
    Root = options.WorkingDirectory ?? DefaultRoot;
    Fs.AddDirectory(Root);
    Terminal = new FakeTerminal(options.Width, options.Height);
    Servers = options.Servers ?? new ServerSessions();
    WorkspaceListPath = Path.Join(
        OperatingSystem.IsWindows() ? @"C:\state" : "/state", WorkspaceList.FileName);
    Workspaces = new WorkspaceList(Fs, WorkspaceListPath);
    Func<IUserInteraction, Func<Stream>, string, LipClient> create = options.CreateClient
        ?? ((interaction, hookOutput, workspace) =>
            LipClient.Create(Fs, interaction, hookOutput, workspace));
    App = new App(new AppOptions {
      Terminal = Terminal,
      CreateClient = (interaction, hookOutput, workspace) => {
        LipClient client = create(interaction, hookOutput, workspace);
        lock (_interactions) {
          _interactions[client] = interaction;
          _clientWorkspaces[client] = workspace;
        }

        return client;
      },
      Catalog = new ModCatalogClient((url, _) => Task.FromResult(
          options.Catalog?.Invoke(url) ?? "{\"mods\": []}")),
      Version = "1.2.3",
      WorkingDirectory = Root,
      DescribeFailure = options.DescribeFailure ?? ((exception, _, cancelled, _) => new Failure(
          exception.Message, ["a hint"], Path.Join(".lip", "logs", "report.log"), null, "",
          cancelled)),
      Workspaces = Workspaces,
      Servers = Servers,
      ServerLockHeld = workspace => {
        lock (_runningElsewhere) {
          return _runningElsewhere.Contains(workspace);
        }
      },
      GithubProbe = options.GithubProbe,
      ServerSetup = options.ServerSetup?.Invoke(this),
      ManagedServers = options.ManagedServers,
      ClientLanguage = options.Language,
      SelfUpdate = options.SelfUpdate ?? ((_, _) => Task.FromException<SelfUpdateResult>(
          new InvalidOperationException("The test gave no self-update."))),
      DaemonStatus = options.DaemonStatus,
      OpenUrl = url => {
        lock (OpenedUrls) {
          OpenedUrls.Add(url);
        }
      },
      Pages = options.Pages,
    });
    Run = Task.Run(App.Run);
  }

  /// Starts the interface. Not async, so that the test reads text in the language of the
  /// interface afterwards (L.T follows the interface that the caller started).
  public static TuiHarness Start(TuiHarnessOptions? options = null) {
    TuiHarness harness = new(options ?? new TuiHarnessOptions());
    L.Bind(harness.App.Context.LanguageScope);
    return harness;
  }

  public MockFileSystem Fs { get; }

  /// The working directory of lip, which is the selected workspace when the interface starts.
  public string Root { get; }

  public FakeTerminal Terminal { get; }

  public App App { get; }

  public TuiContext Context => App.Context;

  public ServerSessions Servers { get; }

  /// The workspace list of the interface, as another process would read it.
  public WorkspaceList Workspaces { get; }

  public string WorkspaceListPath { get; }

  /// The URLs that the interface opened in the browser.
  public List<string> OpenedUrls { get; } = [];

  /// Completes with the exit code of lip once the interface has ended.
  public Task<int> Run { get; }

  /// What the screen shows, as plain text, one row per line.
  public string Screen => App.ScreenText;

  /// The workspaces that clients were made for, in order.
  public IReadOnlyList<string> ClientWorkspaces {
    get {
      lock (_interactions) {
        return [.. _clientWorkspaces.Values];
      }
    }
  }

  /// The user interaction of a client that the interface made: what a fake command of the
  /// client tells the user goes through it.
  public IUserInteraction InteractionOf(LipClient client) {
    lock (_interactions) {
      return _interactions[client];
    }
  }

  /// The workspace of a client that the interface made.
  public string WorkspaceOf(LipClient client) {
    lock (_interactions) {
      return _clientWorkspaces[client];
    }
  }

  /// Makes the server of a workspace, the selected one when not given, run as one that
  /// another lip runs, which holds its server lock; or not run any more. Returns once the
  /// interface has seen it, which looks about once a second: Context.ServerRunning and
  /// Context.IsServerRunning say so from then on.
  public async Task SetServerRunning(bool running, string? workspace = null) {
    string path = workspace ?? await Invoke(() => Context.Workspace);
    SetServerLockHeld(running, path);
    await WaitUntil(
        () => Context.IsServerRunning(path) == running,
        running ? "a running server" : "a stopped server");
  }

  /// As SetServerRunning, without waiting for the interface to see it: for a server that
  /// starts or stops between two looks of the interface.
  public void SetServerLockHeld(bool held, string workspace) {
    lock (_runningElsewhere) {
      if (held) {
        _runningElsewhere.Add(workspace);
      } else {
        _runningElsewhere.Remove(workspace);
      }
    }
  }

  /// The user configuration of the file system, as lip config reads it.
  public ConfigService Config => new(Fs, Mock.Of<IUserInteraction>());

  /// What get returns in the language of the interface: Text(() => ModsText.Title).
  public string Text(Func<string> get) => L.In(Context.Language, get);

  /// Presses a key, times times.
  public void Press(ConsoleKey key, int times = 1, ConsoleModifiers modifiers = 0) {
    for (int i = 0; i < times; i++) {
      Terminal.Press(key, modifiers);
    }
  }

  /// Presses the keys of the characters of a text.
  public void Type(string text) => Terminal.Type(text);

  /// Presses Ctrl+C.
  public void Interrupt() =>
      Terminal.Press(ConsoleKey.C, ConsoleModifiers.Control, '\x03');

  /// The terminal has another size.
  public void Resize(int width, int height) {
    Terminal.Width = width;
    Terminal.Height = height;
  }

  /// Waits until the interface has handled every key pressed so far and drawn the screen. What
  /// runs in the background, such as an operation, may still go on: wait for its text.
  public async Task Settle() {
    await Poll(() => App.KeysHandled >= Terminal.Pressed, () => "The keys were never handled.");
    long frames = await Invoke(() => App.Frames);
    await Poll(() => App.Frames > frames, () => "The screen was never drawn.");
  }

  /// Waits until the screen shows the text.
  public Task WaitFor(string text) =>
      WaitFor(screen => screen.Contains(text, StringComparison.Ordinal), $"'{text}'");

  /// Waits until the screen does not show the text.
  public Task WaitUntilGone(string text) =>
      WaitFor(
          screen => !screen.Contains(text, StringComparison.Ordinal), $"no more '{text}'");

  /// Waits until shown holds for the text of the screen.
  public Task WaitFor(Func<string, bool> shown, string what = "what was waited for") =>
      Poll(() => shown(Screen), () => $"The screen never showed {what}:{NewLine}{Screen}");

  /// Waits until holds, which is asked on the thread of the interface, as what reads the
  /// context of the interface must be: WaitUntil(() => !Context.ContentFocused, "the sidebar").
  public async Task WaitUntil(Func<bool> holds, string what = "what was waited for") {
    for (int i = 0; i < 600; i++) {
      if (await Invoke(holds)) {
        return;
      }

      await Task.Delay(25, TestContext.Current.CancellationToken);
    }

    Assert.Fail($"The interface never reached {what}:{NewLine}{Screen}");
  }

  /// Runs get on the thread of the interface and returns what it returns.
  public Task<T> Invoke<T>(Func<T> get) {
    TaskCompletionSource<T> result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    App.Post(() => {
      try {
        result.SetResult(get());
      }
      catch (Exception ex) {
        result.SetException(ex);
      }
    });
    return result.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
  }

  /// Runs the action on the thread of the interface.
  public Task Invoke(Action action) =>
      Invoke(() => {
        action();
        return true;
      });

  /// Goes to a page with the keys of the user: Esc back to the sidebar when the content area
  /// has the focus, once for each level that the page is deep, ↑ or ↓ to the entry of the
  /// page, then Enter; waits until its content area has the focus.
  public async Task GoTo(PageId page) {
    await Settle();
    for (int level = 0; await Invoke(() => Context.ContentFocused); level++) {
      if (level == 8) {
        Assert.Fail($"Esc never left the page:{NewLine}{Screen}");
      }

      Press(ConsoleKey.Escape);
      await Settle();
    }

    IReadOnlyList<PageEntry> entries = _options.Pages ?? PageRegistry.All;
    int target = entries.ToList().FindIndex(e => e.Id == page);
    int current = entries.ToList().FindIndex(e => e.Id == Context.CurrentPage);
    Press(target > current ? ConsoleKey.DownArrow : ConsoleKey.UpArrow, Math.Abs(target - current));
    Press(ConsoleKey.Enter);
    await Poll(
        () => Context.CurrentPage == page && Context.ContentFocused,
        () => $"The page {page} was never shown:{NewLine}{Screen}");
    await Settle();
  }

  /// Quits the interface as the user does, with Ctrl+C and Enter in the dialog that asks, and
  /// returns the exit code of lip. For when nothing else is open.
  public async Task<int> Quit() {
    Interrupt();
    await WaitFor(Text(() => CommonText.QuitQuestion));
    Press(ConsoleKey.Enter);
    return await Run.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
  }

  /// Writes the frame that the screen shows, as text below a line that names it, its language
  /// and its size.
  public void Dump(TextWriter writer, string name = "frame") {
    writer.WriteLine(
        $"=== {name} · {Languages.Code(Context.Language)} · {Terminal.Width}×{Terminal.Height} ===");
    writer.WriteLine(Screen);
    writer.WriteLine();
  }

  /// Writes the frame that the screen shows to a file.
  public void Dump(string path, string name = "frame") {
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
    using StreamWriter writer = new(path, append: false, new System.Text.UTF8Encoding(false));
    Dump(writer, name);
  }

  /// When the environment variable LIP_TUI_DUMP names a directory, writes the frame that the
  /// screen shows to the file name-language-WIDTHxHEIGHT.txt in it, for visual review;
  /// otherwise does nothing. Call it at the frames of a test that are worth looking at.
  public void Snapshot(string name) {
    if (Environment.GetEnvironmentVariable("LIP_TUI_DUMP") is { Length: > 0 } directory) {
      Dump(
          Path.Join(
              directory,
              $"{name}-{Languages.Code(Context.Language)}-{Terminal.Width}x{Terminal.Height}.txt"),
          name);
    }
  }

  public void Dispose() {
    Terminal.Dispose();
    try {
      Run.Wait(TimeSpan.FromSeconds(30));
    }
    catch (AggregateException) {
      // The test that awaited the interface saw why it failed.
    }

    L.Bind(null);
  }

  private async Task Poll(Func<bool> condition, Func<string> failure) {
    for (int i = 0; i < 600; i++) {
      if (condition()) {
        return;
      }

      // An interface that failed shows nothing more: the test fails with its failure.
      if (Run.IsFaulted) {
        await Run;
      }

      await Task.Delay(25, TestContext.Current.CancellationToken);
    }

    Assert.Fail(failure());
  }
}

/// The clients and the workspaces of the tests that run a real program as the server.
public static class TestClients {
  /// A workspace on the disk whose server, a game of the build windows without a loader, is
  /// cmd.exe, which runs the lines it reads and exits when it reads "exit". Windows only.
  public static string CmdWorkspace() {
    string root = Directory.CreateTempSubdirectory("lip-tui-test-").FullName;
    File.Copy(
        Path.Join(Environment.SystemDirectory, "cmd.exe"),
        Path.Join(root, WorkspaceProgram.WindowsServerFileName));
    Directory.CreateDirectory(Path.Join(root, ".lip"));
    File.WriteAllText(Path.Join(root, ".lip", "state.json"), TuiHarness.StateWithGame);
    return root;
  }

  /// The client of the workspace at root, which prepares no system. Its user configuration is
  /// a directory of the test, not that of the user who runs the tests.
  public static LipClient CmdClient(
      FileSystem fs, IUserInteraction interaction, Func<Stream> hookOutput, string root) =>
      new(
          fs, interaction, new CacheService(fs, interaction),
          new ConfigService(
              fs, interaction, () => root, Path.Join(root, ".lip", "test-user-config")),
          new McmodService(
              fs, interaction, new LockStore(fs), Mock.Of<IArchiveProvider>(),
              new ArchiveReader(),
              new McmodInstaller(fs, new CommandRunner(hookOutput), interaction),
              workspaceRoot: () => root));
}

/// The server setup of the tests: two games, the newest of them for Linux too, and two
/// LeviLamina versions for it; Setup records its calls, and its dry run says that it would ask
/// for the license, as lip setup does, and that the setup would change the workspace.
public sealed class FakeServerSetup(TuiHarness harness) : IServerSetup {
  /// The calls of Setup, as "game build loader loaderVersion dryRun=… acceptEula=…". Lock it
  /// to read it.
  public List<string> Calls { get; } = [];

  /// The sessions that the versions were read in and that the setups were carried out in, in
  /// order; null where none was given. Lock Calls to read it.
  public List<OperationSession?> Sessions { get; } = [];

  /// How many times the versions of the loader were asked for.
  public int LoaderLists { get; private set; }

  /// What Setup returns: what the setup changed, or would change.
  public OperationResult Result { get; set; } = OperationResult.Changed;

  public Task<IReadOnlyList<GameVersion>> GameVersions(
      LipClient client, OperationSession? session = null) {
    lock (Calls) {
      Sessions.Add(session);
    }

    return Task.FromResult<IReadOnlyList<GameVersion>>([
      new GameVersion(SemVersion.Parse("1.21.50"), Windows: true, Linux: true),
      new GameVersion(SemVersion.Parse("1.21.40"), Windows: true, Linux: false),
    ]);
  }

  public Task<IReadOnlyList<SemVersion>> LoaderVersions(
      LipClient client,
      SemVersion game,
      CancellationToken cancellationToken,
      OperationSession? session = null) {
    lock (Calls) {
      Sessions.Add(session);
    }

    LoaderLists++;
    return Task.FromResult<IReadOnlyList<SemVersion>>(
        [SemVersion.Parse("1.1.0"), SemVersion.Parse("1.0.0")]);
  }

  public async Task<OperationResult> Setup(
      LipClient client, SetupRequest request, OperationRun run) {
    lock (Calls) {
      Calls.Add($"{request.Game} {request.Build} {request.Loader} {request.LoaderVersion}"
          + $" dryRun={run.DryRun} acceptEula={run.AcceptEula}");
      Sessions.Add(run.Session);
    }

    if (run.DryRun) {
      await harness.InteractionOf(client).PrintInfo(GameSetup.EulaDryRunMessage + ".");
    }

    return Result;
  }
}
