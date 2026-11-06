using System.IO.Abstractions;

namespace Lip.Core.Sources;

/// A directory archive (protocol/package.md §1): the regular files below a directory of the local
/// file system, keyed by their '/'-separated path relative to it, in ordinal order. Links are not
/// followed; an inaccessible entry fails the listing rather than being left out.
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
      throw new ArgumentException($"Key not found: '{key}'", nameof(key));
    }

    return Task.FromResult<Stream>(_fs.File.OpenRead(
        _fs.Path.Join(_root, key.Replace('/', _fs.Path.DirectorySeparatorChar))));
  }

  private List<string> ListKeys() {
    EnumerationOptions options = new() {
      IgnoreInaccessible = false,
      AttributesToSkip = FileAttributes.ReparsePoint,
    };
    List<string> keys = [];
    void Walk(string directory) {
      foreach (string file in _fs.Directory.EnumerateFiles(directory, "*", options)) {
        keys.Add(_fs.Path.GetRelativePath(_root, file).Replace('\\', '/'));
      }

      foreach (string sub in _fs.Directory.EnumerateDirectories(directory, "*", options)) {
        Walk(sub);
      }
    }

    Walk(_root);
    keys.Sort(StringComparer.Ordinal);
    return keys;
  }
}
