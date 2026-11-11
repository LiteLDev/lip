using System.IO.Abstractions;
using Lip.Core.Infrastructure;

namespace Lip.Core.Services;

public interface ICacheService {
  Task Clean();
  Task<IFileInfo> GetOrCreateFile(
      string key,
      Func<IFileInfo, Task> factory);

  /// Returns the cached file for the key, or null if it is not cached. Never creates anything.
  Task<IFileInfo?> TryGetFile(string key);
}

/// The lip cache (protocol/lip-userspace.md, "Cache"). Failures of the file system are IOExceptions
/// or UnauthorizedAccessExceptions naming the cache path; failures of a factory are passed on as
/// they are.
public class CacheService(IFileSystem fileSystem, IUserInteraction userInteraction) : ICacheService {
  private readonly IDirectoryInfo _cacheDirectory =
      fileSystem.DirectoryInfo.New(LipDirectories.Cache);

  private readonly IFileSystem _fileSystem = fileSystem;
  private readonly IUserInteraction _userInteraction = userInteraction;

  public async Task Clean() {
    _cacheDirectory.Refresh();
    if (!_cacheDirectory.Exists) {
      RunTrace.Write("cache", $"The cache {_cacheDirectory.FullName} does not exist: nothing to clean");
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

    RunTrace.Write("cache", $"Deleting the cache {_cacheDirectory.FullName}");
    try {
      RemoveReadOnlyRecursive(_cacheDirectory);
      _cacheDirectory.Delete(recursive: true);
    }
    catch (Exception ex) when (FileSystemExtensions.IsFileError(ex)) {
      throw FileSystemExtensions.WrapFileError(
          $"Could not delete the lip cache {_cacheDirectory.FullName}. Stop other lip processes"
          + " and programs that use files in it, then try again, or delete the directory"
          + " yourself.", ex);
    }
  }

  /// Writes a missing entry atomically (protocol/lip-userspace.md, "Cache"): the factory writes a
  /// uniquely named &lt;N&gt;.tmp file in the cache directory, which is then renamed to the entry.
  public async Task<IFileInfo> GetOrCreateFile(string key, Func<IFileInfo, Task> factory) {
    IFileInfo cacheFile = GetCacheFile(key);

    if (cacheFile.Exists) {
      RunTrace.Write("cache", $"Hit for {key}: {cacheFile.FullName}");
      await _userInteraction.PrintInfo($"Cache hit for key '{key}'.");

      return cacheFile;
    }

    RunTrace.Write("cache", $"Miss for {key}: creating {cacheFile.FullName}");
    await _userInteraction.PrintInfo($"Caching file for key '{key}'...");
    try {
      cacheFile.Directory?.Create();
    }
    catch (Exception ex) when (FileSystemExtensions.IsFileError(ex)) {
      throw FileSystemExtensions.WrapFileError(
          $"Could not create the lip cache directory {cacheFile.DirectoryName}. Check that you"
          + " may write there.", ex);
    }

    IFileInfo tempFile =
        _fileSystem.FileInfo.New(Path.Join(_cacheDirectory.FullName, $"{Guid.NewGuid():N}.tmp"));

    try {
      await factory(tempFile);

      try {
        _fileSystem.File.Move(tempFile.FullName, cacheFile.FullName);
      }
      catch (Exception ex) when (FileSystemExtensions.IsFileError(ex)
          && !_fileSystem.File.Exists(cacheFile.FullName)) {
        throw FileSystemExtensions.WrapFileError(
            $"Could not move {tempFile.FullName} into the lip cache as {cacheFile.FullName}."
            + " Check that you may write to the cache and that the disk is not full.", ex);
      }
    }
    catch (IOException) when (_fileSystem.File.Exists(cacheFile.FullName)) {
      // Another process has already created the cache file, so we can ignore this exception.
      RunTrace.Write("cache", $"{cacheFile.FullName} was created meanwhile by another process");
    }
    finally {
      try {
        tempFile.Delete();
      }
      catch (Exception ex) when (FileSystemExtensions.IsFileError(ex)) {
        // A leftover temporary file does no harm; the failure that brought us here matters more.
        RunTrace.Write("cache", $"Could not delete {tempFile.FullName}: {ex.Message}");
      }
    }

    cacheFile.Refresh();
    RunTrace.Write("cache", $"Stored {key}: {cacheFile.FullName}");

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
