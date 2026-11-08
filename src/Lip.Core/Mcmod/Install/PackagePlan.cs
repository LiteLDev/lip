using Lip.Core.Sources;

namespace Lip.Core.Mcmod.Install;

/// What installing or uninstalling one package does to the workspace, planned and checked before
/// anything changes (protocol/workspace.md §4). Name: the package name, for messages. Root: the absolute
/// workspace root, where scripts run. OwnedFiles: for an install, the files the package owns
/// afterwards (workspace-relative, '/'-separated) when the workspace is as planned; hook commands
/// may change which files matching preserve are kept, and McmodInstaller.Apply returns the files
/// it owns as it happened.
public sealed record PackagePlan(
    string Name,
    string Root,
    IReadOnlyList<string> OwnedFiles,
    IReadOnlyList<PlannedStep> Steps) {
  /// The absolute paths the plan writes.
  public IEnumerable<string> WrittenPaths =>
      Steps.OfType<WriteStep>().Select(s => s.FullPath);

  /// Whether the plan runs a hook command.
  public bool RunsHooks => Steps.OfType<ScriptStep>().Any(s => s.Commands.Count > 0);

  /// Whether the plan may change the workspace (protocol/workspace.md §1): it writes a file,
  /// deletes a file or a directory, or, when hook commands of the operation run
  /// (hooksRun), deletes what they leave matching when it runs (§6 rule 4); without them the
  /// workspace stays as planned. Hook commands alone are no change.
  public bool MayChange(bool hooksRun) => Steps.Any(s => s switch {
    WriteStep or DeleteFileStep or DeleteDirectoryStep => true,
    DeleteMatchesStep m => hooksRun && (m.OwnedFiles.Count > 0 || m.Purge.Count > 0),
    _ => false,
  });
}

/// One step of a package plan. Paths are absolute.
public abstract record PlannedStep;

/// Runs the commands of a script hook (e.g. "preInstall") in turn.
public sealed record ScriptStep(string Stage, IReadOnlyList<string> Commands) : PlannedStep;

/// Writes the file of an archive, replacing what is there. Target is workspace-relative.
/// Preserve: the globs of preserve of the package; a file there when the step runs that matches
/// one is kept instead.
public sealed record WriteStep(
    string Target,
    string FullPath,
    ISource Source,
    string Key,
    IReadOnlyList<PathGlob> Preserve) : PlannedStep;

/// Deletes a file, if it still exists then.
public sealed record DeleteFileStep(string FullPath) : PlannedStep;

/// Deletes a directory when the operation commits, if it is empty then.
public sealed record DeleteDirectoryStep(string FullPath) : PlannedStep;

/// Deletes what uninstallation steps 2–4 match when it runs but the plan did not foresee, such as
/// files that hook commands created (protocol/workspace.md §6 rule 4): the owned files not
/// matching preserve, and the paths matching purge, except what another package owns then.
/// Paths are relative.
public sealed record DeleteMatchesStep(
    IReadOnlyList<string> OwnedFiles, IReadOnlyList<PathGlob> Purge) : PlannedStep;
