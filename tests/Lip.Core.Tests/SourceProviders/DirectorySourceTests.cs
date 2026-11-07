using System.IO.Abstractions;
using System.Net.Sockets;
using Lip.Core.Sources;
using Lip.Core.Tests.Infrastructure;

namespace Lip.Core.Tests.SourceProviders;

/// Directory archives (protocol/package.md §1) on the real file system.
public class DirectorySourceTests : IDisposable {
  private readonly string _root = UnixFileTypeTests.MakeTempRootPath();

  public DirectorySourceTests() {
    Directory.CreateDirectory(_root);
  }

  public void Dispose() {
    if (Directory.Exists(_root)) {
      Directory.Delete(_root, recursive: true);
    }
    GC.SuppressFinalize(this);
  }

  [Fact]
  public void Keys_RegularFiles_AreListedInOrdinalOrder() {
    // Arrange
    Directory.CreateDirectory(Path.Join(_root, "sub"));
    File.WriteAllText(Path.Join(_root, "b.txt"), "b");
    File.WriteAllText(Path.Join(_root, "sub", "a.txt"), "a");

    // Act
    DirectorySource source = new(new FileSystem(), _root);

    // Assert
    Assert.Equal(["b.txt", "sub/a.txt"], source.Keys);
  }

  [Fact]
  public void Keys_FifoAndSocket_AreLeftOut() {
    Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows has no FIFOs or socket files.");

    // Arrange: only regular files are part of a directory archive.
    Directory.CreateDirectory(Path.Join(_root, "sub"));
    File.WriteAllText(Path.Join(_root, "a.txt"), "a");
    UnixFileTypeTests.MakeFifo(Path.Join(_root, "sub", "fifo"));
    using Socket socket = UnixFileTypeTests.MakeSocket(Path.Join(_root, "socket"));

    // Act
    DirectorySource source = new(new FileSystem(), _root);

    // Assert
    Assert.Equal(["a.txt"], source.Keys);
  }
}
