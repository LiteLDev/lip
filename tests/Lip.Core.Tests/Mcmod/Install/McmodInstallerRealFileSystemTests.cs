using System.IO.Abstractions;
using System.Text.Json;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Install;
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
      // A recursive delete cannot remove a junction without administrator rights (it unmounts
      // it first), so the link goes first.
      string plugins = Path.Combine(_root, "plugins");
      if (new DirectoryInfo(plugins).LinkTarget is not null) {
        Directory.Delete(plugins);
      }

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
            It.Is<IReadOnlyList<string>>(u => u.Single() == ModUrl), AssetFormat.Zip))
        .ReturnsAsync(new OpenedAsset(new ZipSource(zip), new AssetIdentity(ModUrl, TestIdentity.Sha256)));
    using JsonDocument entryJson = JsonDocument.Parse($$"""
        {
          "platform": "server",
          "runtime": "win-x64",
          "assets": [ { "type": "mcmod", "urls": [ "{{ModUrl}}" ] } ],
          "remove_files": [ "plugins/LegacyMoney/data/" ],
          "hooks": {
            "pre_install": [ "echo pre> pre_install.txt" ],
            "post_install": [ "echo post> post_install.txt" ],
            "post_uninstall": [ "echo done> post_uninstall.txt" ]
          }
        }
        """);
    ToothEntry entry = ToothParser.ParseEntry(entryJson.RootElement, legacy: false);
    Package package = new("example.com/legacymoney", "", SemVersion.Parse("0.22.0"), entry);
    ArchiveHandle archive = new(
        "example.com/legacymoney@0.22.0",
        new ZipSource(WorkspaceHarness.Zip(new() { ["tooth.json"] = "{}" })),
        new GitArchiveIdentity("https://example.com/legacymoney.git", new string('0', 40)));
    Candidate candidate = Candidate.Create(
        package, archive, Ref.Parse("example.com/legacymoney"), provider.Object);

    FileSystem fs = new();
    McmodInstaller installer =
        new(fs, new CommandRunner(), new Mock<IUserInteraction>().Object);
    InstallContext context = new(
        _root, new LipEnvironment(McmodPlatform.Server, "win-x64"), new LockFile(), false, false);
    // The server layout: plugins links to mods.
    await new WorkspaceLayout(fs, new DirectoryLinker(fs), new Mock<IUserInteraction>().Object)
        .Prepare(_root, dryRun: false);

    // Act: install
    IReadOnlyList<string> owned = await installer.Install(candidate, context);

    // Assert
    Assert.Equal(3, owned.Count);
    Assert.All(owned, f => Assert.StartsWith("mods/LegacyMoney/", f));
    // Written under mods, visible through plugins.
    Assert.Equal("dll", File.ReadAllText(Path.Combine(_root, "plugins", "LegacyMoney",
        "LegacyMoney.dll")));
    using (JsonDocument manifest = JsonDocument.Parse(
        File.ReadAllText(Path.Combine(_root, "mods", "LegacyMoney", "manifest.json")))) {
      Assert.Equal(0, manifest.RootElement.GetProperty("dependencies").GetArrayLength());
    }
    Assert.True(File.Exists(Path.Combine(_root, "pre_install.txt")));
    Assert.True(File.Exists(Path.Combine(_root, "post_install.txt")));

    // Act: uninstall
    Directory.CreateDirectory(Path.Combine(_root, "plugins", "LegacyMoney", "data", "x"));
    File.WriteAllText(Path.Combine(_root, "plugins", "LegacyMoney", "data", "x", "a.db"), "a");
    await installer.Uninstall(new LockedPackage {
      Tooth = package.Tooth,
      Variant = package.Variant,
      Version = package.Version.ToString(),
      Explicit = true,
      Source = package.Tooth,
      Archive = archive.Identity,
      Assets = [new AssetIdentity(ModUrl, TestIdentity.Sha256)],
      Entry = entry.Json,
      Files = [.. owned],
    }, context);

    // Assert
    Assert.False(Directory.Exists(Path.Combine(_root, "mods", "LegacyMoney")));
    Assert.True(Directory.Exists(Path.Combine(_root, "mods")));
    Assert.NotNull(new DirectoryInfo(Path.Combine(_root, "plugins")).LinkTarget);
    Assert.True(File.Exists(Path.Combine(_root, "post_uninstall.txt")));
  }
}
