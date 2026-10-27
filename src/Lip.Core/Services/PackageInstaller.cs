using System.IO.Abstractions;
using Lip.Core.Entities;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod.Compat;
using Lip.Core.Sources;
using Semver;

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

    // The requested path and the tag's version identify the package, even if the manifest
    // declares something else (e.g. a renamed or transferred repository, or a stale version).
    PackageSpec spec = packageArtifact.Spec;
    if (manifest.Path != spec.Id.Path || manifest.Version != spec.Version) {
      // Differences in case or build metadata alone are not worth a warning.
      if (!string.Equals(manifest.Path, spec.Id.Path, StringComparison.OrdinalIgnoreCase)
          || SemVersion.PrecedenceComparer.Compare(manifest.Version, spec.Version) != 0) {
        await _userInteraction.PrintWarning(
            $"Manifest of package {spec.Id} version {spec.Version} declares '{manifest.Path}' version "
            + $"{manifest.Version}; using the requested package path and version.");
      }

      manifest = manifest with {
        Path = spec.Id.Path,
        Version = spec.Version,
      };
    }

    PackageManifestVariant variant = manifest.GetVariant(packageArtifact.Spec.Id.Variant);

    // Step 1: Run pre-install scripts.

    if (!ignoreScripts) {
      foreach (string script in variant.Scripts.PreInstall) {
        await _commandRunner.Run(script);
      }
    }

    // Step 2: Place files.

    List<IFileInfo> placedFiles = [.. (await ToothFiles.PlaceAssets(
        _fileSystem,
        _sourceService,
        variant,
        packageArtifact.Source,
        ".")).Select(_fileSystem.FileInfo.New)];

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

    const string root = ".";

    foreach (IFileInfo file in await _workspaceService.GetInstalledPackageFiles(existingPackageSpec)) {
      if (ToothFiles.IsPreserved(
          variant,
          ToothFiles.GetRelativePath(_fileSystem, root, file.FullName))) {
        continue;
      }

      if (!_fileSystem.File.Exists(file.FullName)) {
        await _userInteraction.PrintWarning(
            $"File '{file.FullName}' does not exist, skipping removal.");
      }

      file.Delete();
    }

    // Step 3: Remove the files specified to be removed.

    ToothFiles.RemoveFiles(_fileSystem, variant, root);

    // Step 4: Run post-uninstall scripts.

    if (!ignoreScripts) {
      foreach (string script in variant.Scripts.PostUninstall) {
        await _commandRunner.Run(script);
      }
    }

    // Step 4: Remove package from workspace state.

    await _workspaceService.RemoveInstalledPackage(existingPackageSpec);
  }
}
