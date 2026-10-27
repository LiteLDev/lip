using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;

namespace Lip.Core.Tests.Mcmod;

/// A MockFileSystem whose File.Create and File.Move fail on demand, as when a file is locked by
/// another process or the disk is full.
internal sealed class FaultyFileSystem(MockFileSystem inner) : IFileSystem {
  private readonly FaultyFile _file = new(inner);

  /// Called with ("create", path) or ("move", source) before the call; returning true makes it
  /// throw an IOException.
  public Func<string, string, bool> Fail {
    get => _file.Fail;
    set => _file.Fail = value;
  }

  /// The number of File.Create and File.Move calls so far.
  public int Calls => _file.Calls;

  /// Fails the n-th (1-based) File.Create or File.Move call from now on.
  public void FailNthCall(int n) {
    int target = _file.Calls + n;
    Fail = (_, _) => _file.Calls == target;
  }

  public IFile File => _file;

  public IDirectory Directory => inner.Directory;

  public IFileInfoFactory FileInfo => inner.FileInfo;

  public IFileStreamFactory FileStream => inner.FileStream;

  public IPath Path => inner.Path;

  public IDirectoryInfoFactory DirectoryInfo => inner.DirectoryInfo;

  public IDriveInfoFactory DriveInfo => inner.DriveInfo;

  public IFileSystemWatcherFactory FileSystemWatcher => inner.FileSystemWatcher;

  public IFileVersionInfoFactory FileVersionInfo => inner.FileVersionInfo;

  public IRandomAccess RandomAccess => inner.RandomAccess;

  private sealed class FaultyFile(MockFileSystem fileSystem) : MockFile(fileSystem) {
    public Func<string, string, bool> Fail { get; set; } = (_, _) => false;

    public int Calls { get; private set; }

    public override FileSystemStream Create(string path) {
      Check("create", path);
      return base.Create(path);
    }

    public override void Move(string sourceFileName, string destFileName) {
      Check("move", sourceFileName);
      base.Move(sourceFileName, destFileName);
    }

    private void Check(string operation, string path) {
      Calls++;
      if (Fail(operation, path)) {
        throw new IOException($"injected failure: {operation} {path}");
      }
    }
  }
}
