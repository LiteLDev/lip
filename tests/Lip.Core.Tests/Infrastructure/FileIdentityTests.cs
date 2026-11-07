using Lip.Core.Infrastructure;

namespace Lip.Core.Tests.Infrastructure;

public class FileIdentityTests : IDisposable {
  private readonly string _root = UnixFileTypeTests.MakeTempRootPath();

  public FileIdentityTests() {
    Directory.CreateDirectory(_root);
  }

  public void Dispose() {
    if (Directory.Exists(_root)) {
      Directory.Delete(_root, recursive: true);
    }
    GC.SuppressFinalize(this);
  }

  [Fact]
  public void Of_SameFileByAnotherPath_IsEqual() {
    // Arrange
    string file = Path.Join(_root, "a.txt");
    File.WriteAllText(file, "a");

    // Act & Assert
    Assert.NotNull(FileIdentity.Of(file));
    Assert.Equal(FileIdentity.Of(file), FileIdentity.Of(Path.Join(_root, ".", "a.txt")));
  }

  [Fact]
  public void Of_OtherFiles_Differ() {
    // Arrange
    string a = Path.Join(_root, "a.txt");
    string b = Path.Join(_root, "b.txt");
    File.WriteAllText(a, "a");
    File.WriteAllText(b, "a");

    // Act & Assert
    Assert.NotEqual(FileIdentity.Of(a), FileIdentity.Of(b));
  }

  [Fact]
  public void Of_SymbolicLink_IsTheIdentityOfItsTarget() {
    Assert.SkipWhen(OperatingSystem.IsWindows(), "Creating links on Windows needs a privilege.");

    // Arrange
    string file = Path.Join(_root, "a.txt");
    string link = Path.Join(_root, "link");
    File.WriteAllText(file, "a");
    File.CreateSymbolicLink(link, file);

    // Act & Assert
    Assert.Equal(FileIdentity.Of(file), FileIdentity.Of(link));
  }

  [Fact]
  public void Of_Missing_ReturnsNull() {
    // Act & Assert
    Assert.Null(FileIdentity.Of(Path.Join(_root, "missing")));
  }
}
