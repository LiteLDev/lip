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
            "deps": {"github.com/LiteLDev/LeviLamina": "26.60.*"}, "stars": 7, "license": "MIT",
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
          {
            "version": "1.2.0", "prerelease": false, "released": "2026-11-01T00:00:00Z",
            "variants": {
              "": [{"side": "server"}], "lua": [{"side": "server"}], "client": [{"side": "client"}]
            }
          },
          {"version": "1.1.0", "prerelease": false, "variants": {"": [{"side": "server"}]}}
        ],
        "readmes": [{"lang": "en", "text": "# Alpha\nThe readme of Alpha."}]
      }
      """;

  private static readonly string _root = OperatingSystem.IsWindows() ? @"C:\ws" : "/ws";

  private readonly FakeTerminal _terminal = new();
  private readonly MockFileSystem _fs = new(new Dictionary<string, MockFileData>(), _root);
  private readonly List<Task<int>> _runs = [];
  private readonly ServerSessions _servers = new();
  private readonly string _workspaceList = Path.Join(
      OperatingSystem.IsWindows() ? @"C:\state" : "/state", WorkspaceList.FileName);

  public void Dispose() {
    _terminal.Dispose();
    Task.WaitAll([.. _runs], TimeSpan.FromSeconds(30));
  }

  private TuiApp Start(
      Func<IUserInteraction, Func<Stream>, string, LipClient>? createClient = null,
      FakeTerminal? terminal = null,
      string? workingDirectory = null) {
    TuiApp app = new(
        terminal ?? _terminal,
        createClient ?? ((interaction, hookOutput, workspace) =>
            LipClient.Create(_fs, interaction, hookOutput, workspace)),
        new ModCatalogClient((url, _) => Task.FromResult(
            url == ModCatalogClient.BaseUrl + "index.json" ? Index : Detail)),
        "1.2.3",
        workingDirectory ?? _root,
        (exception, _, cancelled) =>
            new Failure(exception.Message, ["a hint"], "report.log", null, "", cancelled),
        new WorkspaceList(_fs, _workspaceList),
        _servers);
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
  public async Task Start_AddsTheWorkingDirectoryToTheWorkspaceListOnce() {
    // Arrange
    string other = Path.Join(_root, "other");
    _fs.AddDirectory(other);
    new WorkspaceList(_fs, _workspaceList).Add(other);

    // Act
    TuiApp app = Start();
    await WaitFor(app, "No package is installed in this workspace.");
    _terminal.Type("q");
    await _runs[0].WaitAsync(TestContext.Current.CancellationToken);
    using FakeTerminal again = new();
    TuiApp second = Start(terminal: again);
    await WaitFor(second, "No package is installed in this workspace.");

    // Assert
    Assert.Equal([other, _root], new WorkspaceList(_fs, _workspaceList).Read());
    Assert.Contains("w  " + _root, second.ScreenText);
  }

  [Fact]
  public async Task Workspaces_AddedAndSelected_AreWhatTheTabsOperateOn() {
    // Arrange
    string other = Path.Join(_root, "other");
    _fs.AddFile(Path.Join(other, ".lip", "state.json"), new MockFileData("""
        { "format": 4, "side": "server", "platform": "win-x64", "packages": [ {
          "manifest": { "format": 4, "id": "github.com/LiteLDev/bds", "version": "1.21.0",
            "variants": { "": [ { "side": "server", "platform": "win-x64" } ] } },
          "variant": "", "explicit": true, "ref": "github.com/LiteLDev/bds@1.21.0",
          "files": [] } ] }
        """));
    List<string> clients = [];
    TuiApp app = Start((interaction, hookOutput, workspace) => {
      lock (clients) {
        clients.Add(workspace);
      }

      return LipClient.Create(_fs, interaction, hookOutput, workspace);
    });
    await WaitFor(app, "No package is installed in this workspace.");

    // Act: a relative path is of the selected workspace.
    _terminal.Type("w");
    await WaitFor(app, "Workspaces");
    _terminal.Type("a");
    await WaitFor(app, "Add a workspace");
    _terminal.Type("other");
    _terminal.Press(ConsoleKey.Enter);

    // Assert
    await WaitFor(app, "github.com/LiteLDev/bds");
    Assert.Contains("w  " + other, app.ScreenText);
    Assert.Equal([_root, other], new WorkspaceList(_fs, _workspaceList).Read());
    lock (clients) {
      Assert.Equal([_root, other], clients);
    }

    // Act: back to the first one, from the list.
    _terminal.Type("w");
    await WaitFor(app, "● " + other);
    _terminal.Press(ConsoleKey.UpArrow);
    _terminal.Press(ConsoleKey.Enter);

    // Assert
    await WaitFor(app, "No package is installed in this workspace.");
    Assert.Contains("w  " + _root, app.ScreenText);
  }

  [Fact]
  public async Task Workspaces_Remove_TakesAWorkspaceOffTheListOnly() {
    // Arrange
    string other = Path.Join(_root, "other");
    _fs.AddFile(Path.Join(other, "lip.toml"), new MockFileData("format = 4\n"));
    new WorkspaceList(_fs, _workspaceList).Add(other);
    TuiApp app = Start();
    await WaitFor(app, "No package is installed in this workspace.");

    // Act: the selected workspace stays; the other one goes.
    _terminal.Type("w");
    await WaitFor(app, "● " + _root);
    _terminal.Type("d");
    await WaitFor(app, "cannot be removed");
    _terminal.Press(ConsoleKey.UpArrow);
    _terminal.Type("d");

    // Assert
    await WaitFor(app, "no file in it changed");
    Assert.Equal([_root], new WorkspaceList(_fs, _workspaceList).Read());
    Assert.Equal("format = 4\n", _fs.File.ReadAllText(Path.Join(other, "lip.toml")));
  }

  [Fact]
  public async Task Workspaces_AddingWhatIsNotADirectory_IsRefused() {
    TuiApp app = Start();
    await WaitFor(app, "No package is installed in this workspace.");

    _terminal.Type("w");
    await WaitFor(app, "Workspaces");
    _terminal.Type("a");
    await WaitFor(app, "Add a workspace");
    _terminal.Type("missing");
    _terminal.Press(ConsoleKey.Enter);

    await WaitFor(app, "is not a directory.");
    Assert.Equal([_root], new WorkspaceList(_fs, _workspaceList).Read());
  }

  [Fact]
  public async Task Start_EmptyWorkspace_ShowsThePackagesTab() {
    TuiApp app = Start();

    await WaitFor(app, "No package is installed in this workspace.");

    Assert.Contains("lip 1.2.3", app.ScreenText);
    Assert.Contains("1 Packages  2 Browse  3 Server  4 Config  5 Settings  6 Develop", app.ScreenText);
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
    await WaitFor(app, "Latest   1.2.0");
    Assert.Contains("License  MIT", app.ScreenText);
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
  public async Task Browse_Filters_AreCollapsedSectionsOfWhatServerModsHave() {
    TuiApp app = Start();
    _terminal.Type("2");
    await WaitFor(app, "Mods — 2 ·");

    // Every section shows its heading only, with the number of its rows.
    Assert.Contains("▸ Game version", app.ScreenText);
    Assert.Contains("▸ Tags", app.ScreenText);
    Assert.Contains("▸ License", app.ScreenText);
    Assert.Contains("▸ Depends on", app.ScreenText);
    Assert.DoesNotContain("[ ]", app.ScreenText);

    // Enter on the heading of the tags shows them: not the tag of the mod for clients.
    _terminal.Type("f");
    _terminal.Press(ConsoleKey.DownArrow);
    _terminal.Press(ConsoleKey.Enter);
    await WaitFor(app, "▾ Tags");
    Assert.Contains("[ ] Server management", app.ScreenText);
    Assert.Contains("[ ] Economy", app.ScreenText);
    Assert.DoesNotContain("Visual & HUD", app.ScreenText);
    Assert.DoesNotContain("[ ] 1.26.60", app.ScreenText);

    // Enter again hides them.
    _terminal.Press(ConsoleKey.Enter);
    await WaitFor(app, "▸ Tags");
    Assert.DoesNotContain("[ ]", app.ScreenText);
  }

  [Fact]
  public async Task Browse_Filters_IncludeAndExcludeSeveralKinds() {
    TuiApp app = Start();
    _terminal.Type("2");
    await WaitFor(app, "Mods — 2 ·");

    // The game version of Alpha.
    _terminal.Type("f");
    _terminal.Press(ConsoleKey.Enter);
    _terminal.Press(ConsoleKey.DownArrow);
    _terminal.Type(" ");
    await WaitFor(app, "Mods — 1 · sort: Relevance · 1 filters");
    Assert.Contains("[+] 1.26.60", app.ScreenText);
    Assert.Contains("Alpha · acme", app.ScreenText);

    // Collapsed, the section says that one of its rows is chosen.
    _terminal.Press(ConsoleKey.LeftArrow);
    await WaitFor(app, "▸ Game version");
    Assert.Contains("1 chosen", app.ScreenText);

    _terminal.Type("c");
    await WaitFor(app, "Mods — 2 · sort: Relevance");

    // Up from the first row goes around to the last: the dependencies. Excluding the one of
    // Alpha leaves Beta.
    _terminal.Press(ConsoleKey.UpArrow);
    _terminal.Press(ConsoleKey.Enter);
    _terminal.Press(ConsoleKey.DownArrow);
    _terminal.Type("x");
    await WaitFor(app, "Mods — 1 · sort: Relevance · 1 filters");
    Assert.Contains("[-] LeviLamina", app.ScreenText);
    Assert.Contains("Beta · acme", app.ScreenText);
    Assert.DoesNotContain("Alpha · acme", app.ScreenText);

    // Including it instead takes the exclusion back.
    _terminal.Type(" ");
    await WaitFor(app, "[+] LeviLamina");
    Assert.Contains("Alpha · acme", app.ScreenText);

    // And the license family of Alpha, with the dependency: still Alpha alone.
    _terminal.Press(ConsoleKey.UpArrow);
    _terminal.Press(ConsoleKey.UpArrow);
    _terminal.Press(ConsoleKey.Enter);
    _terminal.Press(ConsoleKey.DownArrow);
    _terminal.Type(" ");
    await WaitFor(app, "Mods — 1 · sort: Relevance · 2 filters");
    Assert.Contains("[+] Permissive", app.ScreenText);
    Assert.Contains("[ ] No license", app.ScreenText);
  }

  [Fact]
  public async Task Browse_Arrows_MoveBetweenTheFiltersAndTheMods() {
    TuiApp app = Start();
    _terminal.Type("2");
    await WaitFor(app, "Mods — 2 ·");

    // ← goes to the filters, on the left; → and Esc come back.
    _terminal.Press(ConsoleKey.LeftArrow);
    await WaitFor(app, "back to the mods");
    _terminal.Press(ConsoleKey.RightArrow);
    await WaitFor(app, "Enter install");
    _terminal.Press(ConsoleKey.LeftArrow);
    await WaitFor(app, "back to the mods");

    // Enter on a heading shows its rows, and on a row includes it.
    _terminal.Press(ConsoleKey.Enter);
    _terminal.Press(ConsoleKey.DownArrow);
    _terminal.Press(ConsoleKey.Enter);
    await WaitFor(app, "[+] 1.26.60");
    Assert.Contains("Mods — 1 ·", app.ScreenText);

    // ← on a row goes to its heading, and hides the rows.
    _terminal.Press(ConsoleKey.LeftArrow);
    await WaitFor(app, "▸ Game version");
    _terminal.Press(ConsoleKey.Escape);
    await WaitFor(app, "Enter install");
  }

  [Fact]
  public async Task Browse_Search_ArrowsGoOnToTheModsAndEscClearsIt() {
    TuiApp app = Start();
    _terminal.Type("2");
    await WaitFor(app, "Mods — 2 ·");

    // ↓ leaves the search for the mods it found, keeping it.
    _terminal.Type("/acme");
    _terminal.Press(ConsoleKey.DownArrow);
    await WaitFor(app, "2 of 2 ·");
    Assert.Contains("Search acme", app.ScreenText);
    Assert.Contains("Enter install", app.ScreenText);

    // Esc among the mods clears the search.
    _terminal.Press(ConsoleKey.Escape);
    await WaitFor(app, screen => !screen.Contains("Search acme", StringComparison.Ordinal));
    Assert.Contains("Mods — 2 ·", app.ScreenText);
  }

  [Fact]
  public async Task Packages_Enter_WithoutAPackage_AsksWhatToInstall() {
    TuiApp app = Start();
    await WaitFor(app, "No package is installed");

    _terminal.Press(ConsoleKey.Enter);

    await WaitFor(app, "Install packages");
  }

  [Fact]
  public async Task Settings_Arrows_MoveToTheCacheWhereEnterCleansIt() {
    TuiApp app = Start();
    _terminal.Type("5");
    await WaitFor(app, "github_proxy");

    _terminal.Press(ConsoleKey.RightArrow);
    await WaitFor(app, "Enter clean the cache");
    _terminal.Press(ConsoleKey.Enter);
    await WaitFor(app, "and everything in it?");

    // Esc cancels; ← goes back to the configuration.
    _terminal.Press(ConsoleKey.Escape);
    _terminal.Press(ConsoleKey.LeftArrow);
    await WaitFor(app, "Enter edit");
  }

  [Fact]
  public async Task Browse_Lists_GoAroundAtTheirEnds() {
    TuiApp app = Start();
    _terminal.Type("2");
    await WaitFor(app, "1 of 2 ·");

    _terminal.Press(ConsoleKey.UpArrow);
    await WaitFor(app, "2 of 2 ·");
    _terminal.Press(ConsoleKey.DownArrow);
    await WaitFor(app, "1 of 2 ·");
  }

  [Fact]
  public async Task Browse_Enter_AsksForTheVariantAndTheVersionInOneDialog() {
    TuiApp app = Start();
    _terminal.Type("2");
    await WaitFor(app, "Mods — 2 ·");

    _terminal.Press(ConsoleKey.Enter);
    await WaitFor(app, "Install Alpha");
    Assert.Contains("Variant  ◀ (default) ▶  1 of 2", app.ScreenText);
    Assert.Contains("Latest                  1.2.0", app.ScreenText);
    Assert.Contains("1.1.0", app.ScreenText);
    Assert.DoesNotContain("Install packages", app.ScreenText);

    // The variant "lua" has a build for a server in 1.2.0 only; "client" has none at all.
    _terminal.Press(ConsoleKey.RightArrow);
    await WaitFor(app, "Variant  ◀ lua ▶  2 of 2");
    Assert.Contains("github.com/acme/Alpha#lua", app.ScreenText);
    Assert.DoesNotContain("1.1.0", app.ScreenText);

    // c asks for what follows the @ only, for the chosen variant.
    _terminal.Type("c");
    await WaitFor(app, "Install github.com/acme/Alpha#lua");
    Assert.Contains("rev:<commit>", app.ScreenText);
    Assert.DoesNotContain("--platform", app.ScreenText);

    // Cancelling comes back to the versions.
    _terminal.Press(ConsoleKey.Escape);
    await WaitFor(app, "Variant  ◀ lua ▶  2 of 2");
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
    _terminal.Type("5");
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

  /// An empty github_proxy is set, not deleted: lip then connects to GitHub directly; Del removes
  /// it (protocol/config.toml.md, "User configuration").
  [Fact]
  public async Task Settings_EditToEmpty_SetsAnEmptyGithubProxyAndDelRemovesIt() {
    string configPath = _fs.Path.Join(LipDirectories.Config, "config.toml");
    TuiApp app = Start();
    _terminal.Type("5");
    await WaitFor(app, "(not set)");

    _terminal.Press(ConsoleKey.Enter);
    await WaitFor(app, "Set github_proxy");
    _terminal.Press(ConsoleKey.Enter);

    await WaitFor(app, "(empty: connect to GitHub directly)");
    Assert.Equal("format = 4\ngithub_proxy = \"\"\n", _fs.File.ReadAllText(configPath));

    _terminal.Press(ConsoleKey.Delete);
    await WaitFor(app, "(not set)");
    Assert.Equal("format = 4\n", _fs.File.ReadAllText(configPath));
  }

  /// The Settings tab shows and edits the workspace configuration, .lip/config.toml, too.
  [Fact]
  public async Task Settings_Workspace_RestartIsChosenAndWritten() {
    TuiApp app = Start();
    _terminal.Type("5");
    await WaitFor(app, "Workspace ·");
    Assert.Contains("never", app.ScreenText);

    _terminal.Press(ConsoleKey.DownArrow);
    _terminal.Press(ConsoleKey.Enter);
    await WaitFor(app, "Set restart");
    _terminal.Press(ConsoleKey.DownArrow);
    _terminal.Press(ConsoleKey.Enter);

    await WaitFor(app, screen =>
        screen.Contains("on-failure", StringComparison.Ordinal)
            && !screen.Contains("Set restart", StringComparison.Ordinal));
    Assert.Equal(
        "format = 4\nrestart = \"on-failure\"\n",
        _fs.File.ReadAllText(_fs.Path.Join(_root, ".lip", "config.toml")));
  }

  [Fact]
  public async Task Develop_CreateLipToml_AsksForTheIdInTheInterface() {
    TuiApp app = Start();
    _terminal.Type("6");
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
    _terminal.Type("6");
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
      // The server of a game of the build windows without a loader (protocol/lip-run.md).
      File.Copy(
          Path.Join(Environment.SystemDirectory, "cmd.exe"),
          Path.Join(root, WorkspaceProgram.WindowsServerFileName));
      Directory.CreateDirectory(Path.Join(root, ".lip"));
      File.WriteAllText(Path.Join(root, ".lip", "state.json"), """
          { "format": 4, "side": "server", "platform": "win-x64", "packages": [ {
            "manifest": { "format": 4, "id": "github.com/LiteLDev/bds", "version": "1.21.0",
              "variants": { "": [ { "side": "server", "platform": "win-x64" } ] } },
            "variant": "", "explicit": true, "ref": "github.com/LiteLDev/bds@1.21.0",
            "files": [] } ] }
          """);
      FileSystem fs = new();
      TuiApp app = Start((interaction, hookOutput, _) => new LipClient(
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

  [Fact]
  public async Task Config_ServerProperties_EditsAndSavesOnlyTheChangedLines() {
    string original = File
        .ReadAllText(Path.Join(AppContext.BaseDirectory, "Tui", "server.properties"))
        .Replace("\r\n", "\n").Replace("\n", "\r\n");
    string path = _fs.Path.Join(_root, "server.properties");
    _fs.File.WriteAllText(path, original);
    TuiApp app = Start();
    _terminal.Type("4");
    await WaitFor(app, "Dedicated Server");

    // gamemode, the second property: a choice.
    _terminal.Press(ConsoleKey.Enter);
    _terminal.Press(ConsoleKey.DownArrow);
    await WaitFor(app, "Sets the game mode for new players.");
    _terminal.Press(ConsoleKey.Enter);
    await WaitFor(app, "adventure");
    _terminal.Press(ConsoleKey.DownArrow);
    _terminal.Press(ConsoleKey.Enter);
    await WaitFor(app, "(changed)");

    // max-players, three rows below: an integer, which must be one.
    for (int i = 0; i < 4; i++) {
      _terminal.Press(ConsoleKey.DownArrow);
    }

    await WaitFor(app, "The maximum number of players");
    _terminal.Press(ConsoleKey.Enter);
    await WaitFor(app, "Set max-players");
    _terminal.Press(ConsoleKey.U, ConsoleModifiers.Control);
    _terminal.Type("many");
    _terminal.Press(ConsoleKey.Enter);
    await WaitFor(app, "max-players is not set: The value is an integer.");
    _terminal.Press(ConsoleKey.Enter);
    await WaitFor(app, "Set max-players");
    _terminal.Press(ConsoleKey.U, ConsoleModifiers.Control);
    _terminal.Type("20");
    _terminal.Press(ConsoleKey.Enter);
    await WaitFor(app, "s save");
    Assert.Equal(original, _fs.File.ReadAllText(path));

    _terminal.Type("s");
    await WaitFor(app, "Saved server.properties.");

    string[] before = original.Split("\r\n");
    string[] after = _fs.File.ReadAllText(path).Split("\r\n");
    Assert.Equal(before.Length, after.Length);
    Assert.Equal(
        [("gamemode=survival", "gamemode=creative"), ("max-players=10", "max-players=20")],
        before.Zip(after).Where(pair => pair.First != pair.Second));
    Assert.False(_fs.File.Exists(path + ".tmp"));
  }

  [Fact]
  public async Task Config_ModJson_IsEditedAsATreeKeepingItsVersion() {
    await new LockStore(_fs).Save(_root, new LockFile {
      Side = "server",
      Platform = "win-x64",
      Packages = [
        new LockedPackage {
          Manifest = System.Text.Json.JsonDocument.Parse("""
              {
                "format": 4, "id": "github.com/acme/Alpha", "version": "1.0.0",
                "variants": {"": [{"side": "server", "platform": "win-x64",
                  "assets": [{"type": "mcmod", "urls": ["https://example.com/a.zip"]}]}]}
              }
              """).RootElement,
          Variant = "",
          Explicit = true,
          Source = "github.com/acme/Alpha",
          Files = ["mods/Alpha/manifest.json", "mods/Alpha/Alpha.dll"],
        },
      ],
    });
    string path = _fs.Path.Join(_root, "mods", "Alpha", "config", "config.json");
    _fs.AddFile(path, new MockFileData("""
        {
            "version": 2, // kept by the mod
            "enabled": false,
            "limits": {"max": 5, "name": "x"}
        }
        """));
    TuiApp app = Start();
    _terminal.Type("4");
    await WaitFor(app, "Alpha · config/config.json");
    Assert.Contains("it is generated when the server", app.ScreenText);
    Assert.Contains("allowlist.json", app.ScreenText);

    _terminal.Press(ConsoleKey.End);
    await WaitFor(app, "{…} 2 members");
    _terminal.Press(ConsoleKey.Enter);
    _terminal.Press(ConsoleKey.Enter);
    await WaitFor(app, "is the version of the file");
    _terminal.Press(ConsoleKey.DownArrow);
    _terminal.Press(ConsoleKey.Enter);
    _terminal.Press(ConsoleKey.DownArrow);
    _terminal.Press(ConsoleKey.Enter);
    await WaitFor(app, "config.json › limits");
    _terminal.Press(ConsoleKey.Enter);
    await WaitFor(app, "Set max");
    _terminal.Press(ConsoleKey.U, ConsoleModifiers.Control);
    _terminal.Type("9");
    _terminal.Press(ConsoleKey.Enter);
    _terminal.Press(ConsoleKey.Escape);
    await WaitFor(app, "s save");

    // Leaving with changes asks what to do with them.
    _terminal.Type("5");
    await WaitFor(app, "has changes that are not saved");
    _terminal.Press(ConsoleKey.Enter);
    await WaitFor(app, "github_proxy");

    Assert.Equal(
        "{\n    \"version\": 2,\n    \"enabled\": true,\n    \"limits\": {\n        \"max\": 9,\n"
            + "        \"name\": \"x\"\n    }\n}\n",
        _fs.File.ReadAllText(path));
  }

  [Fact]
  public async Task Config_Allowlist_AddsAPlayerToAFileThatDidNotExist() {
    TuiApp app = Start();
    _terminal.Type("4");
    await WaitFor(app, "it is generated when the server");

    _terminal.Press(ConsoleKey.DownArrow);
    await WaitFor(app, "No player is on the allowlist.");
    _terminal.Press(ConsoleKey.Enter);
    _terminal.Type("a");
    await WaitFor(app, "Add a player to the allowlist");
    _terminal.Type("Steve");
    _terminal.Press(ConsoleKey.Enter);
    await WaitFor(app, "(changed)");
    Assert.False(_fs.File.Exists(_fs.Path.Join(_root, "allowlist.json")));
    _terminal.Type("s");
    await WaitFor(app, "Saved allowlist.json.");

    Assert.Contains(
        "\"name\": \"Steve\"", _fs.File.ReadAllText(_fs.Path.Join(_root, "allowlist.json")));
  }
}
