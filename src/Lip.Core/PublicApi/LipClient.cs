using System.IO.Abstractions;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Flurl;
using Lip.Core.Git;
using Lip.Core.Infrastructure;
using Lip.Core.Json;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Compat;
using Lip.Core.Mcmod.Install;
using Lip.Core.Mcmod.Lock;
using Lip.Core.Services;

namespace Lip.Core.PublicApi;

/// The commands of lip (protocol/cli.md §5), other than pack, over the services that carry them
/// out.
public sealed class LipClient {
  private readonly IFileSystem _fileSystem;
  private readonly IUserInteraction _userInteraction;
  private readonly ICacheService _cacheService;
  private readonly ConfigService _configService;
  private readonly Lazy<Task<McmodService>> _mcmodService;

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
          () => Task.FromResult(mcmodService)) {
  }

  private LipClient(
      IFileSystem fileSystem,
      IUserInteraction userInteraction,
      ICacheService cacheService,
      ConfigService configService,
      Func<Task<McmodService>> createMcmodService) {
    _fileSystem = fileSystem;
    _userInteraction = userInteraction;
    _cacheService = cacheService;
    _configService = configService;
    _mcmodService = new(createMcmodService);
  }

  public static LipClient Create(IUserInteraction userInteraction) =>
      Create(new FileSystem(), userInteraction);

  /// A client whose commands read the configuration, migrating liprc.json
  /// (protocol/compatibility.md §4), only when they first need it: commands that do not, such as
  /// init, work whatever the configuration directory holds.
  public static LipClient Create(IFileSystem fileSystem, IUserInteraction userInteraction) {
    ConfigService configService = new(fileSystem, userInteraction);
    CacheService cacheService = new(fileSystem, userInteraction);
    return new(
        fileSystem,
        userInteraction,
        cacheService,
        configService,
        () => CreateMcmodService(fileSystem, userInteraction, cacheService, configService));
  }

  /// The service of the commands that may reach GitHub, which reads github_proxy.
  private static async Task<McmodService> CreateMcmodService(
      IFileSystem fileSystem,
      IUserInteraction userInteraction,
      CacheService cacheService,
      ConfigService configService) {
    FileDownloader fileDownloader = new(userInteraction);
    string? githubProxyValue = await configService.GetGithubProxy();
    Url? githubProxy = githubProxyValue is null ? null : Url.Parse(githubProxyValue);
    RunTrace.Write("config", githubProxy is null
        ? "no github_proxy: GitHub is reached directly"
        : $"GitHub is reached through github_proxy {ConfigService.DescribeProxy(githubProxyValue)}");
    GitRemoteClient gitRemoteClient = new(githubProxy);

    CommandRunner commandRunner = new();
    ArchiveProvider archiveProvider = new(
        gitRemoteClient, cacheService, fileSystem, fileDownloader, githubProxy);

    return new(
        fileSystem,
        userInteraction,
        new LockStore(fileSystem),
        archiveProvider,
        new ArchiveReader(),
        new McmodInstaller(fileSystem, commandRunner, userInteraction));
  }

  private Task<McmodService> GetMcmodService() => _mcmodService.Value;

  public async Task CacheClean() {
    await _cacheService.Clean();
  }

  public async Task ConfigDelete(string key) {
    await _configService.Delete(key);
  }

  public async Task<string> ConfigGet(string key) {
    return await _configService.Get(key);
  }

  public async Task<IDictionary<string, string>> ConfigList() {
    return await _configService.List();
  }

  public async Task ConfigSet(string key, string value) {
    await _configService.Set(key, value);
  }

  /// Writes a lip.toml with one entry for the server on win-x64 (protocol/cli.md §5.7), asking
  /// the user for its package id. Fails when lip.toml exists, or when the user cannot be asked.
  public async Task Init() {
    string path = PackageFormat.FileName;
    string directory = _fileSystem.Directory.GetCurrentDirectory();
    if (_fileSystem.Directory.Exists(path)) {
      throw new InvalidOperationException(
          $"A directory named {path} exists in {directory}, so lip init cannot write {path} there. "
          + $"Move or rename that directory and run 'lip init' again.");
    }

    if (_fileSystem.File.Exists(path)) {
      throw new InvalidOperationException(
          $"{path} already exists in {directory}, and lip init does not replace it. To edit the "
          + $"package, change {path} by hand; to start over, delete {path} and run 'lip init' "
          + "again.");
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
    RunTrace.Write("init", $"writing {path} in {directory} for {id}");
    Stream stream;
    try {
      stream = _fileSystem.File.Open(path, FileMode.CreateNew);
    }
    catch (IOException ex)
        when (_fileSystem.File.Exists(path) || _fileSystem.Directory.Exists(path)) {
      throw new IOException(
          $"{path} was created in {directory} by another program while lip was asking, and lip "
          + $"init does not replace it. Delete {path} and run 'lip init' again to start over.", ex);
    }

    using (stream) {
      await stream.WriteAsync(Encoding.UTF8.GetBytes(TomlJson.Serialize(document)));
    }
  }

  public async Task Install(
      IEnumerable<string> packages,
      bool dryRun,
      bool ignoreScripts,
      bool noDependencies,
      string? platform = null,
      CancellationToken cancellationToken = default) {
    await (await GetMcmodService()).Install(
        [.. packages],
        Options(dryRun, ignoreScripts, noDependencies, platform, cancellationToken));
  }

  /// Installed packages as "identity@version": explicitly installed ones and dependency packages,
  /// in installation order (protocol/cli.md §5.4).
  public async Task<(IEnumerable<string> ExplicitInstalled, IEnumerable<string> ImplicitInstalled)> List() {
    IReadOnlyList<InstalledPackage> packages = await (await GetMcmodService()).List();

    return (
        packages.Where(p => p.Explicit).Select(p => $"{p.Identity}@{p.Version}").Distinct(),
        packages.Where(p => !p.Explicit).Select(p => $"{p.Identity}@{p.Version}").Distinct());
  }

  /// Migrates the tooth.json of format 1, 2 or 3 in the current directory to a lip.toml
  /// (RepositoryMigrator, protocol/compatibility.md §5), asking the user for missing values.
  /// Everything dropped or converted with another meaning is reported as a warning.
  public async Task Migrate() {
    IReadOnlyList<string> notes = await RepositoryMigrator.Migrate(
        _fileSystem, _fileSystem.Directory.GetCurrentDirectory(), _userInteraction);

    foreach (string note in notes) {
      await _userInteraction.PrintWarning(note);
    }
  }

  public async Task Uninstall(
      IEnumerable<string> packages,
      bool dryRun,
      bool ignoreScripts,
      bool noDependencies,
      string? platform = null,
      CancellationToken cancellationToken = default) {
    await (await GetMcmodService()).Uninstall(
        [.. packages],
        Options(dryRun, ignoreScripts, noDependencies, platform, cancellationToken));
  }

  /// Updates the given packages to the requested (or newest) version; without packages, every
  /// explicitly installed package.
  public async Task Update(
      IEnumerable<string> packages,
      bool dryRun,
      bool ignoreScripts,
      string? platform = null,
      CancellationToken cancellationToken = default) {
    await (await GetMcmodService()).Update(
        [.. packages],
        Options(dryRun, ignoreScripts, noDependencies: false, platform, cancellationToken));
  }

  public async Task<IEnumerable<string>> Versions(string package) {
    return await (await GetMcmodService()).Versions(package);
  }

  public async Task<string> View(string package) {
    return await (await GetMcmodService()).View(package);
  }

  private static OperationOptions Options(
      bool dryRun,
      bool ignoreScripts,
      bool noDependencies,
      string? platform,
      CancellationToken cancellationToken) {
    return new OperationOptions(
        dryRun,
        ignoreScripts,
        noDependencies,
        string.IsNullOrEmpty(platform) ? null : platform,
        cancellationToken);
  }
}
