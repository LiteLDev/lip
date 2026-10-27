using System.IO.Abstractions.TestingHelpers;
using Lip.Core.Mcmod.Install;

namespace Lip.Core.Tests.Mcmod.Install;

public class WorkspaceTransactionTests {
  private static readonly string _root = OperatingSystem.IsWindows() ? @"C:\ws" : "/ws";
  private static readonly string _journal =
      OperatingSystem.IsWindows() ? @"C:\data\ws.journal" : "/data/ws.journal";

  private readonly MockFileSystem _fs = new();

  public WorkspaceTransactionTests() {
    _fs.AddDirectory(_root);
    AddFile("keep.txt", "keep");
    AddFile("replace.txt", "original");
    AddFile("delete.txt", "deleted");
    AddFile("dir/inner.txt", "inner");
    _fs.File.SetAttributes(Full("replace.txt"), FileAttributes.ReadOnly);
    _fs.File.SetAttributes(Full("delete.txt"), FileAttributes.Hidden);
  }

  private static string Full(string relative) =>
      Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));

  private void AddFile(string relative, string content) =>
      _fs.AddFile(Full(relative), new MockFileData(content));

  private SortedDictionary<string, string> Snapshot() {
    SortedDictionary<string, string> snapshot = new(StringComparer.Ordinal);
    foreach (string path in _fs.AllPaths.Where(
        p => p.StartsWith(_root, StringComparison.Ordinal))) {
      snapshot[path] = _fs.File.Exists(path)
          ? $"{_fs.File.ReadAllText(path)} ({_fs.File.GetAttributes(path)})"
          : "<dir>";
    }

    return snapshot;
  }

  /// Replaces, creates and deletes files and directories, as an install over an uninstall does.
  private static async Task Change(WorkspaceTransaction transaction) {
    CancellationToken token = TestContext.Current.CancellationToken;
    using MemoryStream replaced = new("new"u8.ToArray());
    await transaction.WriteFile(Full("replace.txt"), replaced, token);
    using MemoryStream created = new("created"u8.ToArray());
    await transaction.WriteFile(Full("new/deep/created.txt"), created, token);
    transaction.DeleteFile(Full("delete.txt"));
    transaction.DeleteFile(Full("dir/inner.txt"));
    transaction.DeleteDirectoryOnCommit(Full("dir"));
    // Deleted, then written again.
    using MemoryStream again = new("again"u8.ToArray());
    await transaction.WriteFile(Full("delete.txt"), again, token);
  }

  [Fact]
  public async Task Rollback_AfterChanges_RestoresEverythingAndDeletesJournal() {
    // Arrange
    SortedDictionary<string, string> before = Snapshot();
    using WorkspaceTransaction transaction = new(_fs, _journal);
    await Change(transaction);

    // Act
    IReadOnlyList<string> errors = transaction.Rollback();

    // Assert
    Assert.Empty(errors);
    Assert.Equal(before, Snapshot());
    Assert.False(_fs.File.Exists(_journal));
  }

  [Fact]
  public async Task WriteFile_ExistingFile_BacksItUpAsHiddenSibling() {
    // Arrange
    using WorkspaceTransaction transaction = new(_fs, _journal);
    using MemoryStream content = new("new"u8.ToArray());

    // Act
    await transaction.WriteFile(
        Full("replace.txt"), content, TestContext.Current.CancellationToken);

    // Assert
    Assert.Equal("original", _fs.File.ReadAllText(Full(".replace.txt.bak")));
    Assert.Equal(
        OperatingSystem.IsWindows(),
        _fs.File.GetAttributes(Full(".replace.txt.bak")).HasFlag(FileAttributes.Hidden));
    Assert.Equal("new", _fs.File.ReadAllText(Full("replace.txt")));
  }

  [Fact]
  public async Task Complete_AfterCommit_DeletesBackupsEmptiedDirectoriesAndJournal() {
    // Arrange
    using WorkspaceTransaction transaction = new(_fs, _journal);
    await Change(transaction);

    // Act
    transaction.Commit();
    IReadOnlyList<string> errors = transaction.Complete();

    // Assert
    Assert.Empty(errors);
    Assert.Equal(
        new SortedDictionary<string, string>(StringComparer.Ordinal) {
          [_root] = "<dir>",
          [Full("delete.txt")] = "again (Normal)",
          [Full("keep.txt")] = "keep (Normal)",
          [Full("new")] = "<dir>",
          [Full("new/deep")] = "<dir>",
          [Full("new/deep/created.txt")] = "created (Normal)",
          [Full("replace.txt")] = "new (Normal)",
        },
        Snapshot());
    Assert.False(_fs.File.Exists(_journal));
  }

  [Fact]
  public async Task Recover_KilledBeforeCommit_UndoesChangesAndDiscardsLock() {
    // Arrange: disposed of without commit or rollback, as by a killed process.
    SortedDictionary<string, string> before = Snapshot();
    using (WorkspaceTransaction transaction = new(_fs, _journal)) {
      await Change(transaction);
    }

    bool discarded = false;

    // Act
    WorkspaceTransaction.RecoveryResult? result = WorkspaceTransaction.Recover(
        _fs, _journal, () => Assert.Fail("committed"), () => discarded = true);

    // Assert
    Assert.NotNull(result);
    Assert.False(result.Committed);
    Assert.NotNull(result.Started);
    Assert.Empty(result.Errors);
    Assert.True(discarded);
    Assert.Equal(before, Snapshot());
    Assert.False(_fs.File.Exists(_journal));
  }

  [Fact]
  public async Task Recover_KilledAfterCommit_CommitsLockAndDeletesBackups() {
    // Arrange
    using (WorkspaceTransaction transaction = new(_fs, _journal)) {
      await Change(transaction);
      transaction.Commit();
    }

    bool committed = false;

    // Act
    WorkspaceTransaction.RecoveryResult? result = WorkspaceTransaction.Recover(
        _fs, _journal, () => committed = true, () => Assert.Fail("discarded"));

    // Assert
    Assert.True(result!.Committed);
    Assert.True(committed);
    Assert.Equal("again", _fs.File.ReadAllText(Full("delete.txt")));
    Assert.False(_fs.Directory.Exists(Full("dir")));
    Assert.DoesNotContain(_fs.AllFiles, f => f.Contains(".bak", StringComparison.Ordinal));
    Assert.False(_fs.File.Exists(_journal));
  }

  [Fact]
  public async Task Recover_JournalReplayedAfterRollback_ChangesNothing() {
    // Arrange: a rollback whose journal is replayed, as when lip is killed before deleting it.
    SortedDictionary<string, string> before = Snapshot();
    string copy = _journal + ".copy";
    using (WorkspaceTransaction transaction = new(_fs, _journal)) {
      await Change(transaction);
      _fs.File.Copy(_journal, copy);
      transaction.Rollback();
    }

    _fs.File.Move(copy, _journal);

    // Act
    WorkspaceTransaction.RecoveryResult? result =
        WorkspaceTransaction.Recover(_fs, _journal, () => { }, () => { });

    // Assert
    Assert.Empty(result!.Errors);
    Assert.Equal(before, Snapshot());
  }

  [Fact]
  public async Task Recover_ChangeCannotBeUndone_KeepsItInJournalForNextTime() {
    // Arrange: the created file is in use by another process.
    SortedDictionary<string, string> before = Snapshot();
    using (WorkspaceTransaction transaction = new(_fs, _journal)) {
      await Change(transaction);
    }

    _fs.GetFile(Full("new/deep/created.txt")).AllowedFileShare = FileShare.None;

    // Act
    WorkspaceTransaction.RecoveryResult? first =
        WorkspaceTransaction.Recover(_fs, _journal, () => { }, () => { });
    _fs.GetFile(Full("new/deep/created.txt")).AllowedFileShare =
        FileShare.ReadWrite | FileShare.Delete;
    WorkspaceTransaction.RecoveryResult? second =
        WorkspaceTransaction.Recover(_fs, _journal, () => { }, () => { });

    // Assert
    Assert.Contains(first!.Errors, e => e.Contains("created.txt"));
    Assert.Empty(second!.Errors);
    Assert.Equal(before, Snapshot());
    Assert.False(_fs.File.Exists(_journal));
  }

  [Fact]
  public void Recover_NoJournal_ReturnsNull() {
    Assert.Null(WorkspaceTransaction.Recover(_fs, _journal, () => { }, () => { }));
  }
}
