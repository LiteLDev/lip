using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Lock;
using Moq;

namespace Lip.Core.Tests.Mcmod;

/// The server layout (spec §7): mods in mods/, plugins/ a link to mods/. The harness links with
/// symbolic links of the MockFileSystem, which does not resolve them.
public class WorkspaceLayoutTests {
  private const string Lib = "example.com/lib";

  private static readonly OperationOptions s_options = new();

  private readonly WorkspaceHarness _ws = new();

  private void AddMod(string name = "Demo") {
    _ws.AddZip("pkg.mcmod", new() {
      ["manifest.json"] = WorkspaceHarness.Concrete(name, "1.0.0", "native"),
      [$"{name}.dll"] = "dll",
    });
  }

  private string? LinkTarget(string relative) =>
      _ws.Fs.DirectoryInfo.New(_ws.Path(relative)).LinkTarget;

  private SortedDictionary<string, string> Snapshot() {
    SortedDictionary<string, string> snapshot = new(StringComparer.Ordinal);
    foreach (string path in _ws.Fs.AllPaths) {
      snapshot[path] = _ws.Fs.File.Exists(path)
          ? Convert.ToBase64String(_ws.Fs.File.ReadAllBytes(path))
          : "<dir>";
    }

    return snapshot;
  }

  [Theory]
  [InlineData("plugins", "mods")]
  [InlineData("plugins/A/a.dll", "mods/A/a.dll")]
  [InlineData("mods/A/a.dll", "mods/A/a.dll")]
  [InlineData("pluginsx/a.dll", "pluginsx/a.dll")]
  [InlineData("x/plugins/a.dll", "x/plugins/a.dll")]
  public void CanonicalPath_Paths_MapPluginsToMods(string path, string expected) {
    // Act
    string canonical = WorkspaceLayout.CanonicalPath(path);

    // Assert
    Assert.Equal(expected, canonical);
  }

  [Fact]
  public void CanonicalPath_OtherCase_IgnoresCaseOnWindowsOnly() {
    // Act
    string canonical = WorkspaceLayout.CanonicalPath("Plugins/A/a.dll");

    // Assert
    Assert.Equal(OperatingSystem.IsWindows() ? "mods/A/a.dll" : "Plugins/A/a.dll", canonical);
  }

  [Fact]
  public async Task Install_EmptyWorkspace_CreatesModsAndLinksPlugins() {
    // Arrange
    AddMod();

    // Act
    await _ws.Service.Install(["pkg.mcmod"], s_options);

    // Assert
    Assert.True(_ws.Exists("mods/Demo/Demo.dll"));
    Assert.Equal((_ws.Path("plugins"), _ws.Path("mods")), Assert.Single(_ws.Linker.Created));
    Assert.All(
        Assert.Single((await _ws.Lock()).Packages).Files,
        f => Assert.StartsWith("mods/Demo/", f));
  }

  [Fact]
  public async Task Install_PluginsAlreadyLinked_LeavesLayout() {
    // Arrange
    AddMod();
    _ws.Fs.AddDirectory(_ws.Path("mods"));
    _ws.Fs.Directory.CreateSymbolicLink(_ws.Path("plugins"), "mods");

    // Act
    await _ws.Service.Install(["pkg.mcmod"], s_options);

    // Assert
    Assert.Empty(_ws.Linker.Created);
    Assert.Equal("mods", LinkTarget("plugins"));
    Assert.True(_ws.Exists("mods/Demo/Demo.dll"));
  }

  [Fact]
  public async Task Install_PluginsDirectory_MovesEntriesIntoModsAndLinks() {
    // Arrange
    AddMod();
    _ws.AddFile("plugins/Old/Old.dll", "old");
    _ws.AddFile("plugins/loose.txt", "loose");
    _ws.AddFile("mods/Kept/Kept.dll", "kept");

    // Act
    await _ws.Service.Install(["pkg.mcmod"], s_options);

    // Assert
    Assert.Equal("old", _ws.Fs.File.ReadAllText(_ws.Path("mods/Old/Old.dll")));
    Assert.Equal("loose", _ws.Fs.File.ReadAllText(_ws.Path("mods/loose.txt")));
    Assert.True(_ws.Exists("mods/Kept/Kept.dll"));
    Assert.True(_ws.Exists("mods/Demo/Demo.dll"));
    Assert.Equal(_ws.Path("mods"), LinkTarget("plugins"));
  }

  [Fact]
  public async Task Install_PluginsDirectoryWithoutMods_RenamesItToMods() {
    // Arrange
    AddMod();
    _ws.AddFile("plugins/Old/Old.dll", "old");

    // Act
    await _ws.Service.Install(["pkg.mcmod"], s_options);

    // Assert
    Assert.Equal("old", _ws.Fs.File.ReadAllText(_ws.Path("mods/Old/Old.dll")));
    Assert.Equal(_ws.Path("mods"), LinkTarget("plugins"));
  }

  [Fact]
  public async Task Install_EntryInBothPluginsAndMods_ThrowsWithoutChanges() {
    // Arrange
    AddMod();
    _ws.AddFile("plugins/A/a.dll", "plugins a");
    _ws.AddFile("plugins/B/b.dll", "plugins b");
    _ws.AddFile("mods/B/b.dll", "mods b");
    SortedDictionary<string, string> before = Snapshot();

    // Act
    WorkspaceLayoutException ex = await Assert.ThrowsAsync<WorkspaceLayoutException>(
        () => _ws.Service.Install(["pkg.mcmod"], s_options));

    // Assert
    Assert.Contains("'B'", ex.Message);
    Assert.DoesNotContain("'A'", ex.Message);
    Assert.Equal(before, Snapshot());
    Assert.Empty(_ws.Linker.Created);
  }

  [Fact]
  public async Task Install_PluginsLinksElsewhere_ThrowsWithoutChanges() {
    // Arrange
    AddMod();
    _ws.Fs.AddDirectory(_ws.Path("elsewhere"));
    _ws.Fs.Directory.CreateSymbolicLink(_ws.Path("plugins"), "elsewhere");
    SortedDictionary<string, string> before = Snapshot();

    // Act & Assert
    await Assert.ThrowsAsync<WorkspaceLayoutException>(
        () => _ws.Service.Install(["pkg.mcmod"], s_options));
    Assert.Equal(before, Snapshot());
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task Install_LinkCannotBeCreated_UndoesMovesAndThrows(bool modsExists) {
    // Arrange
    AddMod();
    _ws.AddFile("plugins/Old/Old.dll", "old");
    _ws.AddFile("plugins/loose.txt", "loose");
    if (modsExists) {
      _ws.AddFile("mods/Kept/Kept.dll", "kept");
    }
    SortedDictionary<string, string> before = Snapshot();
    _ws.Linker.Fail = true;

    // Act
    WorkspaceLayoutException ex = await Assert.ThrowsAsync<WorkspaceLayoutException>(
        () => _ws.Service.Install(["pkg.mcmod"], s_options));

    // Assert
    Assert.Contains("links are not supported", ex.Message);
    Assert.Equal(before, Snapshot());
  }

  [Fact]
  public async Task Install_LinkCannotBeCreatedInEmptyWorkspace_RemovesCreatedMods() {
    // Arrange
    AddMod();
    _ws.Linker.Fail = true;

    // Act & Assert
    await Assert.ThrowsAsync<WorkspaceLayoutException>(
        () => _ws.Service.Install(["pkg.mcmod"], s_options));
    Assert.False(_ws.Fs.Directory.Exists(_ws.Path("mods")));
  }

  [Fact]
  public async Task Install_DryRun_ReportsWithoutChangingLayout() {
    // Arrange
    AddMod();
    _ws.AddFile("plugins/Old/Old.dll", "old");
    _ws.AddFile("mods/Kept/Kept.dll", "kept");
    SortedDictionary<string, string> before = Snapshot();

    // Act
    await _ws.Service.Install(["pkg.mcmod"], s_options with { DryRun = true });

    // Assert
    Assert.Equal(before, Snapshot());
    Assert.Empty(_ws.Linker.Created);
    _ws.Ui.Verify(u => u.PrintInfo("Dry run: would move plugins/Old to mods/Old"));
    _ws.Ui.Verify(u => u.PrintInfo("Dry run: would link plugins/ to mods/"));
  }

  [Fact]
  public async Task Install_ResolutionFails_LeavesLayout() {
    // Arrange
    _ws.AddFile("plugins/Old/Old.dll", "old");

    // Act & Assert
    await Assert.ThrowsAnyAsync<Exception>(() => _ws.Service.Install([Lib], s_options));
    Assert.True(_ws.Exists("plugins/Old/Old.dll"));
    Assert.False(_ws.Fs.Directory.Exists(_ws.Path("mods")));
    Assert.Empty(_ws.Linker.Created);
  }

  [Fact]
  public async Task List_PluginsDirectory_LeavesLayout() {
    // Arrange
    _ws.AddFile("plugins/Old/Old.dll", "old");

    // Act
    await _ws.Service.List();

    // Assert
    Assert.True(_ws.Exists("plugins/Old/Old.dll"));
    Assert.False(_ws.Fs.Directory.Exists(_ws.Path("mods")));
  }

  [Fact]
  public async Task Install_ToothPlacingUnderPlugins_WritesAndLocksUnderMods() {
    // Arrange
    _ws.AddTag(Lib, "1.0.0", new() {
      ["tooth.json"] = WorkspaceHarness.Tooth(Lib, "1.0.0", "plugins/lib/lib.dll"),
      ["file.dll"] = "lib",
    });

    // Act
    await _ws.Service.Install([Lib], s_options);

    // Assert
    Assert.Equal("lib", _ws.Fs.File.ReadAllText(_ws.Path("mods/lib/lib.dll")));
    Assert.Equal(["mods/lib/lib.dll"], Assert.Single((await _ws.Lock()).Packages).Files);
  }

  [Fact]
  public async Task Uninstall_LastPackage_KeepsModsAndPluginsLink() {
    // Arrange
    _ws.AddTag(Lib, "1.0.0", new() {
      ["tooth.json"] = WorkspaceHarness.Tooth(Lib, "1.0.0", "plugins/lib/lib.dll"),
      ["file.dll"] = "lib",
    });
    await _ws.Service.Install([Lib], s_options);

    // Act
    await _ws.Service.Uninstall([Lib], s_options);

    // Assert
    Assert.False(_ws.Exists("mods/lib/lib.dll"));
    Assert.True(_ws.Fs.Directory.Exists(_ws.Path("mods")));
    Assert.Equal(_ws.Path("mods"), LinkTarget("plugins"));
  }

  [Fact]
  public async Task Uninstall_LockWithPluginsPaths_MigratesLayoutAndRemovesFiles() {
    // Arrange: a mod installed while plugins was the server mod directory.
    string manifest = WorkspaceHarness.Concrete("Demo", "1.0.0", "native");
    _ws.AddFile("plugins/Demo/Demo.dll", "dll");
    _ws.AddFile("plugins/Demo/manifest.json", manifest);
    _ws.AddFile("plugins/Other/Other.dll", "other");
    await _ws.LockStore.Save(_ws.Root, new LockFile {
      Platform = "server",
      Packages = [
        new LockedPackage {
          Identity = "Demo",
          Version = "1.0.0",
          Kind = "mcmod",
          Explicit = true,
          Chain = [new LockedHop("https://example.com/pkg.mcmod", null)],
          ArchiveKey = "https://example.com/pkg.mcmod",
          Archive = new ZipArchiveIdentity(TestIdentity.Sha256, null),
          Files = ["plugins/Demo/Demo.dll", "plugins/Demo/manifest.json"],
          Manifest = JsonDocument.Parse(manifest).RootElement.Clone(),
        },
      ],
    });

    // Act
    await _ws.Service.Uninstall(["Demo"], s_options);

    // Assert
    Assert.False(_ws.Fs.Directory.Exists(_ws.Path("mods/Demo")));
    Assert.True(_ws.Exists("mods/Other/Other.dll"));
    Assert.Equal(_ws.Path("mods"), LinkTarget("plugins"));
    Assert.Empty((await _ws.Lock()).Packages);
  }

  [Fact]
  public async Task Install_FilePackagePlacingUnderPlugins_ConflictsWithModFiles() {
    // Arrange
    AddMod();
    await _ws.Service.Install(["pkg.mcmod"], s_options);
    _ws.AddZip("files.mcmod", new() {
      ["manifest.json"] = WorkspaceHarness.Concrete("Patch", "1.0.0", "files", """
            "placements": [ { "src": "Demo.dll", "dest": "plugins/Demo/Demo.dll" } ]
          """),
      ["Demo.dll"] = "patched",
    });

    // Act & Assert
    Exception ex = await Assert.ThrowsAnyAsync<Exception>(
        () => _ws.Service.Install(["files.mcmod"], s_options));
    Assert.Contains("mods/Demo/Demo.dll (owned by Demo)", ex.Message);
    Assert.Equal("dll", _ws.Fs.File.ReadAllText(_ws.Path("mods/Demo/Demo.dll")));
  }
}

/// The layout on the real file system: a junction on Windows, a symbolic link elsewhere.
public class WorkspaceLayoutRealFileSystemTests : IDisposable {
  private readonly string _root =
      Path.Combine(Path.GetTempPath(), "lip-layout-test-" + Guid.NewGuid().ToString("N"));

  private readonly WorkspaceLayout _layout;

  public WorkspaceLayoutRealFileSystemTests() {
    Directory.CreateDirectory(_root);
    FileSystem fs = new();
    _layout = new WorkspaceLayout(
        fs, new DirectoryLinker(fs), new Mock<IUserInteraction>().Object);
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
  public async Task Prepare_PluginsDirectory_MovesEntriesAndLinksPluginsToMods() {
    // Arrange
    Directory.CreateDirectory(Path.Combine(_root, "plugins", "Old"));
    File.WriteAllText(Path.Combine(_root, "plugins", "Old", "Old.dll"), "old");
    Directory.CreateDirectory(Path.Combine(_root, "mods", "Kept"));

    // Act: the second call finds the layout right.
    await _layout.Prepare(_root, dryRun: false);
    await _layout.Prepare(_root, dryRun: false);

    // Assert
    Assert.Equal("old", File.ReadAllText(Path.Combine(_root, "mods", "Old", "Old.dll")));
    Assert.True(Directory.Exists(Path.Combine(_root, "mods", "Kept")));
    DirectoryInfo plugins = new(Path.Combine(_root, "plugins"));
    Assert.NotNull(plugins.LinkTarget);
    if (!OperatingSystem.IsWindows()) {
      Assert.Equal("mods", plugins.LinkTarget);
    }

    // Through the link.
    Assert.Equal("old", File.ReadAllText(Path.Combine(_root, "plugins", "Old", "Old.dll")));
    File.WriteAllText(Path.Combine(_root, "plugins", "new.txt"), "new");
    Assert.True(File.Exists(Path.Combine(_root, "mods", "new.txt")));
  }
}
