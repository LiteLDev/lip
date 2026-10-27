using System.IO.Abstractions;
using Lip.Core.Mcmod.Lock;

namespace Lip.Core.Tests.Mcmod.Lock;

/// On the real file system, where the lock is enforced between handles as between processes.
public class WorkspaceMutexTests : IDisposable {
  private readonly string _directory =
      Path.Combine(Path.GetTempPath(), "lip-mutex-test-" + Guid.NewGuid().ToString("N"));

  private readonly FileSystem _fs = new();

  private string LockPath => Path.Combine(_directory, "workspaces", "hash.lock");

  public void Dispose() {
    if (Directory.Exists(_directory)) {
      Directory.Delete(_directory, recursive: true);
    }

    GC.SuppressFinalize(this);
  }

  [Fact]
  public void Acquire_WhileHeld_ThrowsBusy() {
    // Arrange
    using WorkspaceMutex held = WorkspaceMutex.Acquire(_fs, LockPath, "C:/ws");

    // Act & Assert
    WorkspaceBusyException ex = Assert.Throws<WorkspaceBusyException>(
        () => WorkspaceMutex.Acquire(_fs, LockPath, "C:/ws"));
    Assert.Contains("Another lip process is changing the workspace 'C:/ws'", ex.Message);
  }

  [Fact]
  public void Dispose_Held_ReleasesAndDeletesTheFile() {
    // Arrange
    WorkspaceMutex held = WorkspaceMutex.Acquire(_fs, LockPath, "C:/ws");

    // Act
    held.Dispose();

    // Assert
    Assert.False(File.Exists(LockPath));
    using WorkspaceMutex again = WorkspaceMutex.Acquire(_fs, LockPath, "C:/ws");
    Assert.True(File.Exists(LockPath));
  }

  [Fact]
  public void Acquire_LeftoverFileOfKilledProcess_TakesItOver() {
    // Arrange
    Directory.CreateDirectory(Path.GetDirectoryName(LockPath)!);
    File.WriteAllText(LockPath, "");

    // Act
    using WorkspaceMutex mutex = WorkspaceMutex.Acquire(_fs, LockPath, "C:/ws");

    // Assert
    Assert.True(File.Exists(LockPath));
  }
}
