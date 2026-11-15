using System.IO.Abstractions;
using Lip.Core.Mcmod.Lock;

namespace Lip.Core.Tests.Mcmod.Lock;

/// The server lock (protocol/lip-workspace.md, "Server lock"), on the real file system, whose
/// sharing the lock is made of.
public sealed class ServerLockTests : IDisposable {
  private readonly FileSystem _fs = new();
  private readonly LockStore _lockStore;
  private readonly string _root =
      Directory.CreateTempSubdirectory("lip-server-lock-test-").FullName;

  public ServerLockTests() {
    _lockStore = new LockStore(_fs);
  }

  public void Dispose() => Directory.Delete(_root, recursive: true);

  private ServerLock Acquire() {
    _lockStore.CreateLipDirectory(_root);
    return ServerLock.Acquire(_fs, _lockStore.GetServerLockPath(_root), _root);
  }

  [Fact]
  public void IsHeld_WhileTheLockIsHeld_AndNotAfter() {
    // Arrange
    ServerLock held = Acquire();

    // Act and assert
    Assert.True(ServerLock.IsHeld(_fs, _root));
    held.Dispose();
    Assert.False(ServerLock.IsHeld(_fs, _root));
  }

  [Fact]
  public void IsHeld_WorkspaceWithoutALockFile_IsFalseAndCreatesNothing() {
    // Act and assert
    Assert.False(ServerLock.IsHeld(_fs, _root));
    Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
    Assert.False(ServerLock.IsHeld(_fs, Path.Join(_root, "missing")));
  }

  [Fact]
  public void IsHeld_TakesNoLock_SoTheLockCanBeTakenAfterIt() {
    // Arrange
    Acquire().Dispose();

    // Act
    Assert.False(ServerLock.IsHeld(_fs, _root));

    // Assert
    using ServerLock taken = Acquire();
    Assert.True(ServerLock.IsHeld(_fs, _root));
  }

  [Fact]
  public async Task Acquire_WhileTheLockIsAskedAboutAgainAndAgain_NeverFails() {
    // Arrange: another thread asks whether the lock is held, far more often than the
    // interface, which asks about once a second.
    Acquire().Dispose();
    using CancellationTokenSource stop = new();
    Task asking = Task.Run(() => {
      while (!stop.IsCancellationRequested) {
        ServerLock.IsHeld(_fs, _root);
        Thread.Sleep(1);
      }
    }, TestContext.Current.CancellationToken);

    // Act and assert
    try {
      for (int i = 0; i < 50; i++) {
        Acquire().Dispose();
      }
    }
    finally {
      stop.Cancel();
      await asking;
    }
  }

  [Fact]
  public void Acquire_HeldLock_Throws() {
    // Arrange
    using ServerLock held = Acquire();

    // Act and assert
    Assert.Throws<ServerRunningException>(Acquire);
  }
}
