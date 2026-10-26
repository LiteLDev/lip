using System.Diagnostics;
using System.IO.Abstractions;
using DotNet.Globbing;
using Flurl;
using Lip.Core.Entities;
using Lip.Core.Infrastructure;
using Lip.Core.Sources;

namespace Lip.Core.Services;

public interface IPackageInstaller {
  Task InstallPackage(
      PackageArtifact packageArtifact,
      bool dryRun,
      bool explicitInstall,
      bool ignoreScripts);

  Task UninstallPackage(
      PackageId packageId,
      bool dryRun,
      bool ignoreScripts);
}

public class PackageInstaller(
    ICommandRunner commandRunner,
    IFileSystem fileSystem,
    IUserInteraction userInteraction,
    ISourceService sourceService,
    IWorkspaceService workspaceService) : IPackageInstaller {
  private readonly ICommandRunner _commandRunner = commandRunner;
  private readonly IFileSystem _fileSystem = fileSystem;
  private readonly IUserInteraction _userInteraction = userInteraction;

  private readonly ISourceService _sourceService = sourceService;
  private readonly IWorkspaceService _workspaceService = workspaceService;

  public async Task InstallPackage(
      PackageArtifact packageArtifact,
      bool dryRun,
      bool explicitInstall,
      bool ignoreScripts) {
    IEnumerable<PackageSpec> installedPackages = await _workspaceService.GetInstalledPackages(
        IWorkspaceService.PackageScope.All);

    if (installedPackages.FirstOrDefault(p => p.Id == packageArtifact.Spec.Id) is PackageSpec existingPackageSpec) {
      throw new InvalidOperationException($"Cannot install package {packageArtifact.Spec.Id} version {packageArtifact.Spec.Version} because it is already installed with version {existingPackageSpec.Version}");
    }

    if (dryRun) {
      await _userInteraction.PrintInfo(
          $"Dry run: would install package {packageArtifact.Spec.Id} version {packageArtifact.Spec.Version}");

      return;
    }

    await _userInteraction.PrintInfo(
        $"Installing package {packageArtifact.Spec.Id} version {packageArtifact.Spec.Version}");

    using Stream manifestStream = await packageArtifact.Source.OpenRead("tooth.json");
    PackageManifest manifest = await PackageManifest.FromStream(manifestStream);
    PackageManifestVariant variant = manifest.GetVariant(packageArtifact.Spec.Id.Variant);

    // Step 1: Run pre-install scripts.

    if (!ignoreScripts) {
      foreach (string script in variant.Scripts.PreInstall) {
        await _commandRunner.Run(script);
      }
    }

    // Step 2: Place files.

    List<IFileInfo> placedFiles = [];

    foreach (PackageManifestAsset asset in variant.Assets) {
      ISource assetSource = asset.Type switch {
        PackageManifestAsset.AssetType.Self => packageArtifact.Source,
        PackageManifestAsset.AssetType.Uncompressed => await GetSource(asset.Urls, isArchive: false),
        PackageManifestAsset.AssetType.Tar => await GetSource(asset.Urls, isArchive: true),
        PackageManifestAsset.AssetType.Tgz => await GetSource(asset.Urls, isArchive: true),
        PackageManifestAsset.AssetType.Zip => await GetSource(asset.Urls, isArchive: true),
        _ => throw new UnreachableException(),
      };

      // Match placements first, so lazy sources only download the files that are placed.
      IReadOnlyList<(string Key, string TargetPath)> matches = PlacementMatcher.Match(
          assetSource.Keys,
          asset.Placements);

      if (matches.Count == 0) {
        continue;
      }

      await assetSource.Prefetch(matches.Select(m => m.Key).Distinct());

      foreach ((string key, string targetPath) in matches) {
        IFileInfo targetLocation = _fileSystem.FileInfo.New(targetPath);

        using Stream sourceStream = await assetSource.OpenRead(key);
        using Stream targetStream = _fileSystem.CreateFileWithDirectory(targetLocation.FullName);
        await sourceStream.CopyToAsync(targetStream);

        placedFiles.Add(targetLocation);
      }
    }

    // Step 3: Run post-install scripts.

    if (!ignoreScripts) {
      foreach (string script in variant.Scripts.Install) {
        await _commandRunner.Run(script);
      }

      foreach (string script in variant.Scripts.PostInstall) {
        await _commandRunner.Run(script);
      }
    }

    // Step 4: Add package to workspace state.

    await _workspaceService.AddInstalledPackage(
        packageArtifact.Spec,
        manifest,
        placedFiles,
        explicitInstall);
  }

  public async Task UninstallPackage(
      PackageId packageId,
      bool dryRun,
      bool ignoreScripts) {
    IEnumerable<PackageSpec> installedPackages = await _workspaceService.GetInstalledPackages(
        IWorkspaceService.PackageScope.All);


    PackageSpec existingPackageSpec = installedPackages.FirstOrDefault(p => p.Id == packageId)
        ?? throw new InvalidOperationException($"Cannot uninstall package {packageId} because it is not installed");

    if (dryRun) {
      await _userInteraction.PrintInfo(
          $"Dry run: would uninstall package {existingPackageSpec.Id} version {existingPackageSpec.Version}");

      return;
    }

    await _userInteraction.PrintInfo(
        $"Uninstalling package {existingPackageSpec.Id} version {existingPackageSpec.Version}");

    PackageManifest manifest = await _workspaceService.GetInstalledPackageManifest(existingPackageSpec);
    PackageManifestVariant variant = manifest.GetVariant(existingPackageSpec.Id.Variant);

    // Step 1: Run pre-uninstall scripts.

    if (!ignoreScripts) {
      foreach (string script in variant.Scripts.PreUninstall) {
        await _commandRunner.Run(script);
      }

      foreach (string script in variant.Scripts.Uninstall) {
        await _commandRunner.Run(script);
      }
    }

    // Step 2: Remove placed files.

    foreach (IFileInfo file in await _workspaceService.GetInstalledPackageFiles(existingPackageSpec)) {
      string relativePath = GetWorkingDirectoryRelativePath(file.FullName);

      // Also match the bare file name so that older manifests relying on name-only
      // matching keep preserving the same files.
      if (variant.PreserveFiles.Any(preserveGlob =>
          preserveGlob.IsMatch(relativePath) || preserveGlob.IsMatch(file.Name))) {
        continue;
      }

      if (!_fileSystem.File.Exists(file.FullName)) {
        await _userInteraction.PrintWarning(
            $"File '{file.FullName}' does not exist, skipping removal.");
      }

      file.Delete();
    }

    // Step 3: Remove the files specified to be removed.

    foreach (Glob glob in variant.RemoveFiles) {
      List<string> pathsToRemove = [];

      foreach (string path in _fileSystem.Directory.EnumerateFileSystemEntries(
          ".",
          "*",
          SearchOption.AllDirectories)) {
        // Match the glob against the path relative to the working directory, so a
        // pattern like `extra.log` only matches `./extra.log` instead of any file
        // with the same name in any subdirectory.
        string relativePath = GetWorkingDirectoryRelativePath(path);

        if (string.IsNullOrEmpty(relativePath)) {
          continue;
        }

        // A trailing separator is required to match a directory, so also match
        // patterns like `temp/` against directories.
        if (glob.IsMatch(relativePath)
            || (_fileSystem.Directory.Exists(path) && glob.IsMatch($"{relativePath}/"))) {
          pathsToRemove.Add(path);
        }
      }

      // Delete deeper paths first so that removing a directory recursively does not
      // invalidate the other paths collected above.
      foreach (string path in pathsToRemove.OrderByDescending(
          p => p.Count(c => c is '/' or '\\'))) {
        if (_fileSystem.File.Exists(path)) {
          _fileSystem.File.Delete(path);
        } else if (_fileSystem.Directory.Exists(path)) {
          _fileSystem.Directory.Delete(path, recursive: true);
        }
      }
    }

    // Step 4: Run post-uninstall scripts.

    if (!ignoreScripts) {
      foreach (string script in variant.Scripts.PostUninstall) {
        await _commandRunner.Run(script);
      }
    }

    // Step 4: Remove package from workspace state.

    await _workspaceService.RemoveInstalledPackage(existingPackageSpec);
  }

  /// Returns the given path relative to the working directory, using `/` as the
  /// separator, so it can be matched against manifest file patterns.
  private string GetWorkingDirectoryRelativePath(string path) {
    static string Normalize(string value) => value
        .Replace(Path.DirectorySeparatorChar, '/')
        .Replace(Path.AltDirectorySeparatorChar, '/');

    // Resolve both paths through the file system abstraction, because the absolute
    // path of "." is not the process working directory when a mock file system is used.
    string rootPath = Normalize(_fileSystem.Path.GetFullPath(".")).TrimEnd('/');
    string fullPath = Normalize(_fileSystem.Path.GetFullPath(path));

    if (fullPath == rootPath) {
      return string.Empty;
    }

    // Require a directory boundary after the root, so a sibling directory with the
    // same prefix is not treated as a path inside the working directory.
    return fullPath.StartsWith($"{rootPath}/", StringComparison.OrdinalIgnoreCase)
        ? fullPath[(rootPath.Length + 1)..]
        : fullPath;
  }

  private async Task<ISource> GetSource(IEnumerable<Url> urls, bool isArchive) {
    List<Exception> exceptions = [];

    foreach (Url url in urls) {
      try {
        return await _sourceService.Get(url, isArchive);
      }
      catch (Exception ex) {
        exceptions.Add(new Exception($"Failed to get file source from {url}", ex));
      }
    }

    throw new AggregateException("Failed to get file source from all provided URLs", exceptions);
  }
}
