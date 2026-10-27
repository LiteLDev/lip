using Lip.Core.Sources;

namespace Lip.Core.Mcmod.Install;

/// What installing or uninstalling one package does to the workspace, planned and checked before
/// anything changes (spec §7). Name: the package name, for messages. Root: the absolute
/// workspace root, where scripts run. OwnedFiles: for an install, the files the package owns
/// afterwards (workspace-relative, '/'-separated).
public sealed record PackagePlan(
    string Name,
    string Root,
    IReadOnlyList<string> OwnedFiles,
    IReadOnlyList<PlannedStep> Steps) {
  /// The absolute paths the plan writes.
  public IEnumerable<string> WrittenPaths =>
      Steps.OfType<WriteStep>().Select(s => s.FullPath);
}

/// One step of a package plan. Paths are absolute.
public abstract record PlannedStep;

/// Runs the commands of a script hook (e.g. "preInstall") in turn.
public sealed record ScriptStep(string Stage, IReadOnlyList<string> Commands) : PlannedStep;

/// Writes the file of an archive, replacing what is there. Target is workspace-relative.
public sealed record WriteStep(string Target, string FullPath, ISource Source, string Key)
    : PlannedStep;

/// Deletes a file, if it still exists then.
public sealed record DeleteFileStep(string FullPath) : PlannedStep;

/// Deletes a directory when the operation commits, if it is empty then.
public sealed record DeleteDirectoryStep(string FullPath) : PlannedStep;
