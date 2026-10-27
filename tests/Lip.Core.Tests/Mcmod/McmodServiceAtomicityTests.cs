using Lip.Core.Infrastructure;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Install;
using Lip.Core.Mcmod.Lock;
using Moq;

namespace Lip.Core.Tests.Mcmod;

/// Operations are all or nothing (spec §7): a failure anywhere leaves every file of the
/// workspace, the layout and the lock exactly as they were.
public class McmodServiceAtomicityTests {
  private static readonly OperationOptions s_options = new();

  private WorkspaceHarness _ws = new();

  private static string Scripts(string stage, params string[] commands) =>
      $"\"scripts\": {{ \"{stage}\": [{string.Join(", ", commands.Select(c => $"\"{c}\""))}] }}";

  private void AddMod(
      string file,
      string name,
      string version = "1.0.0",
      string extra = "",
      Dictionary<string, string>? files = null) {
    Dictionary<string, string> content =
        new(files ?? new() { [$"{name}.dll"] = $"{name} {version}" }) {
          ["manifest.json"] = WorkspaceHarness.Concrete(name, version, "native", extra),
        };
    _ws.AddZip(file, content);
  }

  private void FailCommand(string command) =>
      _ws.CommandRunner.Setup(r => r.Run(command, It.IsAny<string>()))
          .ThrowsAsync(new InvalidOperationException("Command failed with exit code 1"));

  [Fact]
  public async Task Install_SecondPackagePostInstallFails_UndoesFirstPackageAndKeepsLock() {
    // Arrange
    AddMod("base.mcmod", "Base");
    await _ws.Service.Install(["base.mcmod"], s_options);
    AddMod("a.mcmod", "A", extra: Scripts("postInstall", "a-post"));
    AddMod("b.mcmod", "B", extra: Scripts("postInstall", "fail"));
    FailCommand("fail");
    SortedDictionary<string, string> before = _ws.Snapshot();

    // Act
    McmodInstallException ex = await Assert.ThrowsAsync<McmodInstallException>(
        () => _ws.Service.Install(["a.mcmod", "b.mcmod"], s_options));

    // Assert: A's script ran (scripts cannot be undone), but its files are gone again.
    Assert.Contains("postInstall command 'fail' failed", ex.Message);
    _ws.CommandRunner.Verify(r => r.Run("a-post", _ws.Root), Times.Once);
    Assert.Equal(before, _ws.Snapshot());
    Assert.False(_ws.Fs.File.Exists(_ws.JournalPath));
  }

  [Fact]
  public async Task Install_SecondPackageConflicts_RunsNoScriptAndChangesNothing() {
    // Arrange
    AddMod("base.mcmod", "Base");
    await _ws.Service.Install(["base.mcmod"], s_options);
    AddMod("a.mcmod", "A", extra: Scripts("preInstall", "a-pre"));
    _ws.AddZip("patch.mcmod", new() {
      ["manifest.json"] = WorkspaceHarness.Concrete("Patch", "1.0.0", "files", """
            "placements": [ { "src": "x.dll", "dest": "mods/Base/Base.dll" } ]
          """),
      ["x.dll"] = "x",
    });
    SortedDictionary<string, string> before = _ws.Snapshot();

    // Act
    McmodInstallException ex = await Assert.ThrowsAsync<McmodInstallException>(
        () => _ws.Service.Install(["a.mcmod", "patch.mcmod"], s_options));

    // Assert
    Assert.Contains("owned by Base", ex.Message);
    _ws.CommandRunner.Verify(r => r.Run(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    Assert.Equal(before, _ws.Snapshot());
  }

  /// Installs Demo 1.0.0, then makes the update to Demo 2.0.0 fail at the n-th file creation or
  /// move; returns whether the update failed.
  private async Task<bool> UpdateFailingAt(int n) {
    _ws = new WorkspaceHarness();
    _ws.AddFile("plugins/Old/Old.dll", "old");
    _ws.AddFile("plugins/loose.txt", "loose");
    AddMod("demo.mcmod", "Demo", "1.0.0", Scripts("preUninstall", "pre"), new() {
      ["Demo.dll"] = "dll 1",
      ["data/a.txt"] = "a 1",
      ["data/b.txt"] = "b 1",
    });
    await _ws.Service.Install(["demo.mcmod"], s_options);
    _ws.AddFile("mods/Demo/config.json", "user");
    AddMod("demo.mcmod", "Demo", "2.0.0", "\"preserveFiles\": [\"config.json\"]", new() {
      ["Demo.dll"] = "dll 2",
      ["data/a.txt"] = "a 2",
      ["new/c.txt"] = "c 2",
      ["config.json"] = "default",
    });
    SortedDictionary<string, string> before = _ws.Snapshot();
    _ws.Faults.FailNthCall(n);

    try {
      await _ws.Service.Update(["demo.mcmod"], s_options);
    }
    catch (McmodInstallException) {
      Assert.Equal(before, _ws.Snapshot());
      Assert.False(_ws.Fs.File.Exists(_ws.JournalPath));
      return true;
    }

    return false;
  }

  [Fact]
  public async Task Update_EveryFileOperationFailing_LeavesWorkspaceAndLockUnchanged() {
    // Act & Assert: each failure point in turn, until the update gets through.
    int n = 1;
    while (await UpdateFailingAt(n)) {
      n++;
    }

    Assert.True(n > 8, $"only {n - 1} failure points");
    Assert.Equal("dll 2", _ws.Fs.File.ReadAllText(_ws.Path("mods/Demo/Demo.dll")));
    Assert.Equal("user", _ws.Fs.File.ReadAllText(_ws.Path("mods/Demo/config.json")));
    Assert.False(_ws.Exists("mods/Demo/data/b.txt"));
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
      AddMod("demo.mcmod", "Demo");
      SortedDictionary<string, string> before = _ws.Snapshot();
      _ws.Faults.FailNthCall(n);

      // Act
      Exception? ex = await Record.ExceptionAsync(
          () => _ws.Service.Install(["demo.mcmod"], s_options));

      // Assert
      if (ex is null) {
        Assert.True(n > 2);
        Assert.Equal("loose", _ws.Fs.File.ReadAllText(_ws.Path("mods/loose.txt")));
        return;
      }

      Assert.Equal(before, _ws.Snapshot());
      Assert.False(_ws.Fs.Directory.Exists(_ws.Path("mods/Old")));
    }
  }

  [Fact]
  public async Task Uninstall_DeleteFails_RestoresOtherPackageAndKeepsLock() {
    // Arrange
    AddMod("a.mcmod", "A");
    AddMod("b.mcmod", "B");
    await _ws.Service.Install(["a.mcmod", "b.mcmod"], s_options);
    SortedDictionary<string, string> before = _ws.Snapshot();
    // B is uninstalled first (reverse install order), then A fails.
    _ws.Faults.Fail = (operation, path) => operation == "move" && path.EndsWith("A.dll");

    // Act
    McmodInstallException ex = await Assert.ThrowsAsync<McmodInstallException>(
        () => _ws.Service.Uninstall(["A", "B"], s_options));

    // Assert
    Assert.Contains("cannot delete", ex.Message);
    Assert.Equal(before, _ws.Snapshot());
  }

  [Fact]
  public async Task Uninstall_PostUninstallFails_RestoresDeletedFilesAndDirectories() {
    // Arrange
    AddMod("a.mcmod", "A", extra: Scripts("postUninstall", "fail"), files: new() {
      ["A.dll"] = "a",
      ["deep/dir/file.txt"] = "f",
    });
    await _ws.Service.Install(["a.mcmod"], s_options);
    FailCommand("fail");
    SortedDictionary<string, string> before = _ws.Snapshot();

    // Act & Assert
    await Assert.ThrowsAsync<McmodInstallException>(
        () => _ws.Service.Uninstall(["A"], s_options));
    Assert.Equal(before, _ws.Snapshot());
  }

  [Fact]
  public async Task Uninstall_NoDependenciesSecondPackageFails_UndoesFirst() {
    // Arrange: B is uninstalled first (reverse install order), then A's script fails.
    AddMod("a.mcmod", "A", extra: Scripts("preUninstall", "fail"));
    AddMod("b.mcmod", "B");
    await _ws.Service.Install(["a.mcmod", "b.mcmod"], s_options);
    FailCommand("fail");
    SortedDictionary<string, string> before = _ws.Snapshot();

    // Act & Assert
    await Assert.ThrowsAsync<McmodInstallException>(() => _ws.Service.Uninstall(
        ["A", "B"], s_options with { NoDependencies = true }));
    Assert.Equal(before, _ws.Snapshot());
  }

  [Fact]
  public async Task Install_CancelledWhileApplying_UndoesChanges() {
    // Arrange
    AddMod("base.mcmod", "Base");
    await _ws.Service.Install(["base.mcmod"], s_options);
    AddMod("a.mcmod", "A", extra: Scripts("postInstall", "a-post"));
    AddMod("b.mcmod", "B");
    using CancellationTokenSource cancellation = new();
    _ws.CommandRunner.Setup(r => r.Run("a-post", It.IsAny<string>()))
        .Callback(cancellation.Cancel)
        .Returns(Task.CompletedTask);
    SortedDictionary<string, string> before = _ws.Snapshot();

    // Act & Assert
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _ws.Service.Install(
        ["a.mcmod", "b.mcmod"], s_options with { CancellationToken = cancellation.Token }));
    Assert.Equal(before, _ws.Snapshot());
    _ws.Ui.Verify(u => u.PrintWarning(It.Is<string>(s => s.StartsWith("Cancelled"))));
  }

  [Fact]
  public async Task Operation_AfterInterruptedOperation_UndoesItFirst() {
    // Arrange: a process killed while installing over Base, with a staged lock.
    AddMod("base.mcmod", "Base", files: new() { ["Base.dll"] = "base", ["sub/x.txt"] = "x" });
    await _ws.Service.Install(["base.mcmod"], s_options);
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
        () => _ws.Service.Uninstall(["Missing"], s_options));

    // Assert
    Assert.Equal(before, _ws.Snapshot());
    Assert.Equal("Base", Assert.Single((await _ws.Lock()).Packages).Identity);
    _ws.Ui.Verify(u => u.PrintWarning(It.Is<string>(
        s => s.Contains("was interrupted") && s.Contains("undid its 7 changes"))));
  }

  [Fact]
  public async Task Operation_AfterInterruptedCommit_FinishesIt() {
    // Arrange: a process killed after committing the uninstall of Base, before moving the lock
    // into place.
    AddMod("base.mcmod", "Base", files: new() { ["Base.dll"] = "base", ["sub/x.txt"] = "x" });
    await _ws.Service.Install(["base.mcmod"], s_options);
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
        () => _ws.Service.Uninstall(["Base"], s_options));

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
    AddMod("base.mcmod", "Base");
    await _ws.Service.Install(["base.mcmod"], s_options);
    using (WorkspaceTransaction killed = new(_ws.Fs, _ws.JournalPath)) {
      killed.DeleteFile(_ws.Path("mods/Base/Base.dll"));
    }

    SortedDictionary<string, string> before = _ws.Snapshot();

    // Act
    await _ws.Service.Uninstall(["Base"], s_options with { DryRun = true });

    // Assert
    Assert.Equal(before, _ws.Snapshot());
    _ws.Ui.Verify(u => u.PrintWarning(It.Is<string>(s => s.Contains("interrupted"))));
  }

  [Fact]
  public async Task Install_Succeeds_LeavesNoBackupJournalOrMutex() {
    // Arrange
    AddMod("demo.mcmod", "Demo");
    _ws.AddFile("mods/Demo/Demo.dll", "unowned");

    // Act
    await _ws.Service.Install(["demo.mcmod"], s_options);

    // Assert
    Assert.Equal("Demo 1.0.0", _ws.Fs.File.ReadAllText(_ws.Path("mods/Demo/Demo.dll")));
    string workspaces = _ws.Fs.Path.GetDirectoryName(_ws.LockPath)!;
    Assert.Equal([_ws.LockPath], _ws.Fs.Directory.GetFiles(workspaces));
    Assert.DoesNotContain(_ws.Fs.AllFiles, f => f.Contains(".bak", StringComparison.Ordinal));
  }
}
