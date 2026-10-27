using System.IO.Abstractions;

namespace Lip.Core.Mcmod.Install;

/// The workspace as it will be when the steps planned so far have run, for planning a whole
/// operation before changing anything (spec §7): the file system, overlaid with the files the
/// planned steps write and delete and with the layout the operation establishes first.
///
/// Paths are absolute. Before the layout is made right, an entry of a plugins directory is
/// still there rather than in mods: the view reads mods/&lt;name&gt; from
/// plugins/&lt;name&gt; then, and shows plugins as the link it will become.
public sealed class WorkspaceView {
  private readonly IFileSystem _fs;
  private readonly string? _mods;
  private readonly string? _plugins;
  private readonly bool _pluginsMergesIntoMods;
  private readonly Dictionary<string, bool> _files = new(WorkspaceLayout.PathComparer);
  private readonly Dictionary<string, bool> _directories = new(WorkspaceLayout.PathComparer);

  // The paths of _files and _directories by parent directory.
  private readonly Dictionary<string, HashSet<string>> _changed = new(WorkspaceLayout.PathComparer);

  // The entries of directories as they are now, by physical path; the workspace does not
  // change while an operation is planned.
  private readonly Dictionary<string, List<Entry>> _listings = new(WorkspaceLayout.PathComparer);

  /// The workspace as it is, with the layout the plan establishes (none when null).
  public WorkspaceView(IFileSystem fileSystem, WorkspaceLayout.LayoutPlan? layout = null) {
    _fs = fileSystem;
    if (layout is null) {
      return;
    }

    _mods = Trim(layout.Mods);
    _plugins = Trim(layout.Plugins);
    _pluginsMergesIntoMods = layout.PluginsIsDirectory;
    if (layout.CreateMods || layout.RenamePluginsToMods) {
      SetDirectory(_mods, true);
    }
  }

  /// An entry of a directory: its absolute path, and whether it is a directory or a link.
  public readonly record struct Entry(string FullName, bool IsDirectory, bool IsLink);

  public bool FileExists(string path) {
    path = Trim(path);
    return _files.TryGetValue(path, out bool exists) ? exists : _fs.File.Exists(Physical(path));
  }

  public bool DirectoryExists(string path) {
    path = Trim(path);
    return _directories.TryGetValue(path, out bool exists)
        ? exists
        : _fs.Directory.Exists(Physical(path));
  }

  /// The entries of a directory (none when it does not exist).
  public IEnumerable<Entry> Enumerate(string directory) {
    directory = Trim(directory);
    Dictionary<string, Entry> entries = new(WorkspaceLayout.PathComparer);
    void AddReal(string physical) {
      foreach (Entry entry in Listing(physical)) {
        string path = _fs.Path.Join(directory, _fs.Path.GetFileName(entry.FullName));
        entries[path] = entry with { FullName = path };
      }
    }

    if (!DirectoryExists(directory)) {
      return [];
    }

    AddReal(Physical(directory));
    if (_pluginsMergesIntoMods && Same(directory, _mods!)) {
      AddReal(_plugins!);
    }

    if (_pluginsMergesIntoMods && Same(directory, _fs.Path.GetDirectoryName(_plugins!)!)) {
      // The link plugins becomes, which nothing walks through.
      entries[_plugins!] = new Entry(_plugins!, true, true);
    }

    foreach (string path in _changed.GetValueOrDefault(directory) ?? []) {
      if (_directories.TryGetValue(path, out bool directoryExists)) {
        if (directoryExists) {
          entries.TryAdd(path, new Entry(path, true, false));
        } else {
          entries.Remove(path);
        }
      }

      if (_files.TryGetValue(path, out bool fileExists)) {
        if (fileExists) {
          entries[path] = new Entry(path, false, false);
        } else {
          entries.Remove(path);
        }
      }
    }

    return [.. entries.Values.OrderBy(e => e.FullName, StringComparer.Ordinal)];
  }

  public bool IsEmpty(string directory) => !Enumerate(directory).Any();

  /// A planned write: the file exists, and so do its ancestors.
  public void AddFile(string path) {
    path = Trim(path);
    SetFile(path, true);
    for (string? directory = _fs.Path.GetDirectoryName(path);
        directory is not null && !DirectoryExists(directory);
        directory = _fs.Path.GetDirectoryName(directory)) {
      SetDirectory(directory, true);
    }
  }

  /// A planned deletion of a file.
  public void DeleteFile(string path) => SetFile(Trim(path), false);

  /// A planned deletion of an empty directory.
  public void DeleteDirectory(string path) => SetDirectory(Trim(path), false);

  private void SetFile(string path, bool exists) {
    _files[path] = exists;
    AddChanged(path);
  }

  private void SetDirectory(string path, bool exists) {
    _directories[path] = exists;
    AddChanged(path);
  }

  private void AddChanged(string path) {
    if (_fs.Path.GetDirectoryName(path) is not string parent) {
      return;
    }

    if (!_changed.TryGetValue(parent, out HashSet<string>? children)) {
      children = new(WorkspaceLayout.PathComparer);
      _changed[parent] = children;
    }

    children.Add(path);
  }

  private List<Entry> Listing(string physical) {
    if (!_listings.TryGetValue(physical, out List<Entry>? listing)) {
      listing = !_fs.Directory.Exists(physical) ? [] : [
        .. _fs.DirectoryInfo.New(physical).EnumerateFileSystemInfos().Select(info => new Entry(
            info.FullName,
            info is IDirectoryInfo,
            info.Attributes.HasFlag(FileAttributes.ReparsePoint))),
      ];
      _listings[physical] = listing;
    }

    return listing;
  }

  /// Where a path is now: mods/&lt;name&gt;/... is still plugins/&lt;name&gt;/... while
  /// plugins is a directory whose entries move into mods.
  private string Physical(string path) {
    if (!_pluginsMergesIntoMods || !path.StartsWith(_mods! + _fs.Path.DirectorySeparatorChar,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal)) {
      return path;
    }

    string relative = path[(_mods!.Length + 1)..];
    string first = relative.Split(_fs.Path.DirectorySeparatorChar)[0];
    return _fs.Path.Exists(_fs.Path.Join(_plugins!, first))
        ? _fs.Path.Join(_plugins!, relative)
        : path;
  }

  private string Trim(string path) => _fs.Path.TrimEndingDirectorySeparator(path);

  private static bool Same(string? a, string b) =>
      a is not null && WorkspaceLayout.PathComparer.Equals(a, b);
}
