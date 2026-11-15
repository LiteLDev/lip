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

/// The commands of lip (protocol/lip.md), other than lip pkg pack, over the services that carry
/// them out.
public sealed class LipClient {
  private readonly IFileSystem _fileSystem;
  private readonly IUserInteraction _userInteraction;
  private readonly ICacheService _cacheService;
  private readonly ConfigService _configService;
  // The service of a command, given whether it is a dry run.
  private readonly Func<bool, Task<McmodService>> _createMcmodService;
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
          _ => Task.FromResult(mcmodService),
          fileSystem.Directory.GetCurrentDirectory) {
  }

  private LipClient(
      IFileSystem fileSystem,
      IUserInteraction userInteraction,
      ICacheService cacheService,
      ConfigService configService,
      Func<bool, Task<McmodService>> createMcmodService,
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
        dryRun => CreateMcmodService(
            fileSystem, userInteraction, cacheService, configService, dryRun, hookOutput,
            workspaceRoot),
        workspaceRoot);
  }

  /// The service of the commands that may reach GitHub, which reads github_proxy; for a dry run,
  /// without writing the configuration.
  private static async Task<McmodService> CreateMcmodService(
      IFileSystem fileSystem,
      IUserInteraction userInteraction,
      CacheService cacheService,
      ConfigService configService,
      bool dryRun,
      Func<Stream>? hookOutput,
      Func<string> workspaceRoot) {
    FileDownloader fileDownloader = new(userInteraction);
    // An empty github_proxy, like an absent one, means GitHub is reached directly
    // (protocol/config.toml.md, "User configuration").
    string? githubProxyValue = await configService.GetGithubProxy(dryRun);
    Url? githubProxy = string.IsNullOrEmpty(githubProxyValue) ? null : Url.Parse(githubProxyValue);
    string described = ConfigService.DescribeProxy(githubProxyValue);
    RunTrace.Write("config", githubProxy is null
        ? $"github_proxy {described}: GitHub is reached directly"
        : $"GitHub is reached through github_proxy {described}");
    GitRemoteClient gitRemoteClient = new(githubProxy);

    CommandRunner commandRunner = new(hookOutput);
    ArchiveProvider archiveProvider = new(
        gitRemoteClient, cacheService, fileSystem, fileDownloader, githubProxy);

    return new(
        fileSystem,
        userInteraction,
        new LockStore(fileSystem),
        archiveProvider,
        new ArchiveReader(),
        new McmodInstaller(fileSystem, commandRunner, userInteraction),
        workspaceRoot,
        systemPreparer: SystemDependencies.Create(fileSystem, fileDownloader, userInteraction));
  }

  /// The service of a command; a dry run (protocol/lip.md, "Dry run") reads the configuration
  /// without writing it.
  private Task<McmodService> GetMcmodService(bool dryRun = false) =>
      _createMcmodService(dryRun);

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
  public async Task Install(
      IEnumerable<string> packages,
      bool dryRun,
      CancellationToken cancellationToken = default,
      bool acceptEula = false,
      bool confirmed = false) {
    await (await GetMcmodService(dryRun)).Install(
        [.. packages],
        new OperationOptions(dryRun, cancellationToken, acceptEula, confirmed));
  }

  /// lip setup (protocol/lip-setup.md). acceptEula: --accept-eula; confirmed: the user confirmed
  /// the plan that its dry run showed (the interface).
  public async Task Setup(
      SetupRequest request,
      bool dryRun,
      CancellationToken cancellationToken = default,
      bool acceptEula = false,
      bool confirmed = false) {
    await (await GetMcmodService(dryRun)).Setup(
        request, new OperationOptions(dryRun, cancellationToken, acceptEula, confirmed));
  }

  /// The versions of the game that the interface offers for a new server (protocol/lip-tui.md,
  /// "New server").
  public async Task<IReadOnlyList<GameVersion>> GameVersions() =>
      await (await GetMcmodService(dryRun: true)).GameVersions();

  /// The versions of LeviLamina that fit a game version, from highest to lowest
  /// (protocol/lip-tui.md, "New server").
  public async Task<IReadOnlyList<Semver.SemVersion>> LoaderVersions(
      Semver.SemVersion game, CancellationToken cancellationToken = default) =>
      await (await GetMcmodService(dryRun: true)).LoaderVersions(game, cancellationToken);

  /// The value of github_proxy: a URI, "" (GitHub is reached directly), or null when it is not
  /// configured (protocol/config.toml.md, "User configuration"), read without writing a file.
  public async Task<string?> GithubProxy() => await _configService.GetGithubProxy(dryRun: true);

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
  /// an interactive Windows console.
  public async Task<int> Run(Stream input, bool inputIsConsole) {
    Invocation invocation = Invocation.Current;
    return await (await GetMcmodService()).Run(
        input, inputIsConsole, invocation.ListenForStop, PipedProgramStarter.ForCurrent());
  }

  /// Runs the server as Run does, started by starter and asked to stop through listen: for the
  /// interface (protocol/lip-tui.md, "Server"), which reads the output of the server itself.
  public async Task<int> Run(Stream input, StopListener listen, IProgramStarter starter) {
    return await (await GetMcmodService()).Run(input, inputIsConsole: false, listen, starter);
  }

  /// Checks for, or installs, the latest release of lip newer than running
  /// (protocol/lip-self-update.md). In a Debug build only, the releases are read from
  /// LIP_SELF_UPDATE_RELEASES_URL when it is set, to test lip against a fake release; a Release
  /// build, which is what lip is released as, has no such hook.
  public async Task<SelfUpdateResult> SelfUpdate(Semver.SemVersion running, bool checkOnly) {
    string? githubProxyValue = await _configService.GetGithubProxy();
    Url? githubProxy = string.IsNullOrEmpty(githubProxyValue) ? null : Url.Parse(githubProxyValue);
#if DEBUG
    string? releasesUrl =
        Invocation.Current.GetEnvironmentVariable("LIP_SELF_UPDATE_RELEASES_URL");
#else
    string? releasesUrl = null;
#endif
    SelfUpdater updater = new(
        _fileSystem,
        new FileDownloader(_userInteraction),
        new ShellElevatedStarter(),
        SelfUpdateHost.Current,
        githubProxy,
        string.IsNullOrEmpty(releasesUrl) ? null : Url.Parse(releasesUrl));
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
  public async Task PrepareSystem(CancellationToken cancellationToken = default) {
    await (await GetMcmodService()).PrepareSystem(cancellationToken);
  }

  public async Task Uninstall(
      IEnumerable<string> packages,
      bool dryRun,
      CancellationToken cancellationToken = default) {
    await (await GetMcmodService(dryRun)).Uninstall(
        [.. packages],
        new OperationOptions(dryRun, cancellationToken));
  }

  /// Updates the given packages to the requested (or newest) version; without packages, every
  /// explicitly installed package.
  public async Task Update(
      IEnumerable<string> packages,
      bool dryRun,
      CancellationToken cancellationToken = default) {
    await (await GetMcmodService(dryRun)).Update(
        [.. packages],
        new OperationOptions(dryRun, cancellationToken));
  }

  /// The result of lip pkg view as JSON (protocol/lip-pkg.md, "lip pkg view"): the package file
  /// of the first archive the package argument names, and the versions of its remote repository.
  public async Task<string> View(string package) {
    return await (await GetMcmodService()).View(package);
  }
}
