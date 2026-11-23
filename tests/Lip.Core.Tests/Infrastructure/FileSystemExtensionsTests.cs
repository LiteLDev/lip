using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Lip.Core.Infrastructure;

namespace Lip.Core.Tests.Infrastructure;

public class FileSystemExtensionsTests {
  [Fact]
  public void CreateFileWithDirectory_DirectoryExists_CreatesFile() {
    // Arrange
    string root = Path.GetPathRoot(Environment.CurrentDirectory) ?? "/";
    string existingFile = Path.Combine(root, "existing", "placeholder.txt");
    MockFileSystem mockFileSystem = new(new Dictionary<string, MockFileData>
    {
            { existingFile, new MockFileData("") }
        });

    // Act
    using FileSystemStream stream = mockFileSystem.CreateFileWithDirectory(Path.Combine(root, "existing", "newfile.txt"));

    // Assert
    Assert.True(mockFileSystem.File.Exists(Path.Combine(root, "existing", "newfile.txt")));
  }

  [Fact]
  public void CreateFileWithDirectory_DirectoryNotExists_CreatesDirectoryAndFile() {
    // Arrange
    string root = Path.GetPathRoot(Environment.CurrentDirectory) ?? "/";
    string placeholder = Path.Combine(root, "root", "placeholder.txt");
    MockFileSystem mockFileSystem = new(new Dictionary<string, MockFileData>
    {
            { placeholder, new MockFileData("") }
        });

    // Act
    string newFile = Path.Combine(root, "root", "newdir", "newfile.txt");
    using FileSystemStream stream = mockFileSystem.CreateFileWithDirectory(newFile);

    // Assert
    Assert.True(mockFileSystem.Directory.Exists(Path.Combine(root, "root", "newdir")));
    Assert.True(mockFileSystem.File.Exists(newFile));
  }

  [Fact]
  public void WithTrailingSeparator_AddsOneUnlessTheDirectoryEndsInOne() {
    // Arrange
    MockFileSystem fs = new();
    string sep = fs.Path.DirectorySeparatorChar.ToString();
    string root = OperatingSystem.IsWindows() ? @"C:\" : "/";

    // Act & Assert: a root ends in a separator already, so the paths below it start with it.
    Assert.Equal(root, fs.Path.WithTrailingSeparator(root));
    Assert.Equal(root + "ws" + sep, fs.Path.WithTrailingSeparator(root + "ws"));
    Assert.Equal(root + "ws" + sep, fs.Path.WithTrailingSeparator(root + "ws" + sep));
    if (OperatingSystem.IsWindows()) {
      Assert.Equal(@"D:\", fs.Path.WithTrailingSeparator(@"D:\"));
      Assert.Equal(@"C:\ws\", fs.Path.WithTrailingSeparator(@"C:\ws/"));
    }
  }
}
