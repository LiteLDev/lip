using System.IO.Abstractions;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod;

namespace Lip.Core.Sources;

/// A directory archive (protocol/lip-package.md, "Terms"): the regular files below a directory of
/// the local file system, keyed by their '/'-separated path relative to it, in ordinal order. Links
/// are not followed and special files (FIFOs, sockets, device nodes) are left out; an inaccessible
/// entry fails the listing rather than being left out.
public class DirectorySource : ISource {
  private readonly IFileSystem _fs;
  private readonly string _root;
  private readonly Lazy<List<string>> _keys;

  public DirectorySource(IFileSystem fileSystem, string root) {
    _fs = fileSystem;
    _root = fileSystem.Path.GetFullPath(root);
    _keys = new(ListKeys);
  }

  public IEnumerable<string> Keys => _keys.Value;

  public Task<Stream> OpenRead(string key) {
    if (!_keys.Value.Contains(key)) {
      throw new ArgumentException($"The directory {_root} has no file '{key}'.");
    }

    string path = _fs.Path.Join(_root, key.Replace('/', _fs.Path.DirectorySeparatorChar));
    try {
      return Task.FromResult<Stream>(_fs.File.OpenRead(path));
    }
    catch (Exception ex) when (FileSystemExtensions.IsFileError(ex)) {
      throw FileSystemExtensions.WrapFileError(
          $"Could not read the file {path} of the package directory {_root}.", ex);
    }
  }

  private List<string> ListKeys() {
    EnumerationOptions options = new() {
      IgnoreInaccessible = false,
      AttributesToSkip = FileAttributes.ReparsePoint,
    };
    List<string> keys = [];
    // Outside Windows '\' is not a separator, so a name may contain it; such a directory is not
    // a valid archive (protocol/lip-package.md, "Terms").
    void Check(string path) {
      if (_fs.Path.DirectorySeparatorChar != '\\' && _fs.Path.GetFileName(path).Contains('\\')) {
        throw new ArchiveUnavailableException(
            $"The package directory {_root} is not a valid archive: '{path}' has a '\\' in its"
            + " name, which lip does not install. Rename it.");
      }
    }

    void Walk(string directory) {
      foreach (string file in _fs.Directory.EnumerateFiles(directory, "*", options)
          .Where(f => !UnixFileType.IsSpecialFile(f))) {
        Check(file);
        keys.Add(_fs.Path.GetRelativePath(_root, file).Replace('\\', '/'));
      }

      foreach (string sub in _fs.Directory.EnumerateDirectories(directory, "*", options)) {
        Check(sub);
        Walk(sub);
      }
    }

    try {
      Walk(_root);
    }
    catch (Exception ex) when (FileSystemExtensions.IsFileError(ex)) {
      throw FileSystemExtensions.WrapFileError(
          $"Could not list the files of the package directory {_root}. Check that lip may read"
          + " it and every directory below it.", ex);
    }

    keys.Sort(StringComparer.Ordinal);
    RunTrace.Write("archive", $"Listed {keys.Count} files in the directory {_root}");
    return keys;
  }
}
