using System.IO.Abstractions;

namespace Lip.Core.Mcmod.Install;

/// The workspace as it will be when the steps planned so far have run, for planning a whole
/// operation before changing anything (protocol/lip-workspace.md, "Atomicity"): the file system,
/// overlaid with the files the planned steps write and delete and with the layout the operation
/// establishes first.
///
/// Paths are absolute, and compared as the paths of the workspace are (WorkspacePaths). When
/// paths are case-insensitive, the paths asked about are spelled as the existing files are
/// (McmodInstaller spells them so), and a planned path stands for every spelling equal to it.
public sealed class WorkspaceView {
  private readonly IFileSystem _fs;
  private readonly StringComparer _comparer;
  private readonly Dictionary<string, bool> _files;
  private readonly Dictionary<string, bool> _directories;

  // The paths of _files and _directories by parent directory.
  private readonly Dictionary<string, HashSet<string>> _changed;

  // The entries of directories as they are now; the workspace does not change while an
  // operation is planned. Keyed by the names the file system has, so that two directories whose
  // paths equal each other ignoring case each have their own entries.
  private readonly Dictionary<string, List<Entry>> _listings;

  /// The workspace as it is, with the layout the plan establishes (none when null), its paths
  /// compared by the rules given (the host's when null).
  public WorkspaceView(
      IFileSystem fileSystem,
      WorkspaceLayout.LayoutPlan? layout = null,
      WorkspacePaths? paths = null) {
    _fs = fileSystem;
    _comparer = (paths ?? WorkspacePaths.Host).Comparer;
    _files = new(_comparer);
    _directories = new(_comparer);
    _changed = new(_comparer);
    _listings = new(StringComparer.Ordinal);
    if (layout is { CreateMods: true }) {
      SetDirectory(Trim(layout.Mods), true);
    }
  }

  /// An entry of a directory: its absolute path, and whether it is a directory or a link.
  public readonly record struct Entry(string FullName, bool IsDirectory, bool IsLink);

  public bool FileExists(string path) {
    path = Trim(path);
    return _files.TryGetValue(path, out bool exists) ? exists : _fs.File.Exists(path);
  }

  public bool DirectoryExists(string path) {
    path = Trim(path);
    return _directories.TryGetValue(path, out bool exists)
        ? exists
        : _fs.Directory.Exists(path);
  }

  /// The entries of a directory (none when it does not exist).
  public IEnumerable<Entry> Enumerate(string directory) {
    directory = Trim(directory);
    if (!DirectoryExists(directory)) {
      return [];
    }

    // By the names the file system has, which may equal each other when paths are
    // case-insensitive; a planned change replaces every entry its path equals.
    Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    Dictionary<string, List<string>> equal = new(_comparer);
    void Add(string path, Entry entry) {
      entries[path] = entry;
      if (!equal.TryGetValue(path, out List<string>? names)) {
        names = [];
        equal[path] = names;
      }
      names.Add(path);
    }

    void RemoveEqual(string path) {
      if (equal.Remove(path, out List<string>? names)) {
        foreach (string name in names) {
          entries.Remove(name);
        }
      }
    }

    foreach (Entry entry in Listing(directory)) {
      string path = _fs.Path.Join(directory, _fs.Path.GetFileName(entry.FullName));
      Add(path, entry with { FullName = path });
    }

    foreach (string path in _changed.GetValueOrDefault(directory) ?? []) {
      if (_directories.TryGetValue(path, out bool directoryExists)) {
        if (!directoryExists) {
          RemoveEqual(path);
        } else if (!equal.ContainsKey(path)) {
          Add(path, new Entry(path, true, false));
        }
      }

      if (_files.TryGetValue(path, out bool fileExists)) {
        RemoveEqual(path);
        if (fileExists) {
          Add(path, new Entry(path, false, false));
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
      children = new(_comparer);
      _changed[parent] = children;
    }

    children.Add(path);
  }

  private List<Entry> Listing(string directory) {
    if (!_listings.TryGetValue(directory, out List<Entry>? listing)) {
      try {
        listing = !_fs.Directory.Exists(directory) ? [] : [
          .. _fs.DirectoryInfo.New(directory).EnumerateFileSystemInfos().Select(info => new Entry(
              info.FullName,
              info is IDirectoryInfo,
              info.Attributes.HasFlag(FileAttributes.ReparsePoint))),
        ];
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
        throw WorkspaceErrors.Wrap(
            ex,
            $"Cannot list the directory '{directory}' to find the files to delete. Check that you"
            + " may read it.");
      }

      _listings[directory] = listing;
    }

    return listing;
  }

  private string Trim(string path) => _fs.Path.TrimEndingDirectorySeparator(path);

}
