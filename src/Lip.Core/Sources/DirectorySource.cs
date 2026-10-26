using System.IO.Abstractions;

namespace Lip.Core.Sources;

public class DirectorySource(IDirectoryInfo directoryInfo) : ISource {
  private const string _gitEntryName = ".git";

  private readonly IDirectoryInfo _directoryInfo = directoryInfo;

  // Keys always use '/' as the separator, matching archive sources on every platform.
  public IEnumerable<string> Keys => EnumerateFiles(_directoryInfo)
      .Select(f => ToKey(Path.GetRelativePath(_directoryInfo.FullName, f.FullName)));

  public async Task<Stream> OpenRead(string key) {
    string normalizedKey = key.Replace('\\', '/');

    if (normalizedKey.Split('/').Any(s => s is "" or "." or ".." or _gitEntryName)) {
      throw new ArgumentException($"Key not found: '{key}'", nameof(key));
    }

    IFileInfo fileInfo = _directoryInfo.FileSystem.FileInfo.New(
        Path.Combine([_directoryInfo.FullName, .. normalizedKey.Split('/')]));

    if (!fileInfo.Exists) {
      throw new ArgumentException($"Key not found: '{key}'", nameof(key));
    }

    return fileInfo.OpenRead();
  }

  // Skips .git entries (directories, or files in submodules and worktrees) without descending into them.
  private static IEnumerable<IFileInfo> EnumerateFiles(IDirectoryInfo directory) {
    foreach (IFileInfo file in directory.EnumerateFiles()) {
      if (file.Name != _gitEntryName) {
        yield return file;
      }
    }

    foreach (IDirectoryInfo subdirectory in directory.EnumerateDirectories()) {
      if (subdirectory.Name == _gitEntryName) {
        continue;
      }

      foreach (IFileInfo file in EnumerateFiles(subdirectory)) {
        yield return file;
      }
    }
  }

  private static string ToKey(string relativePath) => relativePath.Replace(Path.DirectorySeparatorChar, '/');
}
