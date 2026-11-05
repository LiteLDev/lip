using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Install;
using Moq;

namespace Lip.Core.Tests.Mcmod;

/// Operations are all or nothing (protocol/workspace.md §6): an early failure changes nothing and
/// runs no hook command; a later failure undoes every change of the workspace, the layout and the
/// workspace file.
public class McmodServiceAtomicityTests {
  private static readonly OperationOptions _options = new(Rid: WorkspaceHarness.Runtime);

  private WorkspaceHarness _ws = new();

  private static string Hooks(string stage, params string[] commands) =>
      $"\"hooks\": {{ \"{stage}\": [{string.Join(", ", commands.Select(c => $"\"{c}\""))}] }}";

  /// Writes the local file &lt;file&gt;: a package example.com/&lt;name in lowercase&gt; of
  /// format 4 whose entry holds the given extra members and an asset of type "mcmod" downloaded
  /// from https://example.com/&lt;name&gt;-&lt;version&gt;.mcmod, which holds a mod manifest
  /// named name and the given files (by default &lt;name&gt;.dll).
  private void AddMod(
      string file,
      string name,
      string version = "1.0.0",
      string extra = "",
      Dictionary<string, string>? files = null,
      string? manifest = null) {
    string url = $"https://example.com/{name}-{version}.mcmod";
    Dictionary<string, string> content =
        new(files ?? new() { [$"{name}.dll"] = $"{name} {version}" }) {
          ["manifest.json"] = manifest
              ?? $$"""{ "name": "{{name}}", "entry": "{{name}}.dll", "type": "native" }""",
        };
    _ws.AddDownload(url, WorkspaceHarness.Zip(content));
    AddPackage(file, name, version,
        (extra == "" ? "" : extra + ", ")
        + $$""" "assets": [ { "type": "mcmod", "urls": [ "{{url}}" ] } ]""");
  }

  /// Writes the local file &lt;file&gt;: a package example.com/&lt;name in lowercase&gt; of
  /// format 4 with the given entry members and the given files.
  private void AddPackage(
      string file,
      string name,
      string version,
      string entry,
      Dictionary<string, string>? files = null) {
    Dictionary<string, string> content = new(files ?? []) {
      ["tooth.json"] = WorkspaceHarness.Tooth4(Tooth(name), version, entry),
    };
    _ws.AddZip(file, content);
  }

  private static string Tooth(string name) => $"example.com/{name.ToLowerInvariant()}";

  private void FailCommand(string command) =>
      _ws.CommandRunner.Setup(r => r.Run(command, It.IsAny<string>()))
          .ThrowsAsync(new InvalidOperationException("Command failed with exit code 1"));

  /// The files, directories and links of the workspace, without lip's cache outside it.
  private SortedDictionary<string, string> WorkspaceSnapshot() => new(
      _ws.Snapshot().Where(e => e.Key.StartsWith(_ws.Root, StringComparison.Ordinal))
          .ToDictionary(e => e.Key, e => e.Value),
      StringComparer.Ordinal);

  private void VerifyNoCommandRan() =>
      _ws.CommandRunner.Verify(r => r.Run(It.IsAny<string>(), It.IsAny<string>()), Times.Never);

  #region Early failures

  /// Installs Base, then sets up a.zip (a mod whose pre_install hook is "a-pre") to be installed
  /// before the failing package; returns the workspace as it is then.
  private async Task<SortedDictionary<string, string>> ArrangeEarlyFailure(Action addFailing) {
    AddMod("base.zip", "Base");
    await _ws.Service.Install(["base.zip"], _options);
    AddMod("a.zip", "A", extra: Hooks("pre_install", "a-pre"));
    addFailing();
    return WorkspaceSnapshot();
  }

  [Fact]
  public async Task Install_AssetCannotBeDownloaded_ChangesNothing() {
    // Arrange
    SortedDictionary<string, string> before = await ArrangeEarlyFailure(() => AddPackage(
        "b.zip", "B", "1.0.0",
        """ "assets": [ { "type": "zip", "urls": [ "https://example.com/missing.zip" ] } ]"""));

    // Act & Assert
    await Assert.ThrowsAnyAsync<Exception>(
        () => _ws.Service.Install(["a.zip", "b.zip"], _options));
    VerifyNoCommandRan();
    Assert.Equal(before, WorkspaceSnapshot());
  }

  [Fact]
  public async Task Install_MissingSrc_ChangesNothing() {
    // Arrange
    SortedDictionary<string, string> before = await ArrangeEarlyFailure(() => AddPackage(
        "b.zip", "B", "1.0.0", WorkspaceHarness.SelfAsset("missing.txt", "b.txt")));

    // Act & Assert
    await Assert.ThrowsAsync<McmodInstallException>(
        () => _ws.Service.Install(["a.zip", "b.zip"], _options));
    VerifyNoCommandRan();
    Assert.Equal(before, WorkspaceSnapshot());
  }

  [Theory]
  [InlineData("""{ "name": "not an id!", "entry": "B.dll", "type": "native" }""")]
  [InlineData("""{ "entry": "B.dll", "type": "native" }""")]
  [InlineData("""[ "B" ]""")]
  [InlineData("not json")]
  public async Task Install_InvalidModManifest_ChangesNothing(string manifest) {
    // Arrange
    SortedDictionary<string, string> before =
        await ArrangeEarlyFailure(() => AddMod("b.zip", "B", manifest: manifest));

    // Act & Assert
    await Assert.ThrowsAnyAsync<Exception>(
        () => _ws.Service.Install(["a.zip", "b.zip"], _options));
    VerifyNoCommandRan();
    Assert.Equal(before, WorkspaceSnapshot());
  }

  [Fact]
  public async Task Install_MissingModManifest_ChangesNothing() {
    // Arrange: the asset holds no manifest.json at its root.
    SortedDictionary<string, string> before = await ArrangeEarlyFailure(() => {
      _ws.AddDownload(
          "https://example.com/B.mcmod", WorkspaceHarness.Zip(new() { ["sub/manifest.json"] = "{}" }));
      AddPackage("b.zip", "B", "1.0.0",
          """ "assets": [ { "type": "mcmod", "urls": [ "https://example.com/B.mcmod" ] } ]""");
    });

    // Act & Assert
    await Assert.ThrowsAnyAsync<Exception>(
        () => _ws.Service.Install(["a.zip", "b.zip"], _options));
    VerifyNoCommandRan();
    Assert.Equal(before, WorkspaceSnapshot());
  }

  [Fact]
  public async Task Install_ModManifestDependenciesDiffer_ChangesNothing() {
    // Arrange: B has no dependency, but its mod manifest declares one.
    SortedDictionary<string, string> before = await ArrangeEarlyFailure(() => AddMod(
        "b.zip",
        "B",
        manifest: """
            { "name": "B", "entry": "B.dll", "type": "native", "dependencies": [ { "name": "Base" } ] }
            """));

    // Act & Assert
    await Assert.ThrowsAsync<McmodInstallException>(
        () => _ws.Service.Install(["a.zip", "b.zip"], _options));
    VerifyNoCommandRan();
    Assert.Equal(before, WorkspaceSnapshot());
  }

  [Fact]
  public async Task Install_ModNameOfInstalledMod_ChangesNothing() {
    // Arrange: B's mod is named like Base's, but for ASCII case.
    SortedDictionary<string, string> before = await ArrangeEarlyFailure(() => AddMod(
        "b.zip", "B", manifest: """{ "name": "BASE", "entry": "B.dll", "type": "native" }"""));

    // Act & Assert
    await Assert.ThrowsAsync<DependencyResolutionException>(
        () => _ws.Service.Install(["a.zip", "b.zip"], _options));
    VerifyNoCommandRan();
    Assert.Equal(before, WorkspaceSnapshot());
  }

  [Fact]
  public async Task Install_FileOwnedByInstalledPackage_ChangesNothing() {
    // Arrange
    SortedDictionary<string, string> before = await ArrangeEarlyFailure(() => AddPackage(
        "patch.zip",
        "Patch",
        "1.0.0",
        WorkspaceHarness.SelfAsset("x.dll", "mods/Base/Base.dll"),
        new() { ["x.dll"] = "x" }));

    // Act
    McmodInstallException ex = await Assert.ThrowsAsync<McmodInstallException>(
        () => _ws.Service.Install(["a.zip", "patch.zip"], _options));

    // Assert
    Assert.Contains("owned by", ex.Message);
    VerifyNoCommandRan();
    Assert.Equal(before, WorkspaceSnapshot());
  }

  [Fact]
  public async Task Install_FileThroughPluginsOwnedByInstalledPackage_ChangesNothing() {
    // Arrange: plugins/ is compared in its mods/ form (protocol/workspace.md §2).
    SortedDictionary<string, string> before = await ArrangeEarlyFailure(() => AddPackage(
        "patch.zip",
        "Patch",
        "1.0.0",
        WorkspaceHarness.SelfAsset("x.dll", "plugins/Base/Base.dll"),
        new() { ["x.dll"] = "x" }));

    // Act & Assert
    await Assert.ThrowsAsync<McmodInstallException>(
        () => _ws.Service.Install(["a.zip", "patch.zip"], _options));
    VerifyNoCommandRan();
    Assert.Equal(before, WorkspaceSnapshot());
  }

  [Fact]
  public async Task Install_IntoLipDirectory_ChangesNothing() {
    // Arrange
    SortedDictionary<string, string> before = await ArrangeEarlyFailure(() => AddPackage(
        "b.zip", "B", "1.0.0", WorkspaceHarness.SelfAsset("x", ".lip/x"), new() { ["x"] = "x" }));

    // Act & Assert
    await Assert.ThrowsAsync<McmodInstallException>(
        () => _ws.Service.Install(["a.zip", "b.zip"], _options));
    VerifyNoCommandRan();
    Assert.Equal(before, WorkspaceSnapshot());
  }

  [Fact]
  public async Task Install_PluginsDirectoryEntryClashesWithMods_ChangesNothing() {
    // Arrange
    _ws.AddFile("plugins/X/a.txt", "plugins");
    _ws.AddFile("mods/X/b.txt", "mods");
    AddMod("a.zip", "A", extra: Hooks("pre_install", "a-pre"));
    SortedDictionary<string, string> before = WorkspaceSnapshot();

    // Act & Assert
    await Assert.ThrowsAnyAsync<Exception>(() => _ws.Service.Install(["a.zip"], _options));
    VerifyNoCommandRan();
    Assert.Equal(before, WorkspaceSnapshot());
  }

  #endregion

  #region Later failures

  [Fact]
  public async Task Install_SecondPackagePostInstallFails_UndoesFirstPackageAndKeepsWorkspaceFile() {
    // Arrange
    AddMod("base.zip", "Base");
    await _ws.Service.Install(["base.zip"], _options);
    AddMod("a.zip", "A", extra: Hooks("post_install", "a-post"));
    AddMod("b.zip", "B", extra: Hooks("post_install", "fail"));
    FailCommand("fail");
    SortedDictionary<string, string> before = WorkspaceSnapshot();

    // Act
    McmodInstallException ex = await Assert.ThrowsAsync<McmodInstallException>(
        () => _ws.Service.Install(["a.zip", "b.zip"], _options));

    // Assert: A's hook ran (hook commands are not undone), but its files are gone again.
    Assert.Contains("command 'fail' failed", ex.Message);
    _ws.CommandRunner.Verify(r => r.Run("a-post", _ws.Root), Times.Once);
    Assert.Equal(before, WorkspaceSnapshot());
    Assert.False(_ws.Fs.File.Exists(_ws.JournalPath));
  }

  [Fact]
  public async Task Install_PreInstallFails_UndoesLayout() {
    // Arrange: a new workspace with a plugins directory, which the layout replaces by a link.
    _ws.AddFile("plugins/Old/Old.dll", "old");
    AddMod("a.zip", "A", extra: Hooks("pre_install", "fail"));
    FailCommand("fail");
    SortedDictionary<string, string> before = WorkspaceSnapshot();

    // Act & Assert
    await Assert.ThrowsAsync<McmodInstallException>(
        () => _ws.Service.Install(["a.zip"], _options));
    Assert.Equal(before, WorkspaceSnapshot());
    Assert.False(_ws.Fs.File.Exists(_ws.LockPath));
  }

  /// Installs Demo 1.0.0, then makes the update to Demo 2.0.0 fail at the n-th file creation or
  /// move; returns whether the update failed.
  private async Task<bool> UpdateFailingAt(int n) {
    _ws = new WorkspaceHarness();
    _ws.AddFile("plugins/Old/Old.dll", "old");
    _ws.AddFile("plugins/loose.txt", "loose");
    AddMod("demo.zip", "Demo", "1.0.0", Hooks("pre_uninstall", "pre"), new() {
      ["Demo.dll"] = "dll 1",
      ["data/a.txt"] = "a 1",
      ["data/b.txt"] = "b 1",
    });
    await _ws.Service.Install(["demo.zip"], _options);
    _ws.AddFile("mods/Demo/config.json", "user");
    AddMod("demo.zip", "Demo", "2.0.0", "\"preserve_files\": [\"mods/Demo/config.json\"]", new() {
      ["Demo.dll"] = "dll 2",
      ["data/a.txt"] = "a 2",
      ["new/c.txt"] = "c 2",
      ["config.json"] = "default",
    });
    SortedDictionary<string, string> before = WorkspaceSnapshot();
    _ws.Faults.FailNthCall(n);

    try {
      await _ws.Service.Update(["demo.zip"], _options);
    }
    catch (McmodInstallException) {
      Assert.Equal(before, WorkspaceSnapshot());
      Assert.False(_ws.Fs.File.Exists(_ws.JournalPath));
      return true;
    }

    return false;
  }

  [Fact]
  public async Task Update_EveryFileOperationFailing_LeavesWorkspaceAndWorkspaceFileUnchanged() {
    // Act & Assert: each failure point in turn, until the update gets through.
    int n = 1;
    while (await UpdateFailingAt(n)) {
      n++;
    }

    Assert.True(n > 8, $"only {n - 1} failure points");
    Assert.Equal("dll 2", _ws.Fs.File.ReadAllText(_ws.Path("mods/Demo/Demo.dll")));
    Assert.Equal("user", _ws.Fs.File.ReadAllText(_ws.Path("mods/Demo/config.json")));
    Assert.False(_ws.Exists("mods/Demo/data/b.txt"));
    Assert.Equal("2.0.0", Assert.Single((await _ws.Lock()).Packages).Version);
    Assert.DoesNotContain(_ws.Fs.AllFiles, f => f.EndsWith(".bak", StringComparison.Ordinal));
  }

  [Fact]
  public async Task Install_EveryFileOperationFailingWhileMigratingPlugins_RestoresPlugins() {
    for (int n = 1; ; n++) {
      // Arrange
      _ws = new WorkspaceHarness();
      _ws.AddFile("plugins/Old/Old.dll", "old");
      _ws.AddFile("plugins/loose.txt", "loose");
      _ws.AddFile("mods/Kept/Kept.dll", "kept");
      AddMod("demo.zip", "Demo");
      SortedDictionary<string, string> before = WorkspaceSnapshot();
      _ws.Faults.FailNthCall(n);

      // Act
      Exception? ex = await Record.ExceptionAsync(
          () => _ws.Service.Install(["demo.zip"], _options));

      // Assert
      if (ex is null) {
        Assert.True(n > 2);
        Assert.Equal("loose", _ws.Fs.File.ReadAllText(_ws.Path("mods/loose.txt")));
        return;
      }

      Assert.Equal(before, WorkspaceSnapshot());
      Assert.False(_ws.Fs.Directory.Exists(_ws.Path("mods/Old")));
    }
  }

  [Fact]
  public async Task Uninstall_DeleteFails_RestoresOtherPackageAndKeepsWorkspaceFile() {
    // Arrange
    AddMod("a.zip", "A");
    AddMod("b.zip", "B");
    await _ws.Service.Install(["a.zip", "b.zip"], _options);
    SortedDictionary<string, string> before = WorkspaceSnapshot();
    // B is uninstalled first (reverse installation order), then A fails.
    _ws.Faults.Fail = (operation, path) => operation == "move" && path.EndsWith("A.dll");

    // Act
    McmodInstallException ex = await Assert.ThrowsAsync<McmodInstallException>(
        () => _ws.Service.Uninstall([Tooth("A"), Tooth("B")], _options));

    // Assert
    Assert.Contains("cannot delete", ex.Message);
    Assert.Equal(before, WorkspaceSnapshot());
  }

  [Fact]
  public async Task Uninstall_PostUninstallFails_RestoresDeletedFilesAndDirectories() {
    // Arrange
    AddMod("a.zip", "A", extra: Hooks("post_uninstall", "fail"), files: new() {
      ["A.dll"] = "a",
      ["deep/dir/file.txt"] = "f",
    });
    await _ws.Service.Install(["a.zip"], _options);
    FailCommand("fail");
    SortedDictionary<string, string> before = WorkspaceSnapshot();

    // Act & Assert
    await Assert.ThrowsAsync<McmodInstallException>(
        () => _ws.Service.Uninstall(["a.zip"], _options));
    Assert.Equal(before, WorkspaceSnapshot());
  }

  [Fact]
  public async Task Uninstall_NoDependenciesSecondPackageFails_UndoesFirst() {
    // Arrange: B is uninstalled first (reverse installation order), then A's hook fails.
    AddMod("a.zip", "A", extra: Hooks("pre_uninstall", "fail"));
    AddMod("b.zip", "B");
    await _ws.Service.Install(["a.zip", "b.zip"], _options);
    FailCommand("fail");
    SortedDictionary<string, string> before = WorkspaceSnapshot();

    // Act & Assert
    await Assert.ThrowsAsync<McmodInstallException>(() => _ws.Service.Uninstall(
        [Tooth("A"), Tooth("B")], _options with { NoDependencies = true }));
    Assert.Equal(before, WorkspaceSnapshot());
  }

  [Fact]
  public async Task Install_CancelledWhileApplying_UndoesChanges() {
    // Arrange
    AddMod("base.zip", "Base");
    await _ws.Service.Install(["base.zip"], _options);
    AddMod("a.zip", "A", extra: Hooks("post_install", "a-post"));
    AddMod("b.zip", "B");
    using CancellationTokenSource cancellation = new();
    _ws.CommandRunner.Setup(r => r.Run("a-post", It.IsAny<string>()))
        .Callback(cancellation.Cancel)
        .Returns(Task.CompletedTask);
    SortedDictionary<string, string> before = WorkspaceSnapshot();

    // Act & Assert
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _ws.Service.Install(
        ["a.zip", "b.zip"], _options with { CancellationToken = cancellation.Token }));
    Assert.Equal(before, WorkspaceSnapshot());
    _ws.Ui.Verify(u => u.PrintWarning(It.Is<string>(s => s.StartsWith("Cancelled"))));
  }

  #endregion

  #region Interruption

  [Fact]
  public async Task Operation_AfterInterruptedOperation_UndoesItFirst() {
    // Arrange: a process killed while installing over Base, with a staged workspace file.
    AddMod("base.zip", "Base", files: new() { ["Base.dll"] = "base", ["sub/x.txt"] = "x" });
    await _ws.Service.Install(["base.zip"], _options);
    SortedDictionary<string, string> before = _ws.Snapshot();
    using (WorkspaceTransaction killed = new(_ws.Fs, _ws.JournalPath)) {
      using MemoryStream content = new("half"u8.ToArray());
      await killed.WriteFile(
          _ws.Path("mods/Base/Base.dll"), content, TestContext.Current.CancellationToken);
      using MemoryStream empty = new();
      await killed.WriteFile(
          _ws.Path("mods/New/a/b.txt"), empty, TestContext.Current.CancellationToken);
      killed.DeleteFile(_ws.Path("mods/Base/sub/x.txt"));
      killed.DeleteDirectoryOnCommit(_ws.Path("mods/Base/sub"));
      await _ws.LockStore.Stage(_ws.Root, new LockFile());
    }

    // A cut-off last line, as when lip is killed while writing it.
    _ws.Fs.File.AppendAllText(_ws.JournalPath, "{\"op\":\"CreateFi");

    // Act: an operation that fails after the recovery.
    await Assert.ThrowsAsync<InvalidOperationException>(
        () => _ws.Service.Uninstall(["Missing"], _options));

    // Assert
    Assert.Equal(before, _ws.Snapshot());
    Assert.Equal(Tooth("Base"), Assert.Single((await _ws.Lock()).Packages).Identity);
    _ws.Ui.Verify(u => u.PrintWarning(It.Is<string>(
        s => s.Contains("was interrupted") && s.Contains("undid its 7 changes"))));
  }

  [Fact]
  public async Task Operation_AfterInterruptedCommit_FinishesIt() {
    // Arrange: a process killed after committing the uninstall of Base, before moving the
    // workspace file into place.
    AddMod("base.zip", "Base", files: new() { ["Base.dll"] = "base", ["sub/x.txt"] = "x" });
    await _ws.Service.Install(["base.zip"], _options);
    using (WorkspaceTransaction killed = new(_ws.Fs, _ws.JournalPath)) {
      killed.DeleteFile(_ws.Path("mods/Base/Base.dll"));
      killed.DeleteFile(_ws.Path("mods/Base/manifest.json"));
      killed.DeleteFile(_ws.Path("mods/Base/sub/x.txt"));
      killed.DeleteDirectoryOnCommit(_ws.Path("mods/Base/sub"));
      killed.DeleteDirectoryOnCommit(_ws.Path("mods/Base"));
      await _ws.LockStore.Stage(_ws.Root, (await _ws.Lock()) with { Packages = [] });
      killed.Commit();
    }

    // Act
    await Assert.ThrowsAsync<InvalidOperationException>(
        () => _ws.Service.Uninstall([Tooth("Base")], _options));

    // Assert: the uninstall is complete, so Base is no longer installed.
    Assert.Empty((await _ws.Lock()).Packages);
    Assert.False(_ws.Fs.Directory.Exists(_ws.Path("mods/Base")));
    Assert.True(_ws.Fs.Directory.Exists(_ws.Path("mods")));
    Assert.False(_ws.Fs.File.Exists(_ws.JournalPath));
    Assert.DoesNotContain(_ws.Fs.AllFiles, f => f.Contains(".bak", StringComparison.Ordinal));
  }

  [Fact]
  public async Task DryRun_AfterInterruptedOperation_WarnsAndLeavesIt() {
    // Arrange
    AddMod("base.zip", "Base");
    await _ws.Service.Install(["base.zip"], _options);
    using (WorkspaceTransaction killed = new(_ws.Fs, _ws.JournalPath)) {
      killed.DeleteFile(_ws.Path("mods/Base/Base.dll"));
    }

    SortedDictionary<string, string> before = _ws.Snapshot();

    // Act
    await _ws.Service.Uninstall([Tooth("Base")], _options with { DryRun = true });

    // Assert
    Assert.Equal(before, _ws.Snapshot());
    _ws.Ui.Verify(u => u.PrintWarning(It.Is<string>(s => s.Contains("interrupted"))));
  }

  #endregion

  [Fact]
  public async Task DryRun_NewWorkspace_CreatesNoLipDirectory() {
    // Arrange
    AddMod("base.zip", "Base");

    // Act
    await _ws.Service.Install(["base.zip"], _options with { DryRun = true });

    // Assert
    Assert.False(_ws.Fs.Directory.Exists(_ws.Path(".lip")));
    Assert.False(_ws.Fs.Directory.Exists(_ws.Path("mods")));
  }

  [Fact]
  public async Task Install_Succeeds_LeavesNoBackupJournalOrStagedFile() {
    // Arrange
    AddMod("demo.zip", "Demo");
    _ws.AddFile("mods/Demo/Demo.dll", "unowned");

    // Act
    await _ws.Service.Install(["demo.zip"], _options);

    // Assert
    Assert.Equal("Demo 1.0.0", _ws.Fs.File.ReadAllText(_ws.Path("mods/Demo/Demo.dll")));
    Assert.Equal(
        [_ws.LockStore.GetMutexPath(_ws.Root), _ws.LockPath],
        _ws.Fs.Directory.GetFiles(_ws.Fs.Path.GetDirectoryName(_ws.LockPath)!).Order());
    Assert.DoesNotContain(_ws.Fs.AllFiles, f => f.Contains(".bak", StringComparison.Ordinal));
  }
}
