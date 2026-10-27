using System.IO.Abstractions;
using Lip.Core.Infrastructure;

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

  /// Makes the layout right before a mutating operation, or reports what it would do in a dry
  /// run:
  /// - mods is created when missing;
  /// - a missing plugins becomes a link to mods;
  /// - a plugins that links elsewhere is an error;
  /// - the entries of a plugins directory move into mods, then plugins becomes a link. An entry
  ///   whose name already exists in mods is an error, detected before anything moves.
  /// When a step fails, the earlier steps are undone.
  public async Task Prepare(string workspaceRoot, bool dryRun) {
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

      if (_fs.Directory.Exists(mods)) {
        return;
      }

      if (dryRun) {
        await DryRun($"create the directory {ModsDirectory}/");
      } else {
        _fs.Directory.CreateDirectory(mods);
      }

      return;
    }

    if (_fs.File.Exists(plugins)) {
      throw new WorkspaceLayoutException(
          $"'{plugins}' is a file, but it must be a link to '{mods}'.");
    }

    List<string> entries = _fs.Directory.Exists(plugins)
        ? [.. _fs.Directory.EnumerateFileSystemEntries(plugins)]
        : [];
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

    if (dryRun) {
      if (_fs.Directory.Exists(plugins)) {
        foreach (string entry in entries) {
          string name = _fs.Path.GetFileName(entry);
          await DryRun($"move {PluginsDirectory}/{name} to {ModsDirectory}/{name}");
        }
      } else if (!_fs.Directory.Exists(mods)) {
        await DryRun($"create the directory {ModsDirectory}/");
      }

      await DryRun($"link {PluginsDirectory}/ to {ModsDirectory}/");
      return;
    }

    // What to undo, in reverse order, if a later step fails.
    List<(string Description, Action Undo)> done = [];
    try {
      if (!_fs.Directory.Exists(plugins)) {
        if (!_fs.Directory.Exists(mods)) {
          _fs.Directory.CreateDirectory(mods);
          done.Add(($"delete '{mods}'", () => _fs.Directory.Delete(mods)));
        }
      } else if (!_fs.Directory.Exists(mods)) {
        // Nothing to merge with: one rename moves everything.
        _fs.Directory.Move(plugins, mods);
        done.Add(($"move '{mods}' back to '{plugins}'", () => _fs.Directory.Move(mods, plugins)));
      } else {
        foreach (string entry in entries) {
          string destination = _fs.Path.Join(mods, _fs.Path.GetFileName(entry));
          Move(entry, destination);
          done.Add(($"move '{destination}' back to '{entry}'", () => Move(destination, entry)));
        }

        _fs.Directory.Delete(plugins);
        done.Add(($"create '{plugins}'", () => _fs.Directory.CreateDirectory(plugins)));
      }

      _linker.CreateLink(plugins, mods);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
      List<string> errors = [];
      foreach ((string description, Action undo) in Enumerable.Reverse(done)) {
        try {
          undo();
        }
        catch (Exception rollback) when (
            rollback is IOException or UnauthorizedAccessException) {
          errors.Add($"cannot {description}: {rollback.Message}");
        }
      }

      throw new WorkspaceLayoutException(
          $"Cannot make '{plugins}' a link to '{mods}': {ex.Message}"
          + (errors.Count == 0 ? "" : " Undoing failed: " + string.Join("; ", errors)),
          ex);
    }

    if (entries.Count > 0) {
      await _ui.PrintInfo($"Moved the contents of {PluginsDirectory}/ into {ModsDirectory}/");
    }

    await _ui.PrintInfo($"Linked {PluginsDirectory}/ to {ModsDirectory}/");
  }

  private Task DryRun(string action) => _ui.PrintInfo($"Dry run: would {action}");

  /// Moves a file, a directory or a directory link (the link, not its target).
  private void Move(string from, string to) {
    if (_fs.File.Exists(from)) {
      _fs.File.Move(from, to);
    } else {
      _fs.Directory.Move(from, to);
    }
  }

  /// Junction targets read through some APIs carry the NT prefix "\??\".
  private static string StripNtPrefix(string target) =>
      target.StartsWith(@"\??\", StringComparison.Ordinal) ? target[4..] : target;
}
