using System.IO.Abstractions;
using System.Text.Json;
using System.Text.Json.Nodes;
using Flurl;
using Lip.Core.Entities;
using Lip.Core.Git;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Compat;
using Lip.Core.Mcmod.Install;
using Lip.Core.Mcmod.Lock;
using Lip.Core.Services;

namespace Lip.Core.PublicApi;

public interface ILipClient {
  Task CacheClean();
  Task ConfigDelete(string key);
  Task<string> ConfigGet(string key);
  Task<IDictionary<string, string>> ConfigList();
  Task ConfigSet(string key, string value);
  Task Init();
  Task Install(
      IEnumerable<string> packages,
      bool dryRun,
      bool ignoreScripts,
      bool noDependencies,
      string? rid = null,
      CancellationToken cancellationToken = default);
  Task<(IEnumerable<string> ExplicitInstalled, IEnumerable<string> ImplicitInstalled)> List();
  Task Migrate();
  Task Uninstall(
      IEnumerable<string> packages,
      bool dryRun,
      bool ignoreScripts,
      bool noDependencies,
      string? rid = null,
      CancellationToken cancellationToken = default);
  Task Update(
      IEnumerable<string> packages,
      bool dryRun,
      bool ignoreScripts,
      string? rid = null,
      CancellationToken cancellationToken = default);
  Task<IEnumerable<string>> Versions(string package);
  Task<string> View(string package);
}

public class LipClient(
    IFileSystem fileSystem,
    IUserInteraction userInteraction,
    ICacheService cacheService,
    IConfigService configService,
    McmodService mcmodService) : ILipClient, IDisposable {
  private readonly IFileSystem _fileSystem = fileSystem;
  private readonly IUserInteraction _userInteraction = userInteraction;
  private readonly ICacheService _cacheService = cacheService;
  private readonly IConfigService _configService = configService;
  private readonly McmodService _mcmodService = mcmodService;

  // Resources created by Create() and owned by this client.
  private readonly List<IDisposable> _ownedResources = [];

  public static async Task<LipClient> Create(
      IUserInteraction userInteraction, IFileSystem? fileSystem = null) {
    fileSystem ??= new FileSystem();

    ConfigService configService = new(fileSystem, userInteraction);

    CacheService cacheService = new(fileSystem, userInteraction);
    FileDownloader fileDownloader = new(userInteraction);
    Url? githubProxy = await configService.Get<Url?>("github_proxy");
    GitRemoteClient gitRemoteClient = new(githubProxy);

    CommandRunner commandRunner = new();
    ArchiveProvider archiveProvider = new(
        gitRemoteClient, cacheService, fileSystem, fileDownloader, githubProxy);

    McmodService mcmodService = new(
        fileSystem,
        userInteraction,
        new LockStore(fileSystem),
        archiveProvider,
        new ArchiveReader(),
        new McmodInstaller(fileSystem, commandRunner, userInteraction));

    LipClient lipClient = new(
        fileSystem, userInteraction, cacheService, configService, mcmodService);
    lipClient._ownedResources.Add(gitRemoteClient);

    return lipClient;
  }

  // Implemented explicitly so that RPC hosts exposing the client's public methods (lip rpc)
  // do not expose Dispose.
  void IDisposable.Dispose() {
    foreach (IDisposable resource in _ownedResources) {
      resource.Dispose();
    }
    _ownedResources.Clear();
    GC.SuppressFinalize(this);
  }

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

  /// Writes a tooth.json of format 4 with one entry for the server on win-x64 (protocol/cli.md
  /// §5.7), asking the user for its tooth path. Fails when tooth.json exists, or when the user
  /// cannot be asked.
  public async Task Init() {
    string path = ToothFormat.ToothFileName;
    if (_fileSystem.File.Exists(path)) {
      throw new InvalidOperationException($"{path} already exists.");
    }

    string tooth = await ToothMigrator.AskValid(
        _userInteraction,
        "the tooth path of the package (a Go module path, e.g. github.com/owner/repo)",
        answer => Golang.Org.X.Mod.Module.CheckPath(answer) is Exception ex
            ? $"'{answer}' is not a Go module path ({ex.Message})."
            : null);

    JsonObject document = new() {
      [ToothFormat.FormatVersionKey] = ToothFormat.FormatVersion,
      ["format_uuid"] = ToothFormat.FormatUuid,
      ["tooth"] = tooth,
      ["version"] = "0.1.0",
      ["variants"] = new JsonObject {
        [""] = new JsonArray(new JsonObject {
          ["platform"] = "server",
          ["runtime"] = "win-x64",
        }),
      },
    };

    // Asking may take a while; a tooth.json written meanwhile is not replaced.
    using Stream stream = _fileSystem.File.Open(path, FileMode.CreateNew);
    await stream.WriteAsync(ToothMigrator.Serialize(document));
  }

  public async Task Install(
      IEnumerable<string> packages,
      bool dryRun,
      bool ignoreScripts,
      bool noDependencies,
      string? rid = null,
      CancellationToken cancellationToken = default) {
    await _mcmodService.Install(
        [.. packages], Options(dryRun, ignoreScripts, noDependencies, rid, cancellationToken));
  }

  /// Installed packages as "identity@version": explicitly installed ones and dependency packages,
  /// in installation order (protocol/cli.md §5.4).
  public async Task<(IEnumerable<string> ExplicitInstalled, IEnumerable<string> ImplicitInstalled)> List() {
    IReadOnlyList<InstalledPackage> packages = await _mcmodService.List();

    return (
        packages.Where(p => p.Explicit).Select(p => $"{p.Identity}@{p.Version}").Distinct(),
        packages.Where(p => !p.Explicit).Select(p => $"{p.Identity}@{p.Version}").Distinct());
  }

  /// Rewrites the tooth.json of format 1, 2 or 3 in the current directory as a tooth.json of
  /// format 4 (RepositoryMigrator, protocol/cli.md §5.8), asking the user for missing values.
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
      string? rid = null,
      CancellationToken cancellationToken = default) {
    await _mcmodService.Uninstall(
        [.. packages], Options(dryRun, ignoreScripts, noDependencies, rid, cancellationToken));
  }

  /// Updates the given packages to the requested (or newest) version; without packages, every
  /// explicitly installed package.
  public async Task Update(
      IEnumerable<string> packages,
      bool dryRun,
      bool ignoreScripts,
      string? rid = null,
      CancellationToken cancellationToken = default) {
    await _mcmodService.Update(
        [.. packages],
        Options(dryRun, ignoreScripts, noDependencies: false, rid, cancellationToken));
  }

  public async Task<IEnumerable<string>> Versions(string package) {
    return await _mcmodService.Versions(package);
  }

  public async Task<string> View(string package) {
    return await _mcmodService.View(package);
  }

  private static OperationOptions Options(
      bool dryRun,
      bool ignoreScripts,
      bool noDependencies,
      string? rid,
      CancellationToken cancellationToken) {
    return new OperationOptions(
        dryRun,
        ignoreScripts,
        noDependencies,
        string.IsNullOrEmpty(rid) ? null : rid,
        cancellationToken);
  }
}
