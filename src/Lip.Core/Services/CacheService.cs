using System.IO.Abstractions;
using Lip.Core.Infrastructure;

namespace Lip.Core.Services;

public interface ICacheService {
  Task Clean();
  Task<IDirectoryInfo> GetOrCreateDirectory(
      string key,
      Func<IDirectoryInfo, Task> factory);
  Task<IFileInfo> GetOrCreateFile(
      string key,
      Func<IFileInfo, Task> factory);

  /// Returns the cached file for the key, or null if it is not cached. Never creates anything.
  Task<IFileInfo?> TryGetFile(string key);
}

public class CacheService(IFileSystem fileSystem, IUserInteraction userInteraction) : ICacheService {
  private readonly IDirectoryInfo _cacheDirectory = fileSystem.DirectoryInfo.New(
      Path.Join(
          Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
          "lip",
          "cache"));
  private readonly IDirectoryInfo _tempDirectory = fileSystem.DirectoryInfo.New(
      Path.Join(
          Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
          "lip",
          "temp"));

  private readonly IFileSystem _fileSystem = fileSystem;
  private readonly IUserInteraction _userInteraction = userInteraction;

  public async Task Clean() {
    if (!_cacheDirectory.Exists) {
      return;
    }

    // Recursively remove read-only attribute from all files and directories before deleting
    void RemoveReadOnlyRecursive(IDirectoryInfo dir) {
      // Remove read-only from current directory
      if (dir.Attributes.HasFlag(System.IO.FileAttributes.ReadOnly)) {
        dir.Attributes &= ~System.IO.FileAttributes.ReadOnly;
      }
      // Remove read-only from all files
      foreach (var file in dir.GetFiles()) {
        if (file.IsReadOnly) {
          file.IsReadOnly = false;
        }
      }
      // Recurse into subdirectories
      foreach (var subDir in dir.GetDirectories()) {
        RemoveReadOnlyRecursive(subDir);
      }
    }
    RemoveReadOnlyRecursive(_cacheDirectory);
    _cacheDirectory.Delete(recursive: true);
  }

  public async Task<IDirectoryInfo> GetOrCreateDirectory(string key, Func<IDirectoryInfo, Task> factory) {
    string safeKey = Uri.EscapeDataString(key);

    IDirectoryInfo cacheDir = _fileSystem.DirectoryInfo.New(Path.Join(_cacheDirectory.FullName, safeKey));

    if (cacheDir.Exists) {
      await _userInteraction.PrintInfo($"Cache hit for key '{key}'.");

      return cacheDir;
    }
    cacheDir.Parent?.Create();

    await _userInteraction.PrintInfo($"Caching directory for key '{key}'...");

    IDirectoryInfo tempDir = _fileSystem.DirectoryInfo.New(Path.Join(_tempDirectory.FullName, Guid.NewGuid().ToString()));
    tempDir.Parent?.Create();

    try {
      await factory(tempDir);

      _fileSystem.Directory.Move(tempDir.FullName, cacheDir.FullName);
    }
    catch (IOException) when (_fileSystem.Directory.Exists(cacheDir.FullName)) {
      // Another process has already created the cache directory, so we can ignore this exception.
    }
    finally {
      if (tempDir.Exists) {
        tempDir.Delete(recursive: true);
      }
    }

    cacheDir.Refresh();

    return cacheDir;
  }

  public async Task<IFileInfo> GetOrCreateFile(string key, Func<IFileInfo, Task> factory) {
    IFileInfo cacheFile = GetCacheFile(key);

    if (cacheFile.Exists) {
      await _userInteraction.PrintInfo($"Cache hit for key '{key}'.");

      return cacheFile;
    }
    cacheFile.Directory?.Create();

    await _userInteraction.PrintInfo($"Caching file for key '{key}'...");

    IFileInfo tempFile = _fileSystem.FileInfo.New(Path.Join(_tempDirectory.FullName, Guid.NewGuid().ToString()));
    tempFile.Directory?.Create();

    try {
      await factory(tempFile);

      _fileSystem.File.Move(tempFile.FullName, cacheFile.FullName);
    }
    catch (IOException) when (_fileSystem.File.Exists(cacheFile.FullName)) {
      // Another process has already created the cache file, so we can ignore this exception.
    }
    finally {
      tempFile.Delete();
    }

    cacheFile.Refresh();

    return cacheFile;
  }

  public Task<IFileInfo?> TryGetFile(string key) {
    IFileInfo cacheFile = GetCacheFile(key);

    return Task.FromResult(cacheFile.Exists ? cacheFile : null);
  }

  private IFileInfo GetCacheFile(string key) {
    string safeKey = Uri.EscapeDataString(key);

    return _fileSystem.FileInfo.New(Path.Join(_cacheDirectory.FullName, safeKey));
  }
}
