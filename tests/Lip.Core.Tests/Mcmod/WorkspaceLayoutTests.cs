using Lip.Core.Mcmod;

namespace Lip.Core.Tests.Mcmod;

/// The layout (protocol/workspace.md §2): mods in mods/, a directory. plugins/ is an ordinary
/// path.
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
      ["lip.toml"] = WorkspaceHarness.LipToml(Demo, "1.0.0",
          $$""" "assets": [ { "type": "mcmod", "urls": [ "{{DemoUrl}}" ] } ] """),
    });
  }

  /// The workspace part of the snapshot: lip's cache, outside it, may change.
  private SortedDictionary<string, string> Snapshot() => new(
      _ws.Snapshot().Where(e => e.Key.StartsWith(_ws.Root, StringComparison.Ordinal))
          .ToDictionary(e => e.Key, e => e.Value),
      StringComparer.Ordinal);

  [Fact]
  public async Task Install_EmptyWorkspace_CreatesMods() {
    // Arrange
    AddMod();

    // Act
    await _ws.Service.Install([Demo], s_options);

    // Assert
    Assert.True(_ws.Exists("mods/Demo/Demo.dll"));
    Assert.False(_ws.Fs.Directory.Exists(_ws.Path("plugins")));
    Assert.All(
        Assert.Single((await _ws.Lock()).Packages).Files,
        f => Assert.StartsWith("mods/Demo/", f));
  }

  [Fact]
  public async Task Install_PluginsDirectory_LeavesIt() {
    // Arrange
    AddMod();
    _ws.AddFile("plugins/Old/Old.dll", "old");

    // Act
    await _ws.Service.Install([Demo], s_options);

    // Assert
    Assert.True(_ws.Exists("plugins/Old/Old.dll"));
    Assert.False(_ws.Exists("mods/Old/Old.dll"));
    Assert.True(_ws.Exists("mods/Demo/Demo.dll"));
  }

  [Fact]
  public async Task Install_ModsIsALink_ThrowsWithoutChanges() {
    // Arrange: a mods link would let writes under mods/ land outside the workspace.
    AddMod();
    _ws.Fs.AddDirectory(_ws.Path("shared"));
    _ws.Fs.Directory.CreateSymbolicLink(_ws.Path("mods"), "shared");
    SortedDictionary<string, string> before = Snapshot();

    // Act & Assert
    await Assert.ThrowsAsync<WorkspaceLayoutException>(
        () => _ws.Service.Install([Demo], s_options));
    Assert.Equal(before, Snapshot());
  }

  [Fact]
  public async Task Install_DryRun_ReportsWithoutCreatingMods() {
    // Arrange
    AddMod();
    SortedDictionary<string, string> before = Snapshot();

    // Act
    await _ws.Service.Install([Demo], s_options with { DryRun = true });

    // Assert
    Assert.Equal(before, Snapshot());
    _ws.Ui.Verify(u => u.PrintInfo("Dry run: would create the directory mods/"));
  }

  [Fact]
  public async Task Install_ResolutionFails_DoesNotCreateMods() {
    // Act & Assert
    await Assert.ThrowsAnyAsync<Exception>(() => _ws.Service.Install([Lib], s_options));
    Assert.False(_ws.Fs.Directory.Exists(_ws.Path("mods")));
  }

  [Fact]
  public async Task Install_PackagePlacingUnderPlugins_WritesAndLocksUnderPlugins() {
    // Arrange: plugins is a directory like any other.
    _ws.AddTag(Lib, "1.0.0", new() {
      ["lip.toml"] = WorkspaceHarness.LipToml(
          Lib, "1.0.0", WorkspaceHarness.SelfAsset("file.dll", "plugins/lib/lib.dll")),
      ["file.dll"] = "lib",
    });

    // Act
    await _ws.Service.Install([Lib], s_options);

    // Assert
    Assert.Equal("lib", _ws.Fs.File.ReadAllText(_ws.Path("plugins/lib/lib.dll")));
    Assert.Equal(["plugins/lib/lib.dll"], Assert.Single((await _ws.Lock()).Packages).Files);
  }

  [Fact]
  public async Task Uninstall_LastPackage_KeepsMods() {
    // Arrange
    AddMod();
    await _ws.Service.Install([Demo], s_options);

    // Act
    await _ws.Service.Uninstall([Demo], s_options);

    // Assert
    Assert.False(_ws.Fs.Directory.Exists(_ws.Path("mods/Demo")));
    Assert.True(_ws.Fs.Directory.Exists(_ws.Path("mods")));
  }
}
