using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
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

namespace Lip.Cli.Tests.Tui.Screenshots;

/// A package that a workspace of a screenshot has installed. Mod: the package has the
/// archive of a mod, as the Mod config page asks of the packages that it lists; the archives
/// of the screenshots have none, so lip cannot plan to uninstall or update such a package.
public sealed record Installed(
    string Id, string Version, bool Explicit = true, string Variant = "", bool Mod = false,
    params string[] Files) {
  /// The packages that its package file names as dependencies.
  public IReadOnlyList<string> Deps { get; init; } = [];
}

/// What the interface of a scenario finds: a server of a made-up user on a made-up machine,
/// the same in every run and on every machine that makes the screenshots. Nothing of it is
/// on the disk or the network. Change it before the scenario starts the interface.
public sealed class World {
  /// The version of lip that the screenshots show.
  public const string Version = "0.41.0";

  public const string Game = "github.com/LiteLDev/bds";
  public const string Loader = "github.com/LiteLDev/LeviLamina";

  /// The game and LeviLamina as the messages of lip name them: as a package argument does
  /// (PackageDisplay).
  public const string GameName = "bds";
  public const string LoaderName = "LeviLamina";
  public const string GameVersion = "1.26.60";
  public const string LoaderVersion = "26.60.3";

  /// The commit that every package of a state file of the screenshots was installed from.
  public const string Commit = "4f1c2d7a9b3e5f60718293a4b5c6d7e8f9012345";

  private readonly List<string> _listed = [];

  /// A world whose selected workspace is the one given, DefaultWorkspace unless it is, on a
  /// terminal of the size given.
  public World(
      Language language, int width = DefaultWidth, int height = DefaultHeight,
      string? workspace = null) {
    Language = language;
    Width = width;
    Height = height;
    Workspace = workspace ?? DefaultWorkspace;
    Fs = new MockFileSystem(new Dictionary<string, MockFileData>(), Workspace);
    Fs.AddDirectory(Workspace);
    Archives.Setup(a => a.ListVersions(It.IsAny<string>())).ReturnsAsync([]);
    ArchiveUnavailableException nothing = new("The screenshots have no archives.");
    Archives.Setup(a => a.ResolveGitRef(It.IsAny<string>(), It.IsAny<GitRef>()))
        .ThrowsAsync(nothing);
    Archives.Setup(a => a.OpenCommit(It.IsAny<string>(), It.IsAny<string>()))
        .ThrowsAsync(nothing);
    Archives.Setup(a => a.OpenVersion(It.IsAny<string>(), It.IsAny<SpecVersion>()))
        .ThrowsAsync(nothing);
    Archives.Setup(a => a.Open(It.IsAny<Ref>())).ThrowsAsync(nothing);
  }

  /// The path of the file system of the scenarios for a path as Windows writes it, which is
  /// what the screenshots show wherever they are made: itself on Windows, and elsewhere the
  /// path with as many characters that Shot.Capture writes back ("/D/servers/survival").
  public static string PathOf(string windows) =>
      OperatingSystem.IsWindows()
          ? windows
          : "/" + windows[..1] + windows[2..].Replace('\\', '/');

  /// The size of the terminal of a screenshot unless the scenario gives another: wide enough
  /// for every page, and no higher than a page of the documentation shows well.
  public const int DefaultWidth = 100;
  public const int DefaultHeight = 28;

  /// The workspace of most screenshots.
  public static string DefaultWorkspace { get; } = PathOf(@"D:\servers\survival");

  /// The workspace that is selected when the interface starts.
  public string Workspace { get; }

  /// The directory of the user configuration.
  public static string ConfigDirectory { get; } = PathOf(@"C:\Users\Steve\AppData\Roaming\lip");

  public Language Language { get; }

  /// The size of the terminal.
  public int Width { get; }

  public int Height { get; }

  public MockFileSystem Fs { get; }

  /// The archives of the client: none, unless the scenario sets them up.
  public Mock<IArchiveProvider> Archives { get; } = new();

  /// The text of the file of the catalogue at a URL: Catalogue.File unless set.
  public Func<string, string> CatalogFile { get; set; } = Catalogue.File;

  public GithubProbe? GithubProbe { get; set; }

  /// The server setup: SetupOfShots unless set.
  public Func<TuiHarness, IServerSetup>? ServerSetup { get; set; }

  public Func<LipClient, bool, Task<SelfUpdateResult>>? SelfUpdate { get; set; }

  public Func<CancellationToken, Task<DaemonState?>>? DaemonStatus { get; set; }

  public bool ManagedServers { get; set; }

  /// The servers of the process: the harness's own unless set.
  public ServerSessions? Servers { get; set; }

  /// The pages of the sidebar: those of lip, as on a system that is not Linux, unless set.
  public IReadOnlyList<PageEntry> Pages { get; set; } = ServerSetupTests.Pages(linux: false);

  /// Makes the client of a workspace: a real one over Archives unless set.
  public Func<IUserInteraction, Func<Stream>, string, LipClient>? CreateClient { get; set; }

  /// Describes a failure as lip does (ErrorReporter.Describe): its message with those of its
  /// causes, the hints that lip gives, and a report that is named and not written.
  public Func<Exception, string[], bool, string, Failure> DescribeFailure { get; set; } =
      (exception, _, cancelled, _) => ErrorReporter.Describe(
          exception, cancelled,
          (_, _) => Path.Join(".lip", "logs", "20261114T201502Z-4120.log"));

  /// Writes the state file of a workspace, the selected one unless given.
  public World Install(params Installed[] packages) => InstallIn(Workspace, packages);

  public World InstallIn(string workspace, params Installed[] packages) {
    File(
        Path.Join(workspace, ".lip", "state.json"),
        """{ "format": 4, "side": "server", "platform": "win-x64", "packages": [ """
            + string.Join(", ", packages.Select(State)) + " ] }");
    return this;
  }

  /// A server: the game and LeviLamina, then the packages given.
  public World InstallServer(params Installed[] packages) =>
      Install([
        new Installed(Game, GameVersion), new Installed(Loader, LoaderVersion), .. packages,
      ]);

  /// Writes a file: at an absolute path, or at one relative to the selected workspace.
  public World File(string path, string text) {
    string full = Path.IsPathRooted(path) ? path : Path.Join(Workspace, path);
    Fs.AddFile(full, new MockFileData(text.ReplaceLineEndings("\n")));
    return this;
  }

  /// Adds workspaces, which exist, to the workspace list after the selected one.
  public World List(params string[] workspaces) {
    foreach (string workspace in workspaces) {
      Fs.AddDirectory(workspace);
      _listed.Add(workspace);
    }

    return this;
  }

  /// Sets a key of the user configuration.
  public World Configure(string key, string value) {
    string file = Path.Join(ConfigDirectory, "config.toml");
    string before = Fs.FileExists(file) ? Fs.File.ReadAllText(file) : "";
    Fs.AddFile(file, new MockFileData($"{before}{key} = \"{value}\"\n"));
    return this;
  }

  /// What the harness starts the interface with.
  public TuiHarnessOptions Options() {
    if (_listed.Count > 0) {
      string[] list = [Workspace, .. _listed];
      Fs.AddFile(
          Path.Join(OperatingSystem.IsWindows() ? @"C:\state" : "/state", WorkspaceList.FileName),
          new MockFileData(JsonSerializer.Serialize(list)));
    }

    return new TuiHarnessOptions {
      Language = Language,
      Width = Width,
      Height = Height,
      Version = Version,
      FileSystem = Fs,
      WorkingDirectory = Workspace,
      Catalog = url => CatalogFile(url),
      GithubProbe = GithubProbe,
      ServerSetup = ServerSetup ?? (harness => new SetupOfShots(harness, this)),
      SelfUpdate = SelfUpdate,
      DaemonStatus = DaemonStatus,
      ManagedServers = ManagedServers,
      Servers = Servers,
      Pages = Pages,
      DescribeFailure = DescribeFailure,
      CreateClient = CreateClient ?? Client,
    };
  }

  /// The client of a workspace: the real one, over the archives of the world and the user
  /// configuration of the made-up user.
  public LipClient Client(IUserInteraction interaction, Func<Stream> hookOutput, string root) =>
      new(
          Fs, interaction, new CacheService(Fs, interaction),
          new ConfigService(Fs, interaction, () => root, ConfigDirectory),
          new McmodService(
              Fs, interaction, new LockStore(Fs), Archives.Object, new ArchiveReader(),
              new McmodInstaller(Fs, new CommandRunner(hookOutput), interaction),
              workspaceRoot: () => root));

  /// The source that the state file records for a package that was installed at its version
  /// (protocol/state.json.md): its repository, with its variant, at the version.
  public static string Source(Installed package) =>
      package.Id + (package.Variant.Length == 0 ? "" : $"#{package.Variant}")
          + $"@{package.Version}";

  private static string State(Installed package) {
    string commit = package.Id == Game
        ? ""
        : $"\"commit\": \"{Commit}\", ";
    string files = string.Join(", ", package.Files.Select(file => $"\"{file}\""));
    string deps = package.Deps.Count == 0
        ? ""
        : $", \"deps\": {{ {string.Join(", ", package.Deps.Select(d => $"\"{d}\": \"*\""))} }}";
    string assets = package.Mod
        ? """, "assets": [ { "type": "mcmod", "urls": ["https://example.com/mod.zip"] } ]"""
        : "";
    return $$"""
        { "manifest": { "format": 4, "id": "{{package.Id}}", "version": "{{package.Version}}",
            "variants": { "{{package.Variant}}": [
              { "side": "server", "platform": "win-x64"{{deps}}{{assets}} } ] } },
          "variant": "{{package.Variant}}",
          "explicit": {{(package.Explicit ? "true" : "false")}},
          "ref": "{{Source(package)}}", {{commit}}"files": [{{files}}] }
        """;
  }
}

/// The server setup of the screenshots: the games and the LeviLamina versions that a user
/// would find, and a setup that says what lip setup says and writes the state file only.
public sealed class SetupOfShots(TuiHarness harness, World world) : IServerSetup {
  /// What Setup returns: what the setup changed, or would change.
  public OperationResult Result { get; set; } = OperationResult.Changed;

  /// Completes when the setup that is carried out may go on: at once unless set, for a
  /// screenshot of the setup while it runs.
  public Task Release { get; set; } = Task.CompletedTask;

  public Task<IReadOnlyList<GameVersion>> GameVersions(
      LipClient client, OperationSession? session = null) =>
      Task.FromResult<IReadOnlyList<GameVersion>>([
        new GameVersion(SemVersion.Parse("1.26.60"), Windows: true, Linux: true),
        new GameVersion(SemVersion.Parse("1.26.50"), Windows: true, Linux: true),
        new GameVersion(SemVersion.Parse("1.26.40"), Windows: true, Linux: true),
        new GameVersion(SemVersion.Parse("1.26.30"), Windows: true, Linux: false),
        new GameVersion(SemVersion.Parse("1.26.20"), Windows: true, Linux: true),
        new GameVersion(SemVersion.Parse("1.26.10"), Windows: true, Linux: true),
        new GameVersion(SemVersion.Parse("1.26.0"), Windows: true, Linux: true),
        new GameVersion(SemVersion.Parse("1.21.130"), Windows: true, Linux: true),
      ]);

  public Task<IReadOnlyList<SemVersion>> LoaderVersions(
      LipClient client,
      SemVersion game,
      CancellationToken cancellationToken,
      OperationSession? session = null) =>
      Task.FromResult<IReadOnlyList<SemVersion>>([
        SemVersion.Parse("26.60.3"), SemVersion.Parse("26.60.2"), SemVersion.Parse("26.60.1"),
        SemVersion.Parse("26.60.0"),
      ]);

  public async Task<OperationResult> Setup(
      LipClient client, SetupRequest request, OperationRun run) {
    IUserInteraction ui = harness.InteractionOf(client);
    string loader = (request.LoaderVersion ?? SemVersion.Parse(World.LoaderVersion)).ToString();
    if (run.DryRun && !Result.Changes) {
      // The workspace has this game and this loader already.
      return Result;
    }

    if (run.DryRun) {
      await ui.PrintInfo(
          $"Dry run: would install {World.GameName} {request.Game} (3 files)");
      if (request.Loader) {
        await ui.PrintInfo(
            $"Dry run: would install {World.LoaderName} {loader} (12 files)");
      }

      await ui.PrintInfo(
          GameSetup.EulaDryRunMessage + $" ({GameSetup.EulaUrl}) and the Privacy Policy"
              + $" ({GameSetup.PrivacyPolicyUrl}), and download the game.");
      return Result;
    }

    await ui.RunWithProgress(
        "Downloading https://www.minecraft.net/bedrockdedicatedserver/bin-win/"
            + $"bedrock-server-{request.Game}.2.zip",
        progress => {
          progress.Report(100);
          return Task.CompletedTask;
        });
    if (request.Loader) {
      await ui.RunWithProgress(
          $"Downloading https://github.com/LiteLDev/LeviLamina/releases/download/v{loader}/"
              + "levilamina-release-windows-x64.zip",
          async progress => {
            progress.Report(62);
            await Release;
          });
    } else {
      await Release;
    }

    await ui.PrintInfo($"Installing {World.GameName} {request.Game}");
    if (request.Loader) {
      await ui.PrintInfo($"Installing {World.LoaderName} {loader}");
    }

    world.InstallIn(
        harness.WorkspaceOf(client),
        request.Loader
            ? [
              new Installed(World.Game, request.Game.ToString()),
              new Installed(World.Loader, loader),
            ]
            : [new Installed(World.Game, request.Game.ToString())]);

    return Result;
  }
}
