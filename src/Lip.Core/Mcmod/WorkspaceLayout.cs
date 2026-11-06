using System.IO.Abstractions;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod.Install;
using Lip.Core.Mcmod.Lock;

namespace Lip.Core.Mcmod;

/// Thrown when the workspace layout cannot be made right; the workspace is left unchanged.
public class WorkspaceLayoutException(string message, Exception? inner = null)
    : InvalidOperationException(message, inner);

/// The workspace layout (protocol/workspace.md §2): mods live in &lt;root&gt;/mods, a directory.
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

  /// What making the layout right takes (see Plan). Mods is absolute.
  public sealed record LayoutPlan(string Mods, bool CreateMods);

  /// Checks the layout before a mutating operation and returns what makes it right, without
  /// changing anything: mods is created when missing; a mods that is a link is an error.
  public LayoutPlan Plan(string workspaceRoot) {
    string mods = _fs.Path.Join(_fs.Path.GetFullPath(workspaceRoot), ModsDirectory);

    // A mods link would let writes under mods/ land outside the workspace.
    if (_fs.DirectoryInfo.New(mods).LinkTarget is not null) {
      throw new WorkspaceLayoutException($"'{mods}' must be a directory, but it is a link.");
    }

    return new LayoutPlan(mods, !_fs.Directory.Exists(mods));
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
      throw new WorkspaceLayoutException($"Cannot create '{plan.Mods}': {ex.Message}", ex);
    }
  }
}
