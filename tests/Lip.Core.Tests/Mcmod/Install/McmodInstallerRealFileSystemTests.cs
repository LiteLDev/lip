using System.IO.Abstractions;
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
    ToothEntry entry = ToothParser.ParseEntry(entryJson.RootElement);
    Package package = new("example.com/legacymoney", "", SemVersion.Parse("0.22.0"), entry) {
      Manifest = Resolution.TestCandidates.Manifest("example.com/legacymoney", "0.22.0", "", entry),
    };
    ArchiveHandle archive = new(
        "example.com/legacymoney@0.22.0",
        new ZipSource(WorkspaceHarness.Zip(new() { ["tooth.json"] = "{}" })),
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
      Source = package.Tooth,
      Commit = archive.Commit,
      Files = [.. owned],
    }, context);

    // Assert
    Assert.False(Directory.Exists(Path.Combine(_root, "mods", "LegacyMoney")));
    Assert.True(Directory.Exists(Path.Combine(_root, "mods")));
    Assert.True(File.Exists(Path.Combine(_root, "post_uninstall.txt")));
  }
}
