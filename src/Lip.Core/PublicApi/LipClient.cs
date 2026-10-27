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
using Lip.Core.Migration.PackageManifests;
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
  Task Migrate(string file, string output);
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
  private static readonly JsonSerializerOptions _jsonSerializerOptions = new() {
    WriteIndented = true
  };

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
    DocumentParser documentParser = new();
    ArchiveProvider archiveProvider = new(
        gitRemoteClient, cacheService, fileSystem, fileDownloader, githubProxy);

    McmodService mcmodService = new(
        fileSystem,
        userInteraction,
        new LockStore(fileSystem),
        archiveProvider,
        new ArchiveReader(documentParser),
        new ToothCompat(),
        documentParser,
        [new McmodInstaller(fileSystem, commandRunner, documentParser, userInteraction)]);

    LipClient lipClient = new(
        fileSystem, userInteraction, cacheService, configService, mcmodService);
    lipClient._ownedResources.Add(gitRemoteClient);

    return lipClient;
  }

  // Implemented explicitly so that RPC hosts exposing the client's public methods (Lip.Daemon)
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

  /// Writes a format 4 manifest.json (a concrete native mod) named after the current directory.
  public async Task Init() {
    string path = ManifestFormat.ManifestFileName;
    if (_fileSystem.File.Exists(path)) {
      throw new InvalidOperationException($"{path} already exists.");
    }

    string directoryName = _fileSystem.Path.GetFileName(
        Path.TrimEndingDirectorySeparator(_fileSystem.Directory.GetCurrentDirectory()));
    string name = PackageNames.FromText(directoryName, "my-mod");

    JsonObject manifest = new() {
      [ManifestFormat.FormatVersionKey] = ManifestFormat.Version,
      [ManifestFormat.FormatUuidKey] = ManifestFormat.Uuid,
      ["name"] = name,
      ["entry"] = $"{name}.dll",
      ["type"] = "native",
      ["version"] = "0.1.0",
      ["dependencies"] = new JsonArray(),
    };

    using Stream stream = _fileSystem.CreateFileWithDirectory(path);
    await JsonSerializer.SerializeAsync(stream, manifest, _jsonSerializerOptions);
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

  /// Installed packages as "identity@version": explicitly installed ones and dependencies.
  public async Task<(IEnumerable<string> ExplicitInstalled, IEnumerable<string> ImplicitInstalled)> List() {
    IReadOnlyList<InstalledPackage> packages = await _mcmodService.List();

    return (
        packages.Where(p => p.Explicit).Select(p => $"{p.Identity}@{p.Version}").Distinct(),
        packages.Where(p => !p.Explicit).Select(p => $"{p.Identity}@{p.Version}").Distinct());
  }

  /// Converts a tooth.json (format 1–3) into a format 4 manifest.json. Everything that cannot be
  /// converted is reported as a warning.
  public async Task Migrate(string file, string output) {
    PackageManifest packageManifest;
    using (Stream inputStream = _fileSystem.File.OpenRead(file)) {
      using JsonDocument inputJson = await JsonDocument.ParseAsync(inputStream);
      packageManifest = PackageManifestMigration.Migrate(inputJson);
    }

    ToothMigrator.Result result = ToothMigrator.Migrate(packageManifest);

    using (Stream outputStream = _fileSystem.CreateFileWithDirectory(output)) {
      await JsonSerializer.SerializeAsync(outputStream, result.Document, _jsonSerializerOptions);
    }

    foreach (string note in result.Notes) {
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
