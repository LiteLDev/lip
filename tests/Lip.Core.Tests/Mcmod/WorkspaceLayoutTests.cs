using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Install;
using Lip.Core.Mcmod.Lock;
using Moq;

namespace Lip.Core.Tests.Mcmod;

/// The server layout (protocol/workspace.md §2): mods in mods/, plugins/ a link to mods/. The harness links with
/// symbolic links of the MockFileSystem, which does not resolve them.
public class WorkspaceLayoutTests {
  private const string Lib = "example.com/lib";
  private const string Demo = "example.com/demo";
  private const string DemoUrl = "https://example.com/demo.mcmod";

  private static readonly OperationOptions s_options = new(Rid: WorkspaceHarness.Runtime);

  private readonly WorkspaceHarness _ws = new();

  /// The mod Demo: a package of format 4 with an asset of type "mcmod" holding Demo.dll.
  private void AddMod() {
    _ws.AddDownload(DemoUrl, WorkspaceHarness.Zip(new() {
      ["manifest.json"] = """{ "name": "Demo", "dependencies": [] }""",
      ["Demo.dll"] = "dll",
    }));
    _ws.AddTag(Demo, "1.0.0", new() {
      ["tooth.json"] = WorkspaceHarness.Tooth4(Demo, "1.0.0",
          $$""" "assets": [ { "type": "mcmod", "urls": [ "{{DemoUrl}}" ] } ] """),
    });
  }

  private string? LinkTarget(string relative) =>
      _ws.Fs.DirectoryInfo.New(_ws.Path(relative)).LinkTarget;

  /// The workspace part of the snapshot: lip's cache, outside it, may change.
  private SortedDictionary<string, string> Snapshot() => new(
      _ws.Snapshot().Where(e => e.Key.StartsWith(_ws.Root, StringComparison.Ordinal))
          .ToDictionary(e => e.Key, e => e.Value),
      StringComparer.Ordinal);

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
    await _ws.Service.Install([Demo], s_options);

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
    await _ws.Service.Install([Demo], s_options);

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
    await _ws.Service.Install([Demo], s_options);

    // Assert
    Assert.Equal("old", _ws.Fs.File.ReadAllText(_ws.Path("mods/Old/Old.dll")));
    Assert.Equal("loose", _ws.Fs.File.ReadAllText(_ws.Path("mods/loose.txt")));
    Assert.True(_ws.Exists("mods/Kept/Kept.dll"));
    Assert.True(_ws.Exists("mods/Demo/Demo.dll"));
    Assert.Equal(_ws.Path("mods"), LinkTarget("plugins"));
  }

  [Fact]
  public async Task Install_PluginsDirectoryWithoutMods_CreatesModsAndMovesEntriesIntoIt() {
    // Arrange
    AddMod();
    _ws.AddFile("plugins/Old/Old.dll", "old");
    _ws.AddFile("plugins/loose.txt", "loose");

    // Act
    await _ws.Service.Install([Demo], s_options);

    // Assert
    Assert.Equal("old", _ws.Fs.File.ReadAllText(_ws.Path("mods/Old/Old.dll")));
    Assert.Equal("loose", _ws.Fs.File.ReadAllText(_ws.Path("mods/loose.txt")));
    Assert.Null(LinkTarget("mods"));
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
        () => _ws.Service.Install([Demo], s_options));

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
        () => _ws.Service.Install([Demo], s_options));
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
        () => _ws.Service.Install([Demo], s_options));

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
        () => _ws.Service.Install([Demo], s_options));
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
    await _ws.Service.Install([Demo], s_options with { DryRun = true });

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
  public async Task Uninstall_PluginsDirectory_MigratesLayoutAndRemovesFiles() {
    // Arrange: a mod installed while plugins was a directory, the lock in the mods/ form.
    AddMod();
    await _ws.Service.Install([Demo], s_options);
    LockFile lockFile = await _ws.Lock();
    _ws.Fs.Directory.Delete(_ws.Path("plugins"));
    _ws.Fs.Directory.Move(_ws.Path("mods"), _ws.Path("plugins"));
    _ws.AddFile("plugins/Other/Other.dll", "other");
    _ws.Linker.Created.Clear();

    // Act
    await _ws.Service.Uninstall([Demo], s_options);

    // Assert
    Assert.Equal(["mods/Demo/Demo.dll", "mods/Demo/manifest.json"],
        Assert.Single(lockFile.Packages).Files.Order(StringComparer.Ordinal));
    Assert.False(_ws.Fs.Directory.Exists(_ws.Path("mods/Demo")));
    Assert.True(_ws.Exists("mods/Other/Other.dll"));
    Assert.Equal(_ws.Path("mods"), LinkTarget("plugins"));
    Assert.Empty((await _ws.Lock()).Packages);
  }

  [Fact]
  public async Task Install_PlacingUnderPlugins_ConflictsWithModFiles() {
    // Arrange
    AddMod();
    await _ws.Service.Install([Demo], s_options);
    _ws.AddTag("example.com/patch", "1.0.0", new() {
      ["tooth.json"] = WorkspaceHarness.Tooth4("example.com/patch", "1.0.0",
          WorkspaceHarness.SelfAsset("Demo.dll", "plugins/Demo/Demo.dll")),
      ["Demo.dll"] = "patched",
    });

    // Act & Assert
    Exception ex = await Assert.ThrowsAnyAsync<Exception>(
        () => _ws.Service.Install(["example.com/patch"], s_options));
    Assert.Contains("mods/Demo/Demo.dll (owned by example.com/demo)", ex.Message);
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
      foreach (string link in new[] { "plugins", "mods" }.Select(n => Path.Combine(_root, n))) {
        if (new DirectoryInfo(link).LinkTarget is not null) {
          Directory.Delete(link);
        }
      }

      Directory.Delete(_root, recursive: true);
    }
    GC.SuppressFinalize(this);
  }

  /// Establishes the layout as an operation does, through a journaled transaction.
  private async Task Establish() {
    FileSystem fs = new();
    using WorkspaceTransaction transaction = new(fs, new LockStore(fs).GetJournalPath(_root));
    await _layout.Apply(_layout.Plan(_root), transaction);
    transaction.Commit();
    transaction.Complete();
  }

  [Fact]
  public async Task Apply_PluginsDirectory_MovesEntriesAndLinksPluginsToMods() {
    // Arrange
    Directory.CreateDirectory(Path.Combine(_root, "plugins", "Old"));
    File.WriteAllText(Path.Combine(_root, "plugins", "Old", "Old.dll"), "old");
    Directory.CreateDirectory(Path.Combine(_root, "mods", "Kept"));

    // Act: the second time finds the layout right.
    await Establish();
    await Establish();

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

  [Fact]
  public void Plan_ModsIsALink_ThrowsWithoutChanges() {
    // Arrange: a mods link would let writes under mods/ land outside the workspace.
    FileSystem fs = new();
    Directory.CreateDirectory(Path.Combine(_root, "shared"));
    new DirectoryLinker(fs).CreateLink(Path.Combine(_root, "mods"), Path.Combine(_root, "shared"));

    // Act & Assert
    Assert.Throws<WorkspaceLayoutException>(() => _layout.Plan(_root));
    Assert.False(Path.Exists(Path.Combine(_root, "plugins")));
  }

  [Fact]
  public void Plan_ModsIsAFile_PlansToCreateIt() {
    // Arrange: protocol/workspace.md §2 has no rule for a mods file; it fails when created.
    File.WriteAllText(Path.Combine(_root, "mods"), "file");

    // Act
    WorkspaceLayout.LayoutPlan plan = _layout.Plan(_root);

    // Assert
    Assert.True(plan.CreateMods);
    Assert.False(Path.Exists(Path.Combine(_root, "plugins")));
  }
}
