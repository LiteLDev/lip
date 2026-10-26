using System.IO.Abstractions;
using System.Text.Json;
using Flurl;
using Lip.Core.Entities;
using Lip.Core.Git;
using Lip.Core.Infrastructure;
using Lip.Core.Migration.PackageManifests;
using Lip.Core.PackageRegistries;
using Lip.Core.Services;
using Semver;

namespace Lip.Core.PublicApi;

public interface ILipClient {
  Task CacheClean();
  Task ConfigDelete(string key);
  Task<string> ConfigGet(string key);
  Task<IDictionary<string, string>> ConfigList();
  Task ConfigSet(string key, string value);
  Task Init();
  Task Install(IEnumerable<string> packages, bool dryRun, bool ignoreScripts, bool noDependencies);
  Task<(IEnumerable<string> ExplicitInstalled, IEnumerable<string> ImplicitInstalled)> List();
  Task Migrate(string file, string output);
  Task Uninstall(IEnumerable<string> packages, bool dryRun, bool ignoreScripts, bool noDependencies);
  Task Update(IEnumerable<string> packages, bool dryRun, bool ignoreScripts);
  Task<IEnumerable<string>> Versions(string package);
  Task<string> View(string package);
}

public class LipClient(
    IFileSystem fileSystem,
    ICacheService cacheService,
    IConfigService configService,
    IInstallService installService,
    IPackageRegistry packageRegistry,
    IWorkspaceService workspaceService) : ILipClient, IDisposable {
  private static readonly JsonSerializerOptions _jsonSerializerOptions = new() {
    WriteIndented = true
  };

  private readonly IFileSystem _fileSystem = fileSystem;
  private readonly ICacheService _cacheService = cacheService;
  private readonly IConfigService _configService = configService;
  private readonly IInstallService _installService = installService;
  private readonly IPackageRegistry _packageRegistry = packageRegistry;
  private readonly IWorkspaceService _workspaceService = workspaceService;

  // Resources created by Create() and owned by this client.
  private readonly List<IDisposable> _ownedResources = [];

  public static async Task<LipClient> Create(IUserInteraction userInteraction, IFileSystem? fileSystem = null) {
    fileSystem ??= new FileSystem();

    ConfigService configService = new(fileSystem, userInteraction);

    CacheService cacheService = new(fileSystem, userInteraction);
    FileDownloader fileDownloader = new(userInteraction);
    Url? githubProxy = await configService.Get<Url?>("github_proxy");
    GitRemoteClient gitRemoteClient = new(githubProxy);
    GitRemotePackageRegistry gitRemotePackageRegistry = new(gitRemoteClient);

    CommandRunner commandRunner = new();
    SourceService sourceService = new(
        fileDownloader,
        gitRemoteClient,
        gitRemotePackageRegistry,
        userInteraction,
        cacheService,
        githubProxy);
    WorkspaceService workspaceService = new(fileSystem, userInteraction);

    PackageInstaller packageInstaller = new(
        commandRunner,
        fileSystem,
        userInteraction,
        sourceService,
        workspaceService);
    CompositePackageRegistry packageRegistry = new([
      [new WorkspaceServicePackageRegistry(workspaceService)],
      [gitRemotePackageRegistry],
    ]);

    InstallService installService = new(
        userInteraction,
        packageInstaller,
        packageRegistry,
        sourceService,
        workspaceService);

    LipClient lipClient = new(
        fileSystem,
        cacheService,
        configService,
        installService,
        packageRegistry,
        workspaceService);
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

  public async Task Init() {
    PackageManifest packageManifest = new() {
      Path = "github.com/user/repo",
      Version = new(0),
    };

    using Stream stream = _fileSystem.CreateFileWithDirectory("tooth.json");

    await JsonSerializer.SerializeAsync(stream, packageManifest, _jsonSerializerOptions);
  }

  public async Task Install(IEnumerable<string> packages, bool dryRun, bool ignoreScripts, bool noDependencies) {
    List<PackageSpec> parsedPackages = [];
    List<PackageId> flexiblePackages = [];
    List<LocalPackageSpec> localPackages = [];
    List<RemotePackageSpec> remotePackages = [];

    foreach (string package in packages) {
      // Order of parsing is:
      // 1. PackageSpec
      // 2. PackageId (flexible package)
      // 3. Local file
      // 4. Remote URL

      List<Exception> exceptions = [];

      try {
        PackageSpec packageSpec = PackageSpec.Parse(package);
        parsedPackages.Add(packageSpec);
        continue;
      }
      catch (Exception ex) {
        exceptions.Add(ex);
      }

      try {
        PackageId packageId = PackageId.Parse(package);
        flexiblePackages.Add(packageId);
        continue;
      }
      catch (Exception ex) {
        exceptions.Add(ex);
      }

      try {
        LocalPackageSpec localPackageSpec = LocalPackageSpec.Parse(package, _fileSystem);
        localPackages.Add(localPackageSpec);
        continue;
      }
      catch (Exception ex) {
        exceptions.Add(ex);
      }

      try {
        RemotePackageSpec remotePackage = RemotePackageSpec.Parse(package);
        remotePackages.Add(remotePackage);
        continue;
      }
      catch (Exception ex) {
        exceptions.Add(ex);
      }

      throw new AggregateException($"Failed to parse package '{package}'.", exceptions);
    }

    await _installService.InstallPackages(
        parsedPackages,
        flexiblePackages,
        localPackages,
        remotePackages,
        dryRun,
        ignoreScripts,
        noDependencies);
  }

  public async Task<(IEnumerable<string> ExplicitInstalled, IEnumerable<string> ImplicitInstalled)> List() {
    IEnumerable<PackageSpec> explicitInstalled = await _workspaceService
        .GetInstalledPackages(IWorkspaceService.PackageScope.Explicit);
    IEnumerable<PackageSpec> implicitInstalled = await _workspaceService
        .GetInstalledPackages(IWorkspaceService.PackageScope.Implicit);

    return (explicitInstalled.Select(p => p.ToString()), implicitInstalled.Select(p => p.ToString()));
  }

  public async Task Migrate(string file, string output) {
    using Stream inputStream = _fileSystem.File.OpenRead(file);

    JsonDocument inputJson = await JsonDocument.ParseAsync(inputStream);

    PackageManifest packageManifest = PackageManifestMigration.Migrate(inputJson);

    using Stream outputStream = _fileSystem.CreateFileWithDirectory(output);

    await JsonSerializer.SerializeAsync(outputStream, packageManifest, _jsonSerializerOptions);
  }

  public async Task Uninstall(IEnumerable<string> packages, bool dryRun, bool ignoreScripts, bool noDependencies) {
    IEnumerable<PackageId> parsedPackages = packages.Select(PackageId.Parse);

    await _installService.UninstallPackages(parsedPackages, dryRun, ignoreScripts, noDependencies);
  }

  public Task Update(IEnumerable<string> packages, bool dryRun, bool ignoreScripts) {
    IEnumerable<PackageSpec> parsedPackages = [];
    IEnumerable<PackageId> flexiblePackages = [];
    IEnumerable<LocalPackageSpec> localPackages = [];
    IEnumerable<RemotePackageSpec> remotePackages = [];

    foreach (string package in packages) {
      List<Exception> exceptions = [];

      try {
        PackageSpec packageSpec = PackageSpec.Parse(package);
        parsedPackages = parsedPackages.Append(packageSpec);
        continue;
      }
      catch (Exception ex) {
        exceptions.Add(ex);
      }

      try {
        PackageId packageId = PackageId.Parse(package);
        flexiblePackages = flexiblePackages.Append(packageId);
        continue;
      }
      catch (Exception ex) {
        exceptions.Add(ex);
      }

      try {
        LocalPackageSpec localPackageSpec = LocalPackageSpec.Parse(package, _fileSystem);
        localPackages = localPackages.Append(localPackageSpec);
        continue;
      }
      catch (Exception ex) {
        exceptions.Add(ex);
      }

      try {
        RemotePackageSpec remotePackage = RemotePackageSpec.Parse(package);
        remotePackages = remotePackages.Append(remotePackage);
        continue;
      }
      catch (Exception ex) {
        exceptions.Add(ex);
      }

      throw new AggregateException($"Failed to parse package '{package}'.", exceptions);
    }

    return _installService.UpdatePackages(
        parsedPackages,
        flexiblePackages,
        localPackages,
        remotePackages,
        dryRun,
        ignoreScripts);
  }

  public async Task<IEnumerable<string>> Versions(string package) {
    PackageId packageId = PackageId.Parse(package);

    IOrderedEnumerable<SemVersion> semVersions = await _packageRegistry.GetAvailableVersions(
        packageId);

    return semVersions.Select(v => v.ToString());
  }

  public async Task<string> View(string package) {
    List<Exception> exceptions = [];

    try {
      PackageSpec packageSpec = PackageSpec.Parse(package);
      PackageManifest packageManifest = await _packageRegistry.GetPackageManifest(packageSpec);
      return JsonSerializer.Serialize(packageManifest, _jsonSerializerOptions);
    }
    catch (Exception ex) {
      exceptions.Add(ex);
    }

    try {
      PackageId packageId = PackageId.Parse(package);
      PackageSpec packageSpec = await _installService.GetLatestVersion(packageId);
      PackageManifest packageManifest = await _packageRegistry.GetPackageManifest(packageSpec);
      return JsonSerializer.Serialize(packageManifest, _jsonSerializerOptions);
    }
    catch (Exception ex) {
      exceptions.Add(ex);
    }

    throw new AggregateException(
        $"Failed to parse package '{package}' or retrieve its manifest.", exceptions);
  }
}
