using System.IO.Abstractions;
using System.Text;
using System.Text.Json;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Install;
using Lip.Core.Sources;
using Moq;
using Semver;

namespace Lip.Core.Tests.Mcmod.Install;

public class McmodInstallerRealFileSystemTests : IDisposable {
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
  public async Task InstallAndUninstall_RealCommandRunner_RunsScriptsInWorkspaceRoot() {
    // Arrange
    string archiveDir = _root + "-archive";
    Directory.CreateDirectory(Path.Combine(archiveDir, "lang"));
    File.WriteAllText(Path.Combine(archiveDir, "manifest.json"), "{}");
    File.WriteAllText(Path.Combine(archiveDir, "LegacyMoney.dll"), "dll");
    File.WriteAllText(Path.Combine(archiveDir, "lang", "en_US.json"), "{}");
    FileSystem fs = new();
    try {
      ConcreteDocument document = new() {
        IsMarked = true,
        Name = "LegacyMoney",
        Version = SemVersion.Parse("0.22.0"),
        Type = "native",
        RemoveFiles = [new PathGlob("data/")],
        Scripts = new Scripts {
          PreInstall = ["echo pre> pre_install.txt"],
          PostInstall = ["echo post> post_install.txt"],
          PostUninstall = ["echo done> post_uninstall.txt"],
        },
      };
      const string Manifest = "{\"name\":\"LegacyMoney\"}";
      Mock<IDocumentParser> parser = new();
      parser.Setup(p => p.Parse(It.Is<byte[]>(b => Encoding.UTF8.GetString(b) == Manifest)))
          .Returns(document);
      JsonElement manifest = JsonDocument.Parse(Manifest).RootElement;
      McmodInstaller installer =
          new(fs, new CommandRunner(), parser.Object, new Mock<IUserInteraction>().Object);
      Candidate candidate = new(
          new McmodPackage(document, manifest),
          new ArchiveHandle(
              archiveDir, new DirectorySource(fs.DirectoryInfo.New(archiveDir)),
              new DirectoryArchiveIdentity(archiveDir)),
          []);
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
      Assert.True(File.Exists(Path.Combine(_root, "pre_install.txt")));
      Assert.True(File.Exists(Path.Combine(_root, "post_install.txt")));

      // Act: uninstall
      Directory.CreateDirectory(Path.Combine(_root, "plugins", "LegacyMoney", "data", "x"));
      File.WriteAllText(Path.Combine(_root, "plugins", "LegacyMoney", "data", "x", "a.db"), "a");
      await installer.Uninstall(new LockedPackage {
        Identity = "LegacyMoney",
        Version = "0.22.0",
        Kind = McmodPackage.KindName,
        Explicit = true,
        Chain = [],
        ArchiveKey = archiveDir,
        Files = [.. owned],
        Manifest = manifest,
      }, context);

      // Assert
      Assert.False(Directory.Exists(Path.Combine(_root, "mods", "LegacyMoney")));
      Assert.True(Directory.Exists(Path.Combine(_root, "mods")));
      Assert.NotNull(new DirectoryInfo(Path.Combine(_root, "plugins")).LinkTarget);
      Assert.True(File.Exists(Path.Combine(_root, "post_uninstall.txt")));
    }
    finally {
      Directory.Delete(archiveDir, recursive: true);
    }
  }
}
