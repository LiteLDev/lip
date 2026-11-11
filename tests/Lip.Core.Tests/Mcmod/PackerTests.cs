using System.Diagnostics;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.IO.Compression;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Lip.Core.Mcmod;
using Lip.Core.Tests.Infrastructure;

namespace Lip.Core.Tests.Mcmod;

/// lip pack (protocol/lip-pack.md).
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
    // Arrange: only <FILE> itself is left out (protocol/lip-pack.md).
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
    string root = UnixFileTypeTests.MakeTempRootPath();
    Directory.CreateDirectory(Path.Join(root, "sub"));
    try {
      File.WriteAllText(Path.Join(root, "a.txt"), "a");
      UnixFileTypeTests.MakeFifo(Path.Join(root, "sub", "fifo"));
      using Socket socket = UnixFileTypeTests.MakeSocket(Path.Join(root, "socket"));
      string output = Path.Join(root, "out.zip");

      // Act: run on the thread pool so that the timeout also catches a synchronous block.
      await Task.Run(
              () => Packer.WriteArchive(fs, root, output), TestContext.Current.CancellationToken)
          .WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

      // Assert
      Assert.Equal(["a.txt"], (await Entries(fs, output)).Keys);
    }
    finally {
      Directory.Delete(root, recursive: true);
    }
  }

  [Theory]
  [InlineData(@"a\b.txt")]
  [InlineData(@"d\e/f.txt")]
  public async Task WriteArchive_NameWithBackslash_ThrowsWithoutWriting(string path) {
    Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows names cannot contain '\\'.");

    // Arrange: a ZIP entry cannot contain '\' (protocol/lip-package.md, "Terms"), so the file
    // cannot be packed at its own path (protocol/lip-pack.md).
    FileSystem fs = new();
    string root = UnixFileTypeTests.MakeTempRootPath();
    string full = Path.Join(root, path);
    Directory.CreateDirectory(Path.GetDirectoryName(full)!);
    try {
      File.WriteAllText(full, "x");
      string output = Path.Join(root, "out.zip");
      File.WriteAllText(output, "previous");

      // Act & Assert: nothing is written, not even the truncation of <FILE>.
      InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
          () => Packer.WriteArchive(fs, root, output));
      Assert.Contains(full, ex.Message);
      Assert.Equal("previous", File.ReadAllText(output));
    }
    finally {
      Directory.Delete(root, recursive: true);
    }
  }

  [Fact]
  public async Task WriteArchive_OutputThroughALinkToTheDirectory_IsLeftOut() {
    // Arrange: <FILE> is never part of the archive (protocol/lip-pack.md), whatever path
    // reaches it; here a symbolic link (a junction on Windows) to <DIR>.
    FileSystem fs = new();
    string temp = UnixFileTypeTests.MakeTempRootPath();
    string root = Path.Join(temp, "pkg");
    string link = Path.Join(temp, "link");
    Directory.CreateDirectory(root);
    try {
      File.WriteAllText(Path.Join(root, "a.txt"), "a");
      File.WriteAllText(Path.Join(root, "out.zip"), "previous");
      Assert.SkipUnless(LinkDirectory(link, root), "Directory links cannot be created here.");

      // Act
      await Packer.WriteArchive(fs, root, Path.Join(link, "out.zip"));

      // Assert
      Assert.Equal(["a.txt"], (await Entries(fs, Path.Join(root, "out.zip"))).Keys);
    }
    finally {
      if (Directory.Exists(link)) {
        Directory.Delete(link);
      }
      Directory.Delete(temp, recursive: true);
    }
  }

  [Fact]
  public async Task WriteArchive_GitAndLipInAnotherCase_AreLeftOutWhereTheyAreThem() {
    // Arrange: on a case-insensitive file system (e.g. macOS, Windows) .GIT and .Lip are .git and
    // .lip; on a case-sensitive one they are other directories, which are packed.
    FileSystem fs = new();
    string root = UnixFileTypeTests.MakeTempRootPath();
    Directory.CreateDirectory(Path.Join(root, ".GIT"));
    Directory.CreateDirectory(Path.Join(root, ".Lip"));
    try {
      File.WriteAllText(Path.Join(root, "a.txt"), "a");
      File.WriteAllText(Path.Join(root, ".GIT", "x"), "x");
      File.WriteAllText(Path.Join(root, ".Lip", "y"), "y");
      bool ignoresCase = Directory.Exists(Path.Join(root, ".git"));
      string output = Path.Join(root, "out.zip");

      // Act
      await Packer.WriteArchive(fs, root, output);

      // Assert
      Assert.Equal(
          ignoresCase ? ["a.txt"] : [".GIT/x", ".Lip/y", "a.txt"],
          (await Entries(fs, output)).Keys.Order(StringComparer.Ordinal));
    }
    finally {
      Directory.Delete(root, recursive: true);
    }
  }

  [Fact]
  public async Task WriteArchive_OutputByAnotherCase_IsLeftOut() {
    // Arrange: on a case-insensitive file system (e.g. macOS, Windows) another case names the
    // same file.
    FileSystem fs = new();
    string root = UnixFileTypeTests.MakeTempRootPath();
    Directory.CreateDirectory(root);
    try {
      File.WriteAllText(Path.Join(root, "a.txt"), "a");
      File.WriteAllText(Path.Join(root, "pkg.zip"), "previous");
      Assert.SkipUnless(
          File.Exists(Path.Join(root, "PKG.ZIP")), "The file system is case-sensitive.");

      // Act
      await Packer.WriteArchive(fs, root, Path.Join(root, "Pkg.zip"));

      // Assert
      Assert.Equal(["a.txt"], (await Entries(fs, Path.Join(root, "pkg.zip"))).Keys);
    }
    finally {
      Directory.Delete(root, recursive: true);
    }
  }

  [Fact]
  public async Task WriteArchive_OutputByItsShortName_IsLeftOut() {
    Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows has 8.3 short names.");

    // Arrange
    FileSystem fs = new();
    string root = UnixFileTypeTests.MakeTempRootPath();
    Directory.CreateDirectory(root);
    try {
      File.WriteAllText(Path.Join(root, "a.txt"), "a");
      string output = Path.Join(root, "longfilename.zip");
      File.WriteAllText(output, "previous");
      string shortName = ShortName(output);
      Assert.SkipWhen(
          shortName is "" or "longfilename.zip", "8.3 short names are disabled on this volume.");

      // Act
      await Packer.WriteArchive(fs, root, Path.Join(root, shortName));

      // Assert
      Assert.Equal(["a.txt"], (await Entries(fs, output)).Keys);
    }
    finally {
      Directory.Delete(root, recursive: true);
    }
  }

  /// Links the directory link to target: a junction on Windows, which needs no privilege, and a
  /// symbolic link elsewhere. Returns whether the link was created.
  private static bool LinkDirectory(string link, string target) {
    if (!OperatingSystem.IsWindows()) {
      Directory.CreateSymbolicLink(link, target);
      return true;
    }

    using Process process = Process.Start(new ProcessStartInfo("cmd.exe", ["/c", "mklink", "/J", link, target]) {
      RedirectStandardOutput = true,
      RedirectStandardError = true,
    })!;
    process.WaitForExit();
    return process.ExitCode == 0 && Directory.Exists(link);
  }

  /// The 8.3 short name of an existing file, or "" when it cannot be read.
  private static string ShortName(string path) {
    char[] buffer = new char[path.Length + 260];
    uint length = GetShortPathNameW(path, buffer, (uint)buffer.Length);
    return length == 0 || length >= buffer.Length
        ? ""
        : Path.GetFileName(new string(buffer, 0, (int)length));
  }

  [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
  private static extern uint GetShortPathNameW(string longPath, char[] shortPath, uint length);

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
