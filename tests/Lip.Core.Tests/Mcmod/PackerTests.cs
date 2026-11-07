using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.IO.Compression;
using System.Net.Sockets;
using Lip.Core.Mcmod;
using Lip.Core.Tests.Infrastructure;

namespace Lip.Core.Tests.Mcmod;

/// lip pack (protocol/cli.md §5.11).
public class PackerTests {
  private static readonly string _root = OperatingSystem.IsWindows() ? @"C:\build" : "/build";

  [Fact]
  public async Task WriteArchive_PacksEveryFileWithoutAddingAManifest() {
    // Arrange
    MockFileSystem fs = new();
    fs.AddFile(fs.Path.Join(_root, "LegacyMoney.dll"), new MockFileData("dll"));
    fs.AddFile(fs.Path.Join(_root, "lang", "en_US.json"), new MockFileData("{}"));
    string output = fs.Path.Join(_root, "out.mcmod");

    // Act
    await Packer.WriteArchive(fs, _root, output);

    // Assert
    Dictionary<string, string> entries = await Entries(fs, output);
    Assert.Equal(
        ["LegacyMoney.dll", "lang/en_US.json"],
        entries.Keys.Order(StringComparer.Ordinal));
    Assert.Equal("dll", entries["LegacyMoney.dll"]);
    Assert.Equal("{}", entries["lang/en_US.json"]);
  }

  [Fact]
  public async Task WriteArchive_ManifestInTheDirectory_IsPackedAsIs() {
    // Arrange
    MockFileSystem fs = new();
    fs.AddFile(fs.Path.Join(_root, "manifest.json"), new MockFileData("""{"name":"m"}"""));
    string output = fs.Path.Join(_root, "out.zip");

    // Act
    await Packer.WriteArchive(fs, _root, output);

    // Assert
    KeyValuePair<string, string> entry = Assert.Single(await Entries(fs, output));
    Assert.Equal("manifest.json", entry.Key);
    Assert.Equal("""{"name":"m"}""", entry.Value);
  }

  [Fact]
  public async Task WriteArchive_ExcludesOutputAndGitAndLipAtTheRootOnly() {
    // Arrange
    MockFileSystem fs = new();
    fs.AddFile(fs.Path.Join(_root, "a.txt"), new MockFileData("a"));
    fs.AddFile(fs.Path.Join(_root, ".git", "HEAD"), new MockFileData("ref"));
    fs.AddFile(fs.Path.Join(_root, ".lip", "workspace.json"), new MockFileData("{}"));
    fs.AddFile(fs.Path.Join(_root, "sub", ".git", "HEAD"), new MockFileData("nested"));
    fs.AddFile(fs.Path.Join(_root, "sub", ".lip", "x"), new MockFileData("nested"));
    fs.AddFile(fs.Path.Join(_root, ".gitignore"), new MockFileData("*.zip"));
    fs.AddFile(fs.Path.Join(_root, "out.mcmod"), new MockFileData("previous"));
    string output = fs.Path.Join(_root, "out.mcmod");

    // Act
    await Packer.WriteArchive(fs, _root, output);

    // Assert
    Dictionary<string, string> entries = await Entries(fs, output);
    Assert.Equal(
        [".gitignore", "a.txt", "sub/.git/HEAD", "sub/.lip/x"],
        entries.Keys.Order(StringComparer.Ordinal));
  }

  [Fact]
  public async Task WriteArchive_FileNamedLikeATemporaryOutput_IsPackedAndKept() {
    // Arrange: only <FILE> itself is left out (protocol/cli.md §5.11).
    MockFileSystem fs = new();
    string output = fs.Path.Join(_root, "out.mcmod");
    fs.AddFile(output + ".tmp", new MockFileData("mine"));

    // Act
    await Packer.WriteArchive(fs, _root, output);

    // Assert
    KeyValuePair<string, string> entry = Assert.Single(await Entries(fs, output));
    Assert.Equal("out.mcmod.tmp", entry.Key);
    Assert.Equal("mine", entry.Value);
    Assert.Equal("mine", fs.File.ReadAllText(output + ".tmp"));
  }

  [Fact]
  public async Task WriteArchive_OutputOutsideTheDirectory_ReplacesIt() {
    // Arrange
    MockFileSystem fs = new();
    fs.AddFile(fs.Path.Join(_root, "a.txt"), new MockFileData("a"));
    string output = fs.Path.Join(fs.Path.GetDirectoryName(_root)!, "dist", "out.zip");
    fs.AddFile(output, new MockFileData("previous"));

    // Act
    await Packer.WriteArchive(fs, _root, output);

    // Assert
    Assert.Equal(["a.txt"], (await Entries(fs, output)).Keys);
  }

  [Fact]
  public async Task WriteArchive_NotADirectory_Throws() {
    // Arrange
    MockFileSystem fs = new();

    // Act & Assert
    await Assert.ThrowsAsync<InvalidOperationException>(
        () => Packer.WriteArchive(fs, _root, fs.Path.Join(_root, "out.zip")));
  }

  [Fact]
  public async Task WriteArchive_FifoAndSocket_AreLeftOut() {
    Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows has no FIFOs or socket files.");

    // Arrange: only regular files are packed; reading a FIFO would wait for a writer forever.
    FileSystem fs = new();
    string root = Path.Combine(Path.GetTempPath(), "lip-packer-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Join(root, "sub"));
    try {
      File.WriteAllText(Path.Join(root, "a.txt"), "a");
      UnixFileTypeTests.MakeFifo(Path.Join(root, "sub", "fifo"));
      using Socket socket = UnixFileTypeTests.MakeSocket(Path.Join(root, "socket"));
      string output = Path.Join(root, "out.zip");

      // Act
      await Packer.WriteArchive(fs, root, output)
          .WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

      // Assert
      Assert.Equal(["a.txt"], (await Entries(fs, output)).Keys);
    }
    finally {
      Directory.Delete(root, recursive: true);
    }
  }

  private static async Task<Dictionary<string, string>> Entries(IFileSystem fs, string path) {
    using ZipArchive zip = new(new MemoryStream(fs.File.ReadAllBytes(path)));
    Dictionary<string, string> entries = [];
    foreach (ZipArchiveEntry entry in zip.Entries) {
      using StreamReader reader = new(entry.Open());
      entries[entry.FullName] = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
    }

    return entries;
  }
}
