using System.IO.Abstractions;
using System.Runtime.InteropServices;
using System.Text.Json;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Install;
using Lip.Core.Mcmod.Lock;
using Moq;
using Semver;

namespace Lip.Core.Tests.Mcmod.Install;

/// Installing and uninstalling a mod on the real file system (protocol/workspace.md §2, §4), with
/// the real command runner.
public class McmodInstallerRealFileSystemTests : IDisposable {
  private const string ModUrl = "https://example.com/LegacyMoney.mcmod";

  private readonly string _root =
      Path.Combine(Path.GetTempPath(), "lip-installer-test-" + Guid.NewGuid().ToString("N"));

  public McmodInstallerRealFileSystemTests() {
    Directory.CreateDirectory(_root);
  }

  public void Dispose() {
    if (Directory.Exists(_root)) {
      Directory.Delete(_root, recursive: true);
    }
    GC.SuppressFinalize(this);
  }

  [Fact]
  public async Task InstallAndUninstall_RealCommandRunner_RunsHooksInWorkspaceRoot() {
    // Arrange
    byte[] zip = WorkspaceHarness.Zip(new() {
      ["manifest.json"] = """{"name":"LegacyMoney"}""",
      ["LegacyMoney.dll"] = "dll",
      ["lang/en_US.json"] = "{}",
    });
    Mock<IArchiveProvider> provider = new();
    provider.Setup(p => p.OpenAsset(
            It.Is<IReadOnlyList<string>>(u => u.Single() == ModUrl)))
        .ReturnsAsync(new ZipSource(zip));
    using JsonDocument entryJson = JsonDocument.Parse($$"""
        {
          "platform": "server",
          "runtime": "win-x64",
          "assets": [ { "type": "mcmod", "urls": [ "{{ModUrl}}" ] } ],
          "purge": [ "mods/LegacyMoney/data/" ],
          "hooks": {
            "pre_install": [ "echo pre> pre_install.txt" ],
            "post_install": [ "echo post> post_install.txt" ],
            "post_uninstall": [ "echo done> post_uninstall.txt" ]
          }
        }
        """);
    PackageEntry entry = PackageFileParser.ParseEntry(entryJson.RootElement);
    Package package = new("example.com/legacymoney", "", SemVersion.Parse("0.22.0"), entry) {
      Manifest = Resolution.TestCandidates.Manifest("example.com/legacymoney", "0.22.0", "", entry),
    };
    ArchiveHandle archive = new(
        "example.com/legacymoney@0.22.0",
        new ZipSource(WorkspaceHarness.Zip(new() { ["lip.yaml"] = "{}" })),
        new string('0', 40));
    Candidate candidate = Candidate.Create(
        package, archive, Ref.Parse("example.com/legacymoney", allowFile: false), provider.Object);

    FileSystem fs = new();
    SoloInstaller installer =
        new(fs, new McmodInstaller(fs, new CommandRunner(), new Mock<IUserInteraction>().Object));
    InstallContext context = new(
        _root,
        new LipEnvironment(McmodPlatform.Server, "win-x64"),
        new LockFile { Platform = "server", Runtime = "win-x64", Packages = [] },
        false,
        false);
    // Act: install
    IReadOnlyList<string> owned = await installer.Install(candidate, context);

    // Assert
    Assert.Equal(3, owned.Count);
    Assert.All(owned, f => Assert.StartsWith("mods/LegacyMoney/", f));
    Assert.Equal("dll", File.ReadAllText(Path.Combine(_root, "mods", "LegacyMoney",
        "LegacyMoney.dll")));
    using (JsonDocument manifest = JsonDocument.Parse(
        File.ReadAllText(Path.Combine(_root, "mods", "LegacyMoney", "manifest.json")))) {
      Assert.False(manifest.RootElement.TryGetProperty("dependencies", out _));
    }
    Assert.True(File.Exists(Path.Combine(_root, "pre_install.txt")));
    Assert.True(File.Exists(Path.Combine(_root, "post_install.txt")));

    // Act: uninstall
    Directory.CreateDirectory(Path.Combine(_root, "mods", "LegacyMoney", "data", "x"));
    File.WriteAllText(Path.Combine(_root, "mods", "LegacyMoney", "data", "x", "a.db"), "a");
    await installer.Uninstall(new LockedPackage {
      Manifest = package.Manifest,
      Variant = package.Variant,
      Explicit = true,
      Source = package.Id,
      Commit = archive.Commit,
      Files = [.. owned],
    }, context);

    // Assert
    Assert.False(Directory.Exists(Path.Combine(_root, "mods", "LegacyMoney")));
    Assert.True(Directory.Exists(Path.Combine(_root, "mods")));
    Assert.True(File.Exists(Path.Combine(_root, "post_uninstall.txt")));
  }

  // 8.3 short names (Windows): a path through the short name of a directory names what the long
  // name does, so the write rules and purge see it by its long name (protocol/workspace.md §4).

  [Fact]
  public async Task Install_DestThroughShortNameOfLip_ThrowsWithoutWriting() {
    // Arrange
    string lip = Path.Combine(_root, ".lip");
    Directory.CreateDirectory(lip);
    File.WriteAllText(Path.Combine(lip, "workspace.yaml"), "keep");
    string shortName = ShortNameOrSkip(lip);
    Candidate candidate = SelfCandidate(
        "example.com/x", Place("x.txt", $"{shortName}/workspace.yaml"),
        "", new() { ["x.txt"] = "x" });

    // Act & Assert
    McmodInstallException ex = await Assert.ThrowsAsync<McmodInstallException>(
        () => Installer().Install(candidate, Context()));
    Assert.Contains(".lip/workspace.yaml", ex.Message);
    Assert.Equal("keep", File.ReadAllText(Path.Combine(lip, "workspace.yaml")));
  }

  [Fact]
  public async Task Install_DestThroughShortNameOfFileOwnedByAnother_ThrowsWithoutWriting() {
    // Arrange
    string directory = Path.Combine(_root, "pluginsdirectory");
    Directory.CreateDirectory(directory);
    File.WriteAllText(Path.Combine(directory, "foo.dll"), "other");
    string shortName = ShortNameOrSkip(directory);
    Candidate other = SelfCandidate("example.com/other", "", "", []);
    LockFile lockFile = new() {
      Platform = "server",
      Runtime = "win-x64",
      Packages = [Locked(other, ["pluginsdirectory/foo.dll"])],
    };
    Candidate candidate = SelfCandidate(
        "example.com/x", Place("foo.dll", $"{shortName}/foo.dll"),
        "", new() { ["foo.dll"] = "mine" });

    // Act & Assert
    McmodInstallException ex = await Assert.ThrowsAsync<McmodInstallException>(
        () => Installer().Install(candidate, Context(lockFile)));
    Assert.Contains("pluginsdirectory/foo.dll (owned by example.com/other)", ex.Message);
    Assert.Equal("other", File.ReadAllText(Path.Combine(directory, "foo.dll")));
  }

  [Fact]
  public async Task Install_DestThroughShortName_OwnsTheLongName() {
    // Arrange
    string directory = Path.Combine(_root, "pluginsdirectory");
    Directory.CreateDirectory(directory);
    string shortName = ShortNameOrSkip(directory);
    Candidate candidate = SelfCandidate(
        "example.com/x", Place("foo.dll", $"{shortName}/foo.dll"),
        "", new() { ["foo.dll"] = "mine" });

    // Act
    IReadOnlyList<string> owned = await Installer().Install(candidate, Context());

    // Assert
    Assert.Equal(["pluginsdirectory/foo.dll"], owned);
    Assert.Equal("mine", File.ReadAllText(Path.Combine(directory, "foo.dll")));
  }

  [Fact]
  public async Task Uninstall_PurgeThroughShortNameOfLip_DeletesNothingInLip() {
    // Arrange
    string lip = Path.Combine(_root, ".lip");
    Directory.CreateDirectory(lip);
    File.WriteAllText(Path.Combine(lip, "workspace.yaml"), "keep");
    string shortName = ShortNameOrSkip(lip);
    Candidate candidate = SelfCandidate(
        "example.com/x", "",
        $$""" "purge": [ "{{shortName}}/", "{{shortName}}/*", "{{shortName}}/workspace.yaml" ] """,
        []);

    // Act
    await Installer().Uninstall(Locked(candidate, []), Context());

    // Assert
    Assert.Equal("keep", File.ReadAllText(Path.Combine(lip, "workspace.yaml")));
  }

  private static string Place(string src, string dest) =>
      $$""" "place": [ { "src": "{{src}}", "dest": "{{dest}}" } ] """;

  private static SoloInstaller Installer() {
    FileSystem fs = new();
    return new(
        fs, new McmodInstaller(fs, new CommandRunner(), new Mock<IUserInteraction>().Object));
  }

  private InstallContext Context(LockFile? lockFile = null) => new(
      _root,
      new LipEnvironment(McmodPlatform.Server, "win-x64"),
      lockFile ?? new LockFile { Platform = "server", Runtime = "win-x64", Packages = [] },
      false,
      false);

  /// A package of id whose one asset of type "self" holds files, with the given members of the
  /// asset (e.g. "place") and of the entry.
  private static Candidate SelfCandidate(
      string id, string asset, string entry, Dictionary<string, string> files) {
    string assetMembers = asset == "" ? "" : ", " + asset;
    string entryMembers = entry == "" ? "" : ", " + entry;
    using JsonDocument json = JsonDocument.Parse($$"""
        {
          "platform": "server",
          "runtime": "win-x64",
          "assets": [ { "type": "self"{{assetMembers}} } ]{{entryMembers}}
        }
        """);
    PackageEntry parsed = PackageFileParser.ParseEntry(json.RootElement);
    Package package = new(id, "", SemVersion.Parse("1.0.0"), parsed) {
      Manifest = Resolution.TestCandidates.Manifest(id, "1.0.0", "", parsed),
    };
    ArchiveHandle archive = new(
        $"{id}@1.0.0", new ZipSource(WorkspaceHarness.Zip(files)), new string('0', 40));
    return Candidate.Create(
        package, archive, Ref.Parse(id, allowFile: false), new Mock<IArchiveProvider>().Object);
  }

  private static LockedPackage Locked(Candidate candidate, IEnumerable<string> files) => new() {
    Manifest = candidate.Package.Manifest,
    Variant = candidate.Package.Variant,
    Explicit = true,
    Source = candidate.Source.ToString(),
    Commit = candidate.Archive.Commit,
    Files = [.. files],
  };

  /// The 8.3 short name of an existing directory; skips the test when it has none, as off
  /// Windows or on a volume that does not create them.
  private static string ShortNameOrSkip(string path) {
    Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows has 8.3 short names.");
    char[] buffer = new char[path.Length + 260];
    uint length = GetShortPathNameW(path, buffer, (uint)buffer.Length);
    string shortName = length == 0 || length >= buffer.Length
        ? ""
        : Path.GetFileName(new string(buffer, 0, (int)length));
    Assert.SkipWhen(
        shortName == "" || shortName == Path.GetFileName(path),
        "The volume does not create 8.3 short names.");
    return shortName;
  }

  [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
  private static extern uint GetShortPathNameW(string longPath, char[] shortPath, uint length);
}
