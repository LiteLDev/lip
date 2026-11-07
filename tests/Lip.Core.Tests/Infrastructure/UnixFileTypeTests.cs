using System.Diagnostics;
using System.Net.Sockets;
using Lip.Core.Infrastructure;

namespace Lip.Core.Tests.Infrastructure;

public class UnixFileTypeTests : IDisposable {
  private readonly string _root =
      Path.Combine(Path.GetTempPath(), "lip-unix-file-type-test-" + Guid.NewGuid().ToString("N"));

  public UnixFileTypeTests() {
    Directory.CreateDirectory(_root);
  }

  public void Dispose() {
    if (Directory.Exists(_root)) {
      Directory.Delete(_root, recursive: true);
    }
    GC.SuppressFinalize(this);
  }

  /// Creates a FIFO (named pipe) with mkfifo.
  internal static void MakeFifo(string path) {
    using Process process = Process.Start(new ProcessStartInfo("mkfifo", [path]))!;
    process.WaitForExit();
    Assert.Equal(0, process.ExitCode);
  }

  /// Creates a Unix domain socket file, which exists until the socket returned is disposed.
  internal static Socket MakeSocket(string path) {
    Socket socket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
    socket.Bind(new UnixDomainSocketEndPoint(path));
    Assert.True(File.Exists(path));
    return socket;
  }

  [Fact]
  public void IsSpecialFile_RegularFileDirectoryOrMissing_ReturnsFalse() {
    // Arrange
    string file = Path.Join(_root, "a.txt");
    File.WriteAllText(file, "a");

    // Act & Assert
    Assert.False(UnixFileType.IsSpecialFile(file));
    Assert.False(UnixFileType.IsSpecialFile(_root));
    Assert.False(UnixFileType.IsSpecialFile(Path.Join(_root, "missing")));
  }

  [Fact]
  public void IsSpecialFile_FifoOrSocket_ReturnsTrue() {
    Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows has no FIFOs or socket files.");

    // Arrange
    string fifo = Path.Join(_root, "fifo");
    string socket = Path.Join(_root, "socket");
    MakeFifo(fifo);
    using Socket bound = MakeSocket(socket);

    // Act & Assert
    Assert.True(UnixFileType.IsSpecialFile(fifo));
    Assert.True(UnixFileType.IsSpecialFile(socket));
  }

  [Fact]
  public void IsSpecialFile_SymbolicLink_ReturnsFalse() {
    Assert.SkipWhen(OperatingSystem.IsWindows(), "Creating links on Windows needs a privilege.");

    // Arrange: a link to a FIFO is a link, not followed.
    string fifo = Path.Join(_root, "fifo");
    string link = Path.Join(_root, "link");
    MakeFifo(fifo);
    File.CreateSymbolicLink(link, fifo);

    // Act & Assert
    Assert.False(UnixFileType.IsSpecialFile(link));
  }
}
