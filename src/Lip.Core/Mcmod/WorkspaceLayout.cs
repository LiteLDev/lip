using System.IO.Abstractions;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod.Install;
using Lip.Core.Mcmod.Lock;

namespace Lip.Core.Mcmod;

/// Thrown when the workspace layout cannot be made right; the workspace is left unchanged.
public class WorkspaceLayoutException(string message, Exception? inner = null)
    : InvalidOperationException(message, inner);

/// The workspace layout (protocol/lip-workspace.md, "Environment and layout"): mods live in
/// &lt;root&gt;/mods, a directory.
public class WorkspaceLayout(IFileSystem fileSystem, IUserInteraction userInteraction) {
  public const string ModsDirectory = "mods";

  /// How workspace paths are compared: ignoring case on Windows, ordinal elsewhere.
  public static StringComparer PathComparer { get; } =
      OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

  private static readonly StringComparison PathComparison =
      OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

  private readonly IFileSystem _fs = fileSystem;
  private readonly IUserInteraction _ui = userInteraction;

  /// Whether a workspace-relative path is mods itself, which lip never deletes.
  public static bool IsModsDirectory(string path) =>
      string.Equals(path.TrimEnd('/'), ModsDirectory, PathComparison);

  /// Whether a workspace-relative path is .lip or below it, where lip keeps its own files (the
  /// lock): packages never write there, and remove never matches it.
  public static bool IsLipPath(string path) =>
      string.Equals(path.TrimEnd('/'), LockStore.LipDirectory, PathComparison)
      || path.StartsWith(LockStore.LipDirectory + "/", PathComparison);

  /// What making the layout right takes (see Plan). Mods is absolute; LinkTarget is where mods
  /// links to when it is a link.
  public sealed record LayoutPlan(string Mods, bool CreateMods, string? LinkTarget);

  /// Reads what making the layout right takes, without changing anything: mods is created when
  /// missing. Whether the layout is right is checked (Check) only when the operation changes the
  /// workspace.
  public LayoutPlan Plan(string workspaceRoot) {
    string mods = _fs.Path.Join(_fs.Path.GetFullPath(workspaceRoot), ModsDirectory);
    string? target = _fs.DirectoryInfo.New(mods).LinkTarget;
    bool create = target is null && !_fs.Directory.Exists(mods);
    RunTrace.Write("workspace", target is not null
        ? $"{mods} is a link to {target}"
        : create
            ? $"{mods} does not exist; it is created if the operation changes the workspace"
            : $"{mods} exists");
    return new LayoutPlan(mods, create, target);
  }

  /// Checks the layout before an operation changes the workspace: a mods that is a link is an
  /// error. Throws WorkspaceLayoutException.
  public void Check(LayoutPlan plan) {
    // A mods link would let writes under mods/ land outside the workspace.
    if (plan.LinkTarget is { } target) {
      throw new WorkspaceLayoutException(
          $"The mods directory '{plan.Mods}' is a link to '{target}', but lip only installs into a"
          + " real directory, so that it never writes outside the workspace. Replace the link"
          + " with a directory (move the mods into it), then try again.");
    }
  }

  /// Reports what applying the plan would do.
  public async Task ReportDryRun(LayoutPlan plan) {
    if (plan.CreateMods) {
      await _ui.PrintInfo($"Dry run: would create the directory {ModsDirectory}/");
    }
  }

  /// Applies the plan through the transaction, which undoes it when it or a later change fails.
  /// Throws WorkspaceLayoutException.
  public void Apply(LayoutPlan plan, WorkspaceTransaction transaction) {
    if (!plan.CreateMods) {
      return;
    }

    try {
      transaction.CreateDirectory(plan.Mods);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
      // The failure may also be of the journal, written first; its message is the cause.
      bool fileInTheWay = _fs.File.Exists(plan.Mods);
      throw new WorkspaceLayoutException(
          $"Cannot create the mods directory '{plan.Mods}'."
          + (fileInTheWay
              ? " A file named mods is in the way: move or delete it, then try again."
              : ""),
          ex);
    }
  }
}

/// Helpers for messages about files of the workspace.
internal static class WorkspaceErrors {
  /// An exception of the type of a file system exception (IOException or
  /// UnauthorizedAccessException), with a message saying what lip was doing and the exception as
  /// its cause, so that the type-based hints still apply.
  public static Exception Wrap(Exception ex, string message) => ex switch {
    UnauthorizedAccessException => new UnauthorizedAccessException(message, ex),
    IOException => new IOException(message, ex),
    _ => new InvalidOperationException(message, ex),
  };

  /// The message of an exception as a sentence, for messages that cannot keep the exception.
  public static string Reason(Exception ex) {
    string message = ex.Message.Trim();
    return message.Length == 0 || message.EndsWith('.') || message.EndsWith('!')
        || message.EndsWith('?')
            ? message
            : message + ".";
  }

  /// A path as the user knows it: relative to the workspace root and '/'-separated when it is in
  /// the workspace, else as it is.
  public static string Show(IFileSystem fs, string root, string path) {
    string relative = fs.Path.GetRelativePath(root, path);
    return relative == ".." || relative.StartsWith(".." + fs.Path.DirectorySeparatorChar,
        StringComparison.Ordinal) || fs.Path.IsPathRooted(relative)
            ? path
            : relative.Replace('\\', '/');
  }
}
