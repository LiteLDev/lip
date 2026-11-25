using System.IO.Abstractions;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Flurl;
using Lip.Core.Config;
using Lip.Core.Git;
using Lip.Core.Infrastructure;
using Lip.Core.Json;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Compat;
using Lip.Core.Mcmod.Install;
using Lip.Core.Mcmod.Lock;
using Lip.Core.Services;

namespace Lip.Core.PublicApi;

/// The commands of lip (protocol/lip.md) over the services that carry them out.
public sealed class LipClient {
  private readonly IFileSystem _fileSystem;
  private readonly IUserInteraction _userInteraction;
  private readonly ICacheService _cacheService;
  private readonly ConfigService _configService;
  // The service of a command, given whether it is a dry run, and its session, if any.
  private readonly Func<bool, OperationSession?, Task<McmodService>> _createMcmodService;
  // The absolute path of the workspace root.
  private readonly Func<string> _workspaceRoot;

  public LipClient(
      IFileSystem fileSystem,
      IUserInteraction userInteraction,
      ICacheService cacheService,
      ConfigService configService,
      McmodService mcmodService)
      : this(
          fileSystem,
          userInteraction,
          cacheService,
          configService,
          (_, _) => Task.FromResult(mcmodService),
          fileSystem.Directory.GetCurrentDirectory) {
  }

  private LipClient(
      IFileSystem fileSystem,
      IUserInteraction userInteraction,
      ICacheService cacheService,
      ConfigService configService,
      Func<bool, OperationSession?, Task<McmodService>> createMcmodService,
      Func<string> workspaceRoot) {
    _fileSystem = fileSystem;
    _userInteraction = userInteraction;
    _cacheService = cacheService;
    _configService = configService;
    _createMcmodService = createMcmodService;
    _workspaceRoot = workspaceRoot;
  }

  public static LipClient Create(IUserInteraction userInteraction) =>
      Create(new FileSystem(), userInteraction);

  /// A client whose commands read the configuration, migrating liprc.json
  /// (protocol/lip-compatibility.md, "liprc.json"), only when they need it: commands that do not,
  /// such as lip pkg init, work whatever the configuration directory holds. A dry run reads
  /// liprc.json without migrating it.
  public static LipClient Create(IFileSystem fileSystem, IUserInteraction userInteraction) =>
      Create(fileSystem, userInteraction, hookOutput: null);

  /// As above; hook commands write their output to the stream that hookOutput opens for each,
  /// instead of the standard error of lip. The workspace is the working directory of the current
  /// invocation (Invocation): of the file system for this process, otherwise that of the client
  /// that the invocation is of.
  public static LipClient Create(
      IFileSystem fileSystem, IUserInteraction userInteraction, Func<Stream>? hookOutput) =>
      Create(fileSystem, userInteraction, hookOutput, workspace: null);

  /// As above, with workspace, an absolute path, as the workspace root when it is given: the
  /// workspace that the interface selected (protocol/lip-tui.md, "Workspaces").
  public static LipClient Create(
      IFileSystem fileSystem,
      IUserInteraction userInteraction,
      Func<Stream>? hookOutput,
      string? workspace) {
    Invocation invocation = Invocation.Current;
    Func<string> workspaceRoot = workspace is not null
        ? () => workspace
        : invocation == Invocation.Process
            ? fileSystem.Directory.GetCurrentDirectory
            : () => invocation.WorkingDirectory;
    ConfigService configService = new(fileSystem, userInteraction, workspaceRoot);
    CacheService cacheService = new(fileSystem, userInteraction);
    return new(
        fileSystem,
        userInteraction,
        cacheService,
        configService,
        (dryRun, session) => CreateMcmodService(
            fileSystem, userInteraction, cacheService, configService, dryRun, hookOutput,
            workspaceRoot, session),
        workspaceRoot);
  }

  /// The service of the commands that may reach GitHub, which reads github_proxy; for a dry run,
  /// without writing the configuration. With a session, what the service reads of the network
  /// is what the other services of the session read of it; the configuration, the workspace
  /// and the cache it reads itself.
  private static async Task<McmodService> CreateMcmodService(
      IFileSystem fileSystem,
      IUserInteraction userInteraction,
      CacheService cacheService,
      ConfigService configService,
      bool dryRun,
      Func<Stream>? hookOutput,
      Func<string> workspaceRoot,
      OperationSession? session) {
    // An empty github_proxy, like an absent one, means GitHub is reached directly
    // (protocol/config.toml.md, "User configuration").
    string? githubProxyValue = await configService.GetGithubProxy(dryRun);
    Url? githubProxy = string.IsNullOrEmpty(githubProxyValue) ? null : Url.Parse(githubProxyValue);
    string described = ConfigService.DescribeProxy(githubProxyValue);
    RunTrace.Write("config", githubProxy is null
        ? $"github_proxy {described}: GitHub is reached directly"
        : $"GitHub is reached through github_proxy {described}");
    OperationSession.Network network = session?.NetworkFor(githubProxyValue, githubProxy)
        ?? new OperationSession.Network(githubProxyValue, githubProxy);
    // Every download of the command, the system dependencies' included, goes through
    // github_proxy as config.toml.md defines.
    FileDownloader fileDownloader = new(userInteraction, githubProxy);

    CommandRunner commandRunner = new(hookOutput);
    // One provider per command: whether it leaves the cache unchanged is of the command.
    ArchiveProvider archiveProvider = new(
        network.Git, cacheService, fileSystem, fileDownloader, network.Archives);

    return new(
        fileSystem,
        userInteraction,
        new LockStore(fileSystem),
        archiveProvider,
        new ArchiveReader(),
        new McmodInstaller(fileSystem, commandRunner, userInteraction),
        workspaceRoot,
        systemPreparer: SystemDependencies.Create(fileSystem, fileDownloader, userInteraction),
        resolverCache: network.Resolver);
  }

  /// The service of a command; a dry run (protocol/lip.md, "Dry run") reads the configuration
  /// without writing it.
  private Task<McmodService> GetMcmodService(
      bool dryRun = false, OperationSession? session = null) =>
      _createMcmodService(dryRun, session);

  public async Task CacheClean() {
    await _cacheService.Clean();
  }

  /// lip config delete (protocol/lip-config.md); workspace is --workspace.
  public async Task ConfigDelete(string key, bool workspace = false) {
    await _configService.Delete(key, workspace);
  }

  /// lip config get: the value of the key, or null when it has none.
  public async Task<string?> ConfigGet(string key, bool workspace = false) {
    return await _configService.Get(key, workspace);
  }

  /// lip config list: every key of the scope with its value, null when it has none.
  public async Task<IDictionary<string, string?>> ConfigList(bool workspace = false) {
    return await _configService.List(workspace);
  }

  /// lip config set; the value may be empty.
  public async Task ConfigSet(string key, string value, bool workspace = false) {
    await _configService.Set(key, value, workspace);
  }

  /// Writes a lip.toml with one entry for the server on win-x64 (protocol/lip-pkg.md,
  /// "lip pkg init"), asking
  /// the user for its package id. Fails when lip.toml exists, or when the user cannot be asked.
  public async Task Init() {
    string directory = _workspaceRoot();
    string name = PackageFormat.FileName;
    string path = _fileSystem.Path.Join(directory, name);
    if (_fileSystem.Directory.Exists(path)) {
      throw new InvalidOperationException(
          $"A directory named {name} exists in {directory}, so lip pkg init cannot write {name} "
          + $"there. Move or rename that directory and run 'lip pkg init' again.");
    }

    if (_fileSystem.File.Exists(path)) {
      throw new InvalidOperationException(
          $"{name} already exists in {directory}, and lip pkg init does not replace it. To edit "
          + $"the package, change {name} by hand; to start over, delete {name} and run 'lip pkg "
          + "init' again.");
    }

    string id = await _userInteraction.AskValid(
        "the package id of the package (a Go module path, e.g. github.com/owner/repo)",
        answer => Golang.Org.X.Mod.Module.CheckPath(answer) is Exception ex
            ? ex.Message
            : null);

    JsonObject document = new() {
      [PackageFormat.FormatKey] = PackageFormat.Format,
      ["id"] = id,
      ["version"] = "0.1.0",
      ["variants"] = new JsonObject {
        [""] = new JsonArray(new JsonObject {
          ["side"] = "server",
          ["platform"] = "win-x64",
        }),
      },
    };

    // Asking may take a while; a lip.toml written meanwhile is not replaced.
    RunTrace.Write("init", $"writing {name} in {directory} for {id}");
    Stream stream;
    try {
      stream = _fileSystem.File.Open(path, FileMode.CreateNew);
    }
    catch (IOException ex)
        when (_fileSystem.File.Exists(path) || _fileSystem.Directory.Exists(path)) {
      throw new IOException(
          $"{name} was created in {directory} by another program while lip was asking, and lip "
          + $"pkg init does not replace it. Delete {name} and run 'lip pkg init' again to start "
          + "over.", ex);
    }

    using (stream) {
      await stream.WriteAsync(Encoding.UTF8.GetBytes(TomlJson.Serialize(document)));
    }
  }

  /// lip install (protocol/lip-install.md). acceptEula: --accept-eula, for an install carried out
  /// as a setup; confirmed: the user confirmed the plan that its dry run showed (the interface).
  /// session: the session of the command (OperationSession), if it has one.
  /// acceptRosettaLicense: --accept-rosetta-license. Returns what the install changed, or, as a
  /// dry run, would change.
  public async Task<OperationResult> Install(
      IEnumerable<string> packages,
      bool dryRun,
      CancellationToken cancellationToken = default,
      bool acceptEula = false,
      bool confirmed = false,
      OperationSession? session = null,
      bool acceptRosettaLicense = false) {
    return await (await GetMcmodService(dryRun, session)).Install(
        [.. packages],
        new OperationOptions(
            dryRun, cancellationToken, acceptEula, confirmed, acceptRosettaLicense));
  }

  /// lip setup (protocol/lip-setup.md). acceptEula: --accept-eula; confirmed: the user confirmed
  /// the plan that its dry run showed (the interface). session: the session of the command, if
  /// it has one. acceptRosettaLicense: --accept-rosetta-license. Returns what the setup changed,
  /// or, as a dry run, would change.
  public async Task<OperationResult> Setup(
      SetupRequest request,
      bool dryRun,
      CancellationToken cancellationToken = default,
      bool acceptEula = false,
      bool confirmed = false,
      OperationSession? session = null,
      bool acceptRosettaLicense = false) {
    return await (await GetMcmodService(dryRun, session)).Setup(
        request,
        new OperationOptions(
            dryRun, cancellationToken, acceptEula, confirmed, acceptRosettaLicense));
  }

  /// The versions of the game that the interface offers for a server setup (protocol/lip-tui.md,
  /// "Server setup"). With a session, the builds list is read once in it.
  public async Task<IReadOnlyList<GameVersion>> GameVersions(OperationSession? session = null) =>
      await (await GetMcmodService(dryRun: true, session)).GameVersions();

  /// The versions of LeviLamina that fit a game version, from highest to lowest
  /// (protocol/lip-tui.md, "Server setup"). With a session, what is read of LeviLamina is read
  /// once in it.
  public async Task<IReadOnlyList<Semver.SemVersion>> LoaderVersions(
      Semver.SemVersion game,
      CancellationToken cancellationToken = default,
      OperationSession? session = null) =>
      await (await GetMcmodService(dryRun: true, session)).LoaderVersions(
          game, cancellationToken);

  /// The versions of a package id: of the version tags of its repository, or of the server
  /// (protocol/lip-package.md, "Resolution"), read as a dry run reads them.
  public async Task<IReadOnlyList<Semver.SemVersion>> PackageVersions(string id) =>
      await (await GetMcmodService(dryRun: true)).PackageVersions(id);

  /// The value of github_proxy: a URI, "" (GitHub is reached directly), or null when it is not
  /// configured (protocol/config.toml.md, "User configuration"), read without writing a file.
  public async Task<string?> GithubProxy() => await _configService.GetGithubProxy(dryRun: true);

  /// The value of language: en, zh-Hans or zh-Hant, or null when it has no value
  /// (protocol/config.toml.md, "User configuration"), read without writing a file.
  public async Task<string?> Language() => await _configService.GetLanguage(dryRun: true);

  /// Installed packages as "identity@version": explicitly installed ones and dependency packages,
  /// in installation order (protocol/lip-list.md).
  public async Task<(IEnumerable<string> ExplicitInstalled, IEnumerable<string> ImplicitInstalled)> List() {
    IReadOnlyList<InstalledPackage> packages = await (await GetMcmodService()).List();

    return (
        packages.Where(p => p.Explicit).Select(p => $"{p.Identity}@{p.Version}").Distinct(),
        packages.Where(p => !p.Explicit).Select(p => $"{p.Identity}@{p.Version}").Distinct());
  }

  /// The installed packages, in installation order (protocol/lip-list.md).
  public async Task<IReadOnlyList<InstalledPackage>> ListPackages() =>
      await (await GetMcmodService()).List();

  /// The installed mods and their configuration files (protocol/lip-tui.md, "Configuration").
  public async Task<IReadOnlyList<ModConfig>> ListModConfigs() {
    string root = _workspaceRoot();
    LockFile? lockFile = await new LockStore(_fileSystem).Load(root);
    return ModConfigs.Find(_fileSystem, root, lockFile);
  }

  /// The text of a configuration file of the workspace, at a '/'-separated path relative to it,
  /// or null when it does not exist (protocol/lip-tui.md, "Configuration").
  public async Task<string?> ReadConfigFile(string path) {
    string full = ConfigFilePath(path);
    if (!_fileSystem.File.Exists(full)) {
      return null;
    }

    // Decoded as it is, a byte order mark included, so that writing it back keeps it; a file
    // that is not UTF-8 cannot be read, rather than be written back with its bytes replaced.
    return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
        .GetString(await _fileSystem.File.ReadAllBytesAsync(full));
  }

  /// Writes a configuration file of the workspace atomically (protocol/README.md, "Files").
  public async Task WriteConfigFile(string path, string text) {
    string full = ConfigFilePath(path);
    string temporary = full + ".tmp";
    await _fileSystem.File.WriteAllBytesAsync(temporary, Encoding.UTF8.GetBytes(text));
    try {
      _fileSystem.File.Move(temporary, full, overwrite: true);
    }
    catch {
      try {
        _fileSystem.File.Delete(temporary);
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
        // The renaming failed already; that is what the user hears of.
      }

      throw;
    }
  }

  private string ConfigFilePath(string path) {
    if (path.Length == 0 || path.StartsWith('/') || path.Contains('\\') || path.Contains(':')
        || path.Split('/').Any(segment => segment is "" or "." or "..")) {
      throw new ArgumentException($"'{path}' is not a path in the workspace.", nameof(path));
    }

    return _fileSystem.Path.Join(_workspaceRoot(), path);
  }

  /// Migrates the tooth.json of format 1, 2 or 3 in the current directory to a lip.toml
  /// (RepositoryMigrator, protocol/lip-compatibility.md, "lip pkg migrate"), asking the user for
  /// missing values. Everything dropped or converted with another meaning is reported as a warning.
  public async Task Migrate() {
    IReadOnlyList<string> notes = await RepositoryMigrator.Migrate(
        _fileSystem, _workspaceRoot(), _userInteraction);

    foreach (string note in notes) {
      await _userInteraction.PrintWarning(note);
    }
  }

  /// Runs the server of the workspace (protocol/lip-run.md) for the current invocation
  /// (Invocation) and returns its exit code. The server reads what lip reads from input; it is
  /// asked to stop when the invocation receives SIGINT, SIGTERM or SIGHUP, or on Windows a console
  /// control signal, none of which then ends lip, or when it is abandoned. inputIsConsole: input is
  /// an interactive Windows console. acceptRosettaLicense: --accept-rosetta-license.
  public async Task<int> Run(
      Stream input, bool inputIsConsole, bool acceptRosettaLicense = false) {
    Invocation invocation = Invocation.Current;
    return await (await GetMcmodService()).Run(
        input,
        inputIsConsole,
        invocation.ListenForStop,
        PipedProgramStarter.ForCurrent(),
        new ServerRunOptions { AcceptRosettaLicense = acceptRosettaLicense });
  }

  /// Runs the server as Run does, started by starter and asked to stop through listen: for the
  /// interface (protocol/lip-tui.md, "Server"), which reads the output of the server itself.
  public async Task<int> Run(
      Stream input,
      StopListener listen,
      IProgramStarter starter,
      ServerRunOptions? options = null) {
    return await (await GetMcmodService()).Run(
        input, inputIsConsole: false, listen, starter, options);
  }

  /// Checks for, or installs, the latest release of lip newer than running
  /// (protocol/lip-self-update.md). In a Debug build only, the releases are those of the
  /// repository at LIP_SELF_UPDATE_REPOSITORY when it is set, to test lip against a fake
  /// release; a Release build, which is what lip is released as, has no such hook.
  public async Task<SelfUpdateResult> SelfUpdate(Semver.SemVersion running, bool checkOnly) {
    string? githubProxyValue = await _configService.GetGithubProxy();
    Url? githubProxy = string.IsNullOrEmpty(githubProxyValue) ? null : Url.Parse(githubProxyValue);
#if DEBUG
    string? repository =
        Invocation.Current.GetEnvironmentVariable("LIP_SELF_UPDATE_REPOSITORY");
#else
    string? repository = null;
#endif
    SelfUpdater updater = new(
        _fileSystem,
        new FileDownloader(_userInteraction, githubProxy),
        new GitRemoteClient(githubProxy),
        new ShellInstallerStarter(),
        SelfUpdateHost.Current,
        string.IsNullOrEmpty(repository) ? null : Url.Parse(repository));
    SelfUpdateResult result = await updater.Run(running, checkOnly);
    if (result.Outcome is SelfUpdateOutcome.Replaced or SelfUpdateOutcome.InstallerStarted) {
      Invocation.Current.LipReplaced = true;
    }

    return result;
  }

  /// Installs what the programs of the workspace in the current directory need of the system,
  /// when it is not installed: the Wine of lip off Windows, and the Visual C++ runtime
  /// (protocol/lip-workspace.md, "System dependencies"). Operations and lip run do so by
  /// themselves when they need it; this does it ahead of them, e.g. while setting up.
  /// acceptRosettaLicense: --accept-rosetta-license.
  public async Task PrepareSystem(
      CancellationToken cancellationToken = default, bool acceptRosettaLicense = false) {
    await (await GetMcmodService()).PrepareSystem(acceptRosettaLicense, cancellationToken);
  }

  /// lip uninstall (protocol/lip-uninstall.md). session: the session of the command, if it
  /// has one. acceptRosettaLicense: --accept-rosetta-license. Returns what the uninstall
  /// changed, or, as a dry run, would change.
  public async Task<OperationResult> Uninstall(
      IEnumerable<string> packages,
      bool dryRun,
      CancellationToken cancellationToken = default,
      OperationSession? session = null,
      bool acceptRosettaLicense = false) {
    return await (await GetMcmodService(dryRun, session)).Uninstall(
        [.. packages],
        new OperationOptions(
            dryRun, cancellationToken, AcceptRosettaLicense: acceptRosettaLicense));
  }

  /// Updates the given packages to the requested (or newest) version; without packages, every
  /// explicitly installed package. session: the session of the command, if it has one.
  /// acceptRosettaLicense: --accept-rosetta-license. Returns what the update changed, or, as a
  /// dry run, would change.
  public async Task<OperationResult> Update(
      IEnumerable<string> packages,
      bool dryRun,
      CancellationToken cancellationToken = default,
      OperationSession? session = null,
      bool acceptRosettaLicense = false) {
    return await (await GetMcmodService(dryRun, session)).Update(
        [.. packages],
        new OperationOptions(
            dryRun, cancellationToken, AcceptRosettaLicense: acceptRosettaLicense));
  }

  /// Updates every mod that the user installed, each to the highest version that fits, or as
  /// its branch, tag, commit, URL or local path is now (McmodService.UpdateMods): what the
  /// interface means by updating all. session: the session of the command, if it has one.
  public async Task<OperationResult> UpdateMods(
      bool dryRun,
      CancellationToken cancellationToken = default,
      OperationSession? session = null) {
    return await (await GetMcmodService(dryRun, session)).UpdateMods(
        new OperationOptions(dryRun, cancellationToken));
  }
}
