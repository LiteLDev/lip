using System.IO.Abstractions;
using Lip.Core.Entities;
using Lip.Core.Infrastructure;
using Lip.Core.Services;

namespace Lip.Core.Mcmod.Compat;

/// Installs and uninstalls tooth.json packages by the v1–v3 rules (spec §9): the selected
/// variant's assets are placed relative to the workspace root, overwriting existing files (no
/// §7 ownership checks), and its scripts run in the workspace root.
public class ToothInstaller(
    ICommandRunner commandRunner,
    IFileSystem fileSystem,
    ISourceService sourceService,
    IUserInteraction userInteraction) : IMcmodInstaller {
  private readonly ICommandRunner _commandRunner = commandRunner;
  private readonly IFileSystem _fileSystem = fileSystem;
  private readonly ISourceService _sourceService = sourceService;
  private readonly IUserInteraction _userInteraction = userInteraction;

  public bool CanHandle(string kind) => kind == ToothPackage.KindName;

  public async Task<IReadOnlyList<string>> Install(Candidate candidate, InstallContext context) {
    if (candidate.Package is not ToothPackage package) {
      throw new ArgumentException(
          $"{candidate.Package.Identity} is not a tooth.json package.", nameof(candidate));
    }

    if (context.DryRun) {
      return [];
    }

    string root = _fileSystem.Path.GetFullPath(context.WorkspaceRoot);
    PackageManifestVariant variant = package.Variant;

    if (!context.IgnoreScripts) {
      await RunScripts(variant.Scripts.PreInstall, root);
    }

    List<string> placedFiles = await ToothFiles.PlaceAssets(
        _fileSystem,
        _sourceService,
        variant,
        candidate.Archive.Source,
        root);

    if (!context.IgnoreScripts) {
      await RunScripts(variant.Scripts.Install, root);
      await RunScripts(variant.Scripts.PostInstall, root);
    }

    return [
      .. placedFiles
          .Select(path => ToothFiles.GetRelativePath(_fileSystem, root, path))
          .Distinct(StringComparer.Ordinal),
    ];
  }

  /// The variant is selected again from the locked manifest: the label is the variant of the
  /// last chain hop (the ref that reached the tooth.json archive), mapped by §9.2 for the
  /// workspace environment, and the variants are merged for the environment rid. The environment
  /// comes from the lock (spec §5.1), so this is the variant the package was installed with.
  public async Task Uninstall(LockedPackage package, InstallContext context) {
    if (context.DryRun) {
      return;
    }

    PackageManifest manifest;
    using (MemoryStream stream = new(System.Text.Encoding.UTF8.GetBytes(
        package.Manifest.GetRawText()))) {
      manifest = await PackageManifest.FromStream(stream);
    }

    string refVariant = package.Chain.Count > 0 ? Ref.Parse(package.Chain[^1].Ref).Variant : "";

    PackageManifestVariant variant = ToothCompat.Select(manifest, refVariant, context.Environment)
        ?.Variant
        ?? throw new InvalidOperationException(
            $"Cannot uninstall {package.Identity}@{package.Version}: its tooth.json has no "
            + $"variant '{refVariant}' for {context.Environment.Rid}.");

    string root = _fileSystem.Path.GetFullPath(context.WorkspaceRoot);

    if (!context.IgnoreScripts) {
      await RunScripts(variant.Scripts.PreUninstall, root);
      await RunScripts(variant.Scripts.Uninstall, root);
    }

    foreach (string file in package.Files) {
      if (ToothFiles.IsPreserved(variant, file)) {
        continue;
      }

      string fullPath = _fileSystem.Path.GetFullPath(_fileSystem.Path.Combine(root, file));

      if (!_fileSystem.File.Exists(fullPath)) {
        await _userInteraction.PrintWarning(
            $"File '{fullPath}' does not exist, skipping removal.");
        continue;
      }

      _fileSystem.File.Delete(fullPath);
    }

    ToothFiles.RemoveFiles(_fileSystem, variant, root);

    if (!context.IgnoreScripts) {
      await RunScripts(variant.Scripts.PostUninstall, root);
    }
  }

  private async Task RunScripts(IEnumerable<string> scripts, string workingDirectory) {
    foreach (string script in scripts) {
      await _commandRunner.Run(script, workingDirectory);
    }
  }
}
