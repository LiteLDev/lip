using System.IO.Abstractions;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod.Install;

namespace Lip.Core.Mcmod;

/// Thrown when the workspace layout cannot be made right; the workspace is left unchanged.
public class WorkspaceLayoutException(string message, Exception? inner = null)
    : InvalidOperationException(message, inner);

/// The server workspace layout (spec §7): mods live in &lt;root&gt;/mods, and &lt;root&gt;/plugins is
/// a link to mods, so that software looking in plugins keeps working.
///
/// Workspace-relative paths are canonical when they go through mods: a path under plugins is the
/// same file as the path under mods, and lip records and compares it as the latter.
public class WorkspaceLayout(
    IFileSystem fileSystem,
    IDirectoryLinker directoryLinker,
    IUserInteraction userInteraction) {
  public const string ModsDirectory = "mods";
  public const string PluginsDirectory = "plugins";

  /// How workspace paths are compared: ignoring case on Windows, ordinal elsewhere.
  public static StringComparer PathComparer { get; } =
      OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

  private static readonly StringComparison PathComparison =
      OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

  private readonly IFileSystem _fs = fileSystem;
  private readonly IDirectoryLinker _linker = directoryLinker;
  private readonly IUserInteraction _ui = userInteraction;

  /// The canonical form of a workspace-relative '/'-separated path: "plugins" and "plugins/..."
  /// become "mods" and "mods/..." (ignoring case on Windows); other paths are unchanged.
  public static string CanonicalPath(string path) {
    if (string.Equals(path, PluginsDirectory, PathComparison)) {
      return ModsDirectory;
    }

    return path.StartsWith(PluginsDirectory + "/", PathComparison)
        ? ModsDirectory + path[PluginsDirectory.Length..]
        : path;
  }

  /// The forms of a canonical workspace-relative path that globs relative to the workspace root
  /// are matched against: the path itself and, under mods, the same path through plugins.
  public static IEnumerable<string> GlobForms(string path) {
    yield return path;
    if (path.StartsWith(ModsDirectory + "/", PathComparison)) {
      yield return PluginsDirectory + path[ModsDirectory.Length..];
    }
  }

  /// Whether a workspace-relative path is mods or plugins itself, which lip never deletes.
  public static bool IsLayoutDirectory(string path) =>
      string.Equals(CanonicalPath(path.TrimEnd('/')), ModsDirectory, PathComparison);

  /// What making the layout right takes (see Plan). Paths are absolute.
  public sealed record LayoutPlan(
      string Mods,
      string Plugins,
      bool CreateMods,
      bool RenamePluginsToMods,
      IReadOnlyList<string> MovedEntries,
      bool DeletePlugins,
      bool LinkPlugins) {
    /// Whether plugins is a directory now, whose entries end up in mods.
    public bool PluginsIsDirectory => RenamePluginsToMods || DeletePlugins;
  }

  /// Checks the layout before a mutating operation and returns what makes it right, without
  /// changing anything:
  /// - mods is created when missing;
  /// - a missing plugins becomes a link to mods;
  /// - a plugins that links elsewhere is an error;
  /// - the entries of a plugins directory move into mods, then plugins becomes a link. An entry
  ///   whose name already exists in mods is an error.
  public LayoutPlan Plan(string workspaceRoot) {
    string root = _fs.Path.GetFullPath(workspaceRoot);
    string mods = _fs.Path.Join(root, ModsDirectory);
    string plugins = _fs.Path.Join(root, PluginsDirectory);

    if (_fs.File.Exists(mods) || _linker.GetLinkTarget(mods) is not null) {
      throw new WorkspaceLayoutException(
          $"'{mods}' must be a directory, but it is a file or a link.");
    }

    if (_linker.GetLinkTarget(plugins) is string target) {
      string resolved = _fs.Path.GetFullPath(_fs.Path.Combine(root, StripNtPrefix(target)));
      if (!string.Equals(
          _fs.Path.TrimEndingDirectorySeparator(resolved), mods, PathComparison)) {
        throw new WorkspaceLayoutException(
            $"'{plugins}' links to '{target}', but it must link to '{mods}'. Remove or fix the"
            + " link, then try again.");
      }

      return new LayoutPlan(mods, plugins, !_fs.Directory.Exists(mods), false, [], false, false);
    }

    if (_fs.File.Exists(plugins)) {
      throw new WorkspaceLayoutException(
          $"'{plugins}' is a file, but it must be a link to '{mods}'.");
    }

    if (!_fs.Directory.Exists(plugins)) {
      return new LayoutPlan(mods, plugins, !_fs.Directory.Exists(mods), false, [], false, true);
    }

    if (!_fs.Directory.Exists(mods)) {
      // Nothing to merge with: one rename moves everything.
      return new LayoutPlan(mods, plugins, false, true, [], false, true);
    }

    List<string> entries = [.. _fs.Directory.EnumerateFileSystemEntries(plugins)];
    List<string> taken = [
      .. entries
          .Select(e => _fs.Path.GetFileName(e))
          .Where(name => _fs.Path.Exists(_fs.Path.Join(mods, name))
              || _linker.GetLinkTarget(_fs.Path.Join(mods, name)) is not null),
    ];
    if (taken.Count > 0) {
      throw new WorkspaceLayoutException(
          $"Cannot move the contents of '{plugins}' into '{mods}': "
          + string.Join(", ", taken.Select(n => $"'{n}'"))
          + $" already exist{(taken.Count == 1 ? "s" : "")} in both. Merge or remove them, then"
          + " try again.");
    }

    return new LayoutPlan(mods, plugins, false, false, entries, true, true);
  }

  /// Reports what applying the plan would do.
  public async Task ReportDryRun(LayoutPlan plan) {
    if (plan.CreateMods) {
      await DryRun($"create the directory {ModsDirectory}/");
    }

    foreach (string entry in plan.RenamePluginsToMods
        ? _fs.Directory.EnumerateFileSystemEntries(plan.Plugins)
        : plan.MovedEntries) {
      string name = _fs.Path.GetFileName(entry);
      await DryRun($"move {PluginsDirectory}/{name} to {ModsDirectory}/{name}");
    }

    if (plan.LinkPlugins) {
      await DryRun($"link {PluginsDirectory}/ to {ModsDirectory}/");
    }
  }

  /// Applies the plan through the transaction, which undoes it when it or a later change fails.
  /// Throws WorkspaceLayoutException.
  public async Task Apply(LayoutPlan plan, WorkspaceTransaction transaction) {
    try {
      if (plan.CreateMods) {
        transaction.CreateDirectory(plan.Mods);
      }

      if (plan.RenamePluginsToMods) {
        transaction.Move(plan.Plugins, plan.Mods);
      }

      foreach (string entry in plan.MovedEntries) {
        transaction.Move(entry, _fs.Path.Join(plan.Mods, _fs.Path.GetFileName(entry)));
      }

      if (plan.DeletePlugins) {
        transaction.DeleteEmptyDirectory(plan.Plugins);
      }

      if (plan.LinkPlugins) {
        transaction.CreateLink(plan.Plugins, plan.Mods, _linker);
      }
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
      throw new WorkspaceLayoutException(
          $"Cannot make '{plan.Plugins}' a link to '{plan.Mods}': {ex.Message}", ex);
    }

    if (plan.RenamePluginsToMods || plan.MovedEntries.Count > 0) {
      await _ui.PrintInfo($"Moved the contents of {PluginsDirectory}/ into {ModsDirectory}/");
    }

    if (plan.LinkPlugins) {
      await _ui.PrintInfo($"Linked {PluginsDirectory}/ to {ModsDirectory}/");
    }
  }

  /// Makes the layout right on its own (see Plan), or reports what it would do in a dry run.
  /// When a step fails, the earlier steps are undone.
  public async Task Prepare(string workspaceRoot, bool dryRun) {
    LayoutPlan plan = Plan(workspaceRoot);
    if (dryRun) {
      await ReportDryRun(plan);
      return;
    }

    using WorkspaceTransaction transaction = new(_fs);
    try {
      await Apply(plan, transaction);
    }
    catch (WorkspaceLayoutException ex) {
      IReadOnlyList<string> errors = transaction.Rollback();
      if (errors.Count == 0) {
        throw;
      }

      throw new WorkspaceLayoutException(
          ex.Message + " Undoing failed: " + string.Join("; ", errors), ex.InnerException);
    }

    transaction.Commit();
    transaction.Complete();
  }

  private Task DryRun(string action) => _ui.PrintInfo($"Dry run: would {action}");

  /// Junction targets read through some APIs carry the NT prefix "\??\".
  private static string StripNtPrefix(string target) =>
      target.StartsWith(@"\??\", StringComparison.Ordinal) ? target[4..] : target;
}
