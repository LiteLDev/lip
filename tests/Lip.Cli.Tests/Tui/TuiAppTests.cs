using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Lip.Cli.Tui;
using Lip.Core.Catalog;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Install;
using Lip.Core.Mcmod.Lock;
using Lip.Core.PublicApi;
using Lip.Core.Services;
using Moq;

namespace Lip.Cli.Tests.Tui;

/// The interface (protocol/lip-tui.md), driven by the keys of a fake terminal.
public sealed class TuiAppTests : IDisposable {
  private const string Index = """
      {
        "mods": [
          {
            "id": "github.com/acme/Alpha", "name": "Alpha", "owner": "acme",
            "description": "An economy core", "tags": ["type:mod", "money"],
            "side": ["server"], "platform": ["win-x64"], "gameVersions": ["1.26.60"],
            "deps": {"github.com/LiteLDev/LeviLamina": "26.60.*"}, "stars": 7,
            "updated": "2026-11-01T00:00:00Z", "created": "2025-01-01T00:00:00Z",
            "url": "https://levimc.org/mods/github.com/acme/Alpha/", "requiredBy": 2
          },
          {
            "id": "github.com/acme/Beta", "name": "Beta", "owner": "acme",
            "description": "领地系统", "tags": ["land"],
            "side": ["server", "client"], "platform": ["win-x64"], "stars": 40,
            "updated": "2026-10-01T00:00:00Z", "created": "2026-01-01T00:00:00Z"
          },
          {
            "id": "github.com/acme/Shaders", "name": "Shaders", "owner": "acme",
            "description": "Client only", "tags": ["shader"],
            "side": ["client"], "platform": ["win-x64"], "stars": 99
          }
        ]
      }
      """;

  private const string Detail = """
      {
        "id": "github.com/acme/Alpha", "license": "MIT",
        "latest": {"variant": "", "version": "1.2.0"},
        "versions": [
          {"version": "1.2.0", "prerelease": false, "released": "2026-11-01T00:00:00Z"},
          {"version": "1.1.0", "prerelease": false}
        ],
        "readmes": [{"lang": "en", "text": "# Alpha\nThe readme of Alpha."}]
      }
      """;

  private static readonly string _root = OperatingSystem.IsWindows() ? @"C:\ws" : "/ws";

  private readonly FakeTerminal _terminal = new();
  private readonly MockFileSystem _fs = new(new Dictionary<string, MockFileData>(), _root);
  private readonly List<Task<int>> _runs = [];

  public void Dispose() {
    _terminal.Dispose();
    Task.WaitAll([.. _runs], TimeSpan.FromSeconds(30));
  }

  private TuiApp Start(Func<IUserInteraction, Func<Stream>, LipClient>? createClient = null) {
    TuiApp app = new(
        _terminal,
        createClient ?? ((interaction, hookOutput) =>
            LipClient.Create(_fs, interaction, hookOutput)),
        new ModCatalogClient((url, _) => Task.FromResult(
            url == ModCatalogClient.BaseUrl + "index.json" ? Index : Detail)),
        "1.2.3",
        _root,
        (exception, _, cancelled) =>
            new Failure(exception.Message, ["a hint"], "report.log", null, "", cancelled));
    _runs.Add(Task.Run(app.Run));
    return app;
  }

  private static async Task WaitFor(TuiApp app, Func<string, bool> shown) {
    for (int i = 0; i < 600; i++) {
      if (shown(app.ScreenText)) {
        return;
      }

      await Task.Delay(25, TestContext.Current.CancellationToken);
    }

    Assert.Fail("The screen never showed what was waited for:\n" + app.ScreenText);
  }

  private static Task WaitFor(TuiApp app, string text) =>
      WaitFor(app, screen => screen.Contains(text, StringComparison.Ordinal));

  [Fact]
  public async Task Start_EmptyWorkspace_ShowsThePackagesTab() {
    TuiApp app = Start();

    await WaitFor(app, "No package is installed in this workspace.");

    Assert.Contains("lip 1.2.3", app.ScreenText);
    Assert.Contains("1 Packages  2 Browse  3 Server  4 Settings  5 Develop", app.ScreenText);
    Assert.Contains(_root, app.ScreenText);
  }

  [Fact]
  public async Task Quit_EndsWithCodeZero() {
    Start();

    _terminal.Type("q");

    Assert.Equal(0, await _runs[0].WaitAsync(TestContext.Current.CancellationToken));
  }

  [Fact]
  public async Task Browse_ListsTheModsForAServerWithTheirDetails() {
    TuiApp app = Start();

    _terminal.Type("2");

    await WaitFor(app, "Mods — 2 ·");
    Assert.Contains("Alpha · acme", app.ScreenText);
    Assert.Contains("Beta · acme", app.ScreenText);
    Assert.DoesNotContain("Shaders", app.ScreenText);
    Assert.Contains("github.com/acme/Alpha", app.ScreenText);
    Assert.Contains("LeviLamina 26.60.*", app.ScreenText);
    // The file of the selected mod is read once the selection rests.
    await WaitFor(app, "License  MIT");
    Assert.Contains("Latest   1.2.0", app.ScreenText);
  }

  [Fact]
  public async Task Browse_SearchAndSort_SelectTheMods() {
    TuiApp app = Start();
    _terminal.Type("2");
    await WaitFor(app, "Mods — 2 ·");

    _terminal.Type("/领地");
    await WaitFor(app, "Mods — 1 ·");
    Assert.Contains("Beta · acme", app.ScreenText);

    _terminal.Press(ConsoleKey.Escape);
    _terminal.Type("s");
    await WaitFor(app, "Mods — 2 · sort: Stars");
    string[] rows = app.ScreenText.Split('\n');
    Assert.True(
        Array.FindIndex(rows, row => row.Contains("Beta · acme"))
            < Array.FindIndex(rows, row => row.Contains("Alpha · acme")));
  }

  [Fact]
  public async Task Browse_Filters_IncludeAGameVersionAndExcludeATag() {
    TuiApp app = Start();
    _terminal.Type("2");
    await WaitFor(app, "Mods — 2 ·");
    // Only what mods for a server have is offered: not the tag of the mod for clients.
    Assert.Contains("Game version", app.ScreenText);
    Assert.Contains("Server management", app.ScreenText);
    Assert.Contains("Economy", app.ScreenText);
    Assert.DoesNotContain("Visual & HUD", app.ScreenText);

    // The first row is the game version of Alpha.
    _terminal.Type("f ");
    await WaitFor(app, "Mods — 1 · sort: Relevance · 1 filters");
    Assert.Contains("[+] 1.26.60", app.ScreenText);
    Assert.Contains("Alpha · acme", app.ScreenText);

    _terminal.Type("c");
    await WaitFor(app, "Mods — 2 · sort: Relevance");

    // Down to "Economy", the tag of Alpha, and exclude it.
    _terminal.Press(ConsoleKey.DownArrow);
    _terminal.Press(ConsoleKey.DownArrow);
    _terminal.Type("x");
    await WaitFor(app, "Mods — 1 · sort: Relevance · 1 filters");
    Assert.Contains("[-] Economy", app.ScreenText);
    Assert.Contains("Beta · acme", app.ScreenText);
    Assert.DoesNotContain("Alpha · acme", app.ScreenText);

    // Including it instead takes the exclusion back.
    _terminal.Type(" ");
    await WaitFor(app, "[+] Economy");
    Assert.Contains("Alpha · acme", app.ScreenText);
  }

  [Fact]
  public async Task Browse_Enter_AsksForTheVersionAndNeverForTheWholePackageArgument() {
    TuiApp app = Start();
    _terminal.Type("2");
    await WaitFor(app, "Mods — 2 ·");

    _terminal.Press(ConsoleKey.Enter);
    await WaitFor(app, "Install Alpha — choose a version");
    Assert.Contains("Latest                  1.2.0", app.ScreenText);
    Assert.Contains("1.1.0", app.ScreenText);
    Assert.Contains("Custom...", app.ScreenText);
    Assert.DoesNotContain("Install packages", app.ScreenText);

    // The last entry asks for what follows the @ only.
    _terminal.Press(ConsoleKey.End);
    _terminal.Press(ConsoleKey.Enter);
    await WaitFor(app, "Install Alpha — custom version");
    Assert.Contains("rev:<commit>", app.ScreenText);
    Assert.DoesNotContain("--platform", app.ScreenText);
  }

  [Fact]
  public async Task Browse_Readme_IsShown() {
    TuiApp app = Start();
    _terminal.Type("2");
    await WaitFor(app, "Mods — 2 ·");

    _terminal.Type("r");

    await WaitFor(app, "The readme of Alpha.");
  }

  [Fact]
  public async Task Install_ThatFails_ShowsTheFailureWithItsHintsAndReport() {
    TuiApp app = Start();
    await WaitFor(app, "No package is installed");

    _terminal.Type("i./missing.zip");
    _terminal.Press(ConsoleKey.Enter);

    await WaitFor(app, "failed");
    Assert.Contains("error:", app.ScreenText);
    Assert.Contains("hint: a hint", app.ScreenText);
    Assert.Contains("Full report: report.log", app.ScreenText);
    Assert.False(_fs.Directory.Exists(_fs.Path.Join(_root, ".lip")));
  }

  [Fact]
  public async Task Settings_Edit_SetsTheConfigurationValue() {
    TuiApp app = Start();
    _terminal.Type("4");
    await WaitFor(app, "github_proxy");
    Assert.Contains("(not set)", app.ScreenText);

    _terminal.Press(ConsoleKey.Enter);
    await WaitFor(app, "Set github_proxy");
    _terminal.Type("https://proxy.example");
    _terminal.Press(ConsoleKey.Enter);

    await WaitFor(app, screen =>
        screen.Contains("https://proxy.example", StringComparison.Ordinal)
            && !screen.Contains("Set github_proxy", StringComparison.Ordinal));
    Assert.Contains(
        "https://proxy.example",
        _fs.File.ReadAllText(_fs.Path.Join(LipDirectories.Config, "config.toml")));
  }

  [Fact]
  public async Task Develop_CreateLipToml_AsksForTheIdInTheInterface() {
    TuiApp app = Start();
    _terminal.Type("5");
    await WaitFor(app, "lip pkg init");

    _terminal.Press(ConsoleKey.Enter);
    await WaitFor(app, "lip asks");
    _terminal.Type("github.com/owner/repo");
    _terminal.Press(ConsoleKey.Enter);

    await WaitFor(app, "Wrote lip.toml.");
    Assert.Contains(
        "github.com/owner/repo", _fs.File.ReadAllText(_fs.Path.Join(_root, "lip.toml")));
  }

  [Fact]
  public async Task Develop_QuestionNotAnswered_CancelsTheCommand() {
    TuiApp app = Start();
    _terminal.Type("5");
    await WaitFor(app, "lip pkg init");

    _terminal.Press(ConsoleKey.Enter);
    await WaitFor(app, "lip asks");
    _terminal.Press(ConsoleKey.Escape);

    await WaitFor(app, "Cancelled.");
    Assert.False(_fs.File.Exists(_fs.Path.Join(_root, "lip.toml")));
  }

  [Fact]
  public async Task Server_WithoutAServer_ShowsWhyItDidNotStart() {
    TuiApp app = Start();
    _terminal.Type("3");
    await WaitFor(app, "Press Enter to start the server");

    _terminal.Press(ConsoleKey.Enter);

    await WaitFor(app, "Run the server failed");
    Assert.Contains("There is no server to run", app.ScreenText);
  }

  [Fact]
  public async Task Server_Runs_ShowsItsOutputSendsItsInputAndStopsItBeforeQuitting() {
    Assert.SkipUnless(OperatingSystem.IsWindows(), "The server of the test is cmd.exe.");
    string root = Directory.CreateTempSubdirectory("lip-tui-test-").FullName;
    try {
      // cmd.exe runs the lines it reads, and exits when it reads "exit".
      File.Copy(
          Path.Join(Environment.SystemDirectory, "cmd.exe"),
          Path.Join(root, WorkspaceProgram.ServerFileName));
      FileSystem fs = new();
      TuiApp app = Start((interaction, hookOutput) => new LipClient(
          fs, interaction, new CacheService(fs, interaction), new ConfigService(fs, interaction),
          new McmodService(
              fs, interaction, new LockStore(fs), Mock.Of<IArchiveProvider>(),
              new ArchiveReader(),
              new McmodInstaller(fs, new CommandRunner(hookOutput), interaction),
              workspaceRoot: () => root)));
      _terminal.Type("3");
      await WaitFor(app, "Press Enter to start the server");

      _terminal.Press(ConsoleKey.Enter);
      await WaitFor(app, "running · up");
      _terminal.Type("echo hello-from-server");
      _terminal.Press(ConsoleKey.Enter);
      await WaitFor(app, screen => screen.Split('\n').Any(line =>
          line.Contains("hello-from-server", StringComparison.Ordinal)
              && !line.Contains("echo", StringComparison.Ordinal)));

      // Quitting asks the server to stop: it reads the line "stop", which cmd.exe echoes and
      // does not know, and then the line that makes it exit.
      _terminal.Press(ConsoleKey.Q, ConsoleModifiers.Control);
      await WaitFor(app, "Quitting stops it first.");
      _terminal.Press(ConsoleKey.Enter);
      await WaitFor(app, screen => screen.Split('\n').Any(line =>
          line.Trim('│', ' ') == "stop"));
      _terminal.Type("exit 7");
      _terminal.Press(ConsoleKey.Enter);

      Assert.Equal(0, await _runs[0].WaitAsync(TestContext.Current.CancellationToken));
    }
    finally {
      try {
        Directory.Delete(root, recursive: true);
      }
      catch (UnauthorizedAccessException) {
        // The server still runs: the test failed before it stopped it.
      }
    }
  }
}
