using System.IO.Abstractions;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lip.Core.Infrastructure;

namespace Lip.Core.Mcmod.Install;

/// Changes to a workspace that can be undone (protocol/workspace.md §6, §7): an operation applies all of its file
/// changes through one transaction, and either commits them or rolls all of them back.
///
/// - A created directory or file is recorded, and deleted again on rollback.
/// - A file that is replaced or deleted is first renamed to a hidden backup next to it
///   (".&lt;name&gt;.bak", or ".&lt;name&gt;.bak1", ... when that name is taken): a cheap
///   rename on the same volume. Rollback renames it back; commit deletes it. On Windows backups
///   also get the hidden attribute.
/// - A directory that empties is deleted only at commit, since backups are still in it.
///
/// Every change is appended to the journal (&lt;root&gt;/.lip/journal, protocol/workspace.md §7.3;
/// one JSON object per line, its paths relative to &lt;root&gt; and '/'-separated)
/// and flushed to the operating system before it is made, so that a later process can undo the
/// changes of a process that was killed (Recover). Commit appends a "Commit" line; from then on
/// recovery finishes the operation (deleting the backups) instead of undoing it.
public sealed class WorkspaceTransaction : IDisposable {
  private static int s_open;

  private readonly IFileSystem _fs;
  private readonly string _journalPath;
  private readonly string _root;
  private readonly HashSet<string> _reserved;
  private readonly List<JournalEntry> _entries = [];
  private Stream? _journal;
  private string? _started;
  private bool _committed;
  private bool _disposed;

  /// reserved: paths that backups must never take, such as the files the operation writes.
  public WorkspaceTransaction(
      IFileSystem fileSystem, string journalPath, IEnumerable<string>? reserved = null) {
    _fs = fileSystem;
    _journalPath = journalPath;
    _root = JournalRoot(fileSystem, journalPath);
    _reserved = new(reserved ?? [], WorkspaceLayout.PathComparer);
    Interlocked.Increment(ref s_open);
  }

  /// Whether a transaction of this process is open, i.e. an operation is changing a workspace:
  /// ending the process now leaves changes for the next operation to undo.
  public static bool AnyOpen => Volatile.Read(ref s_open) > 0;

  #region Changes

  /// Creates a directory and its missing ancestors.
  public void CreateDirectory(string path) {
    List<string> missing = [];
    for (string? d = path; d is not null && !_fs.Directory.Exists(d);
        d = _fs.Path.GetDirectoryName(d)) {
      missing.Add(d);
    }

    foreach (string directory in Enumerable.Reverse(missing)) {
      Record(new JournalEntry(JournalOp.CreateDirectory, directory));
      _fs.Directory.CreateDirectory(directory);
      RunTrace.Write("workspace", $"Created the directory {Show(directory)}");
    }
  }

  /// Writes a file, creating its directory; an existing file is backed up first.
  public async Task WriteFile(string path, Stream content, CancellationToken cancellationToken) {
    CreateDirectory(_fs.Path.GetDirectoryName(path)!);
    if (_fs.File.Exists(path)) {
      Backup(path);
    }

    Record(new JournalEntry(JournalOp.CreateFile, path));
    await using FileSystemStream output = _fs.File.Create(path);
    await content.CopyToAsync(output, cancellationToken);
    RunTrace.Write("workspace", $"Wrote {Show(path)} ({output.Length} bytes)");
  }

  /// Deletes a file: renames it to a backup, deleted at commit.
  public void DeleteFile(string path) => Backup(path);

  /// Deletes the directory at commit, if it is empty then.
  public void DeleteDirectoryOnCommit(string path) {
    Record(new JournalEntry(JournalOp.DeleteDirectoryOnCommit, path));
    RunTrace.Write(
        "workspace", $"Will delete the directory {Show(path)} at commit if it is empty");
  }

  private void Backup(string path) {
    string backup = BackupPath(path);
    // "hidden" is the Windows hidden attribute (protocol/workspace.md §7.3); elsewhere .NET
    // reports every name starting with '.' as hidden.
    bool wasHidden = OperatingSystem.IsWindows()
        && _fs.File.GetAttributes(path).HasFlag(FileAttributes.Hidden);
    Record(new JournalEntry(JournalOp.Backup, path, backup, wasHidden));
    _fs.File.Move(path, backup);
    RunTrace.Write("workspace", $"Moved {Show(path)} to the backup {Show(backup)}");
    _fs.SetHidden(backup, true);
  }

  /// A path of the workspace as the user knows it.
  private string Show(string path) => WorkspaceErrors.Show(_fs, _root, path);

  /// ".{name}.bak" in the directory of the file, or ".{name}.bak1", ".{name}.bak2", ... when
  /// that name is taken or reserved.
  private string BackupPath(string path) {
    string directory = _fs.Path.GetDirectoryName(path)!;
    string name = $".{_fs.Path.GetFileName(path)}.bak";
    string backup = _fs.Path.Join(directory, name);
    for (int i = 1;
        _fs.File.Exists(backup) || _fs.Directory.Exists(backup) || _reserved.Contains(backup);
        i++) {
      backup = _fs.Path.Join(directory, $"{name}{i}");
    }

    return backup;
  }

  #endregion

  #region Commit and rollback

  /// Marks the transaction committed: from now on, recovery finishes it instead of undoing it.
  /// The caller makes the operation's result durable (the lock) right after this.
  public void Commit() {
    if (_journal is not null) {
      Append(new JournalEntry(JournalOp.Commit, ""));
    }

    _committed = true;
    RunTrace.Write("journal", $"Committed the operation ({_entries.Count} changes)");
  }

  /// After Commit: deletes the backups and the directories to delete at commit, then the
  /// journal. Best effort; returns what could not be done. A backup that cannot be deleted is
  /// left (hidden, and named in the error) rather than kept in the journal, so that it does not
  /// hold up the next operation.
  public IReadOnlyList<Exception> Complete() {
    if (!_committed) {
      throw new InvalidOperationException(
          "lip tried to complete an operation on the workspace that is not committed. This is a"
          + " bug in lip.");
    }

    Finish(_fs, _root, _entries, out List<Exception> errors);
    CloseJournal([], committed: true, errors);
    _entries.Clear();
    RunTrace.Write("journal", $"Completed the operation ({errors.Count} problems)");
    return errors;
  }

  /// Undoes the operation (protocol/workspace.md §7.5): discardLock throws its result away, then
  /// every change is undone in reverse order. Best effort; returns what could not be undone, in
  /// which case the journal keeps those changes so that the next operation tries again. Also
  /// undoes a committed transaction whose result could not be made durable: the journal loses
  /// its "Commit" record first (§7.4), so that recovery undoes it too; when it cannot, nothing is
  /// undone and the journal is left for the next operation to complete.
  public IReadOnlyList<Exception> Rollback(Action discardLock) {
    RunTrace.Write("journal", $"Rolling back the operation ({_entries.Count} changes,"
        + $" {(_committed ? "committed" : "not committed")})");
    List<Exception> errors = [];
    if (_committed && _journal is not null) {
      try {
        _journal.Dispose();
        _journal = null;
        WriteJournal(_fs, _journalPath, _root, _entries, committed: false, _started);
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
        // The journal still commits the operation, and the staged workspace file is kept, so
        // that the next operation completes it rather than finding it partly undone.
        RunTrace.Write("journal", $"Cannot uncommit the journal {_journalPath}: {ex}");
        errors.Add(WorkspaceErrors.Wrap(
            ex,
            $"Could not undo the operation, because the journal '{Show(_journalPath)}' cannot be"
            + $" updated: {WorkspaceErrors.Reason(ex)} The changes are kept, and the next install,"
            + " uninstall or update of this workspace completes the operation."));
        return errors;
      }
    }

    discardLock();
    List<JournalEntry> left = Undo(_fs, _root, _entries, out List<Exception> undoErrors);
    errors.AddRange(undoErrors);
    CloseJournal(left, committed: false, errors);
    _entries.Clear();
    _committed = false;
    RunTrace.Write("journal", $"Rolled back the operation ({left.Count} changes left to undo)");
    return errors;
  }

  /// A transaction disposed of without Complete or Rollback leaves its journal, as a killed
  /// process would.
  public void Dispose() {
    _journal?.Dispose();
    _journal = null;
    if (!_disposed) {
      _disposed = true;
      Interlocked.Decrement(ref s_open);
    }
  }

  #endregion

  #region Journal

  private void Record(JournalEntry entry) {
    Begin();
    Append(entry);
    _entries.Add(entry);
  }

  /// Creates the journal with its "Begin" record unless it exists: before the first change or
  /// hook command of the operation (protocol/workspace.md §7.4 step 5).
  public void Begin() {
    if (_committed) {
      throw new InvalidOperationException(
          "lip tried to change the workspace after committing the operation. This is a bug in"
          + " lip.");
    }

    if (_journal is null) {
      if (_entries.Count > 0) {
        throw new InvalidOperationException(
            "lip tried to change the workspace after the operation ended. This is a bug in lip.");
      }

      // CreateNew: the journal of an interrupted operation is recovered before a new operation
      // starts, and never overwritten.
      try {
        _fs.Directory.CreateDirectory(_fs.Path.GetDirectoryName(_journalPath)!);
        _journal = _fs.FileStream.New(
            _journalPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
        RunTrace.Write("journal", $"Cannot create the journal {_journalPath}: {ex.Message}");
        throw WorkspaceErrors.Wrap(
            ex,
            $"Cannot create the journal '{Show(_journalPath)}', where lip records its changes so"
            + " that they can be undone; lip changed no files.");
      }

      RunTrace.Write("journal", $"Began the journal {_journalPath}");
      _started = DateTimeOffset.UtcNow.ToString("O");
      Append(new JournalEntry(JournalOp.Begin, "", _started));
    }
  }

  private void Append(JournalEntry entry) {
    try {
      _journal!.Write(Encoding.UTF8.GetBytes(Serialize(ToJournal(_fs, _root, entry)) + "\n"));
      // To the operating system, which keeps it when lip is killed.
      _journal.Flush();
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
      RunTrace.Write(
          "journal", $"Cannot append {entry.Op} to the journal {_journalPath}: {ex.Message}");
      throw WorkspaceErrors.Wrap(
          ex,
          $"Cannot write to the journal '{Show(_journalPath)}', where lip records its changes so"
          + " that they can be undone.");
    }
  }

  /// Deletes the journal this transaction wrote when nothing is left, else replaces it with what
  /// is left. Adds a failure to errors rather than throwing: the journal only matters after a
  /// crash, and recovering from an outdated one changes nothing that is already right.
  private void CloseJournal(List<JournalEntry> left, bool committed, List<Exception> errors) {
    // Begin sets _started when it creates the journal, which an operation of only hook commands
    // has too, though it records no change.
    bool written = _started is not null || _journal is not null || _entries.Count > 0;
    _journal?.Dispose();
    _journal = null;
    if (!written) {
      return;
    }

    try {
      if (left.Count == 0) {
        if (_fs.File.Exists(_journalPath)) {
          _fs.File.Delete(_journalPath);
          RunTrace.Write("journal", $"Deleted the journal {_journalPath}");
        }
      } else {
        WriteJournal(_fs, _journalPath, _root, left, committed, _started);
        RunTrace.Write("journal", $"Kept {left.Count} changes left to undo in the journal"
            + $" {_journalPath}");
      }
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
      RunTrace.Write("journal", $"Cannot update the journal {_journalPath}: {ex}");
      errors.Add(WorkspaceErrors.Wrap(
          ex,
          $"Could not update the journal '{Show(_journalPath)}': {WorkspaceErrors.Reason(ex)}"
          + " The next install, uninstall or update of this workspace checks it again."));
    }
  }

  /// Replaces the journal by one with the records of entries. Started: the time the operation
  /// started, as its "Begin" record gave it, or null when unknown.
  private static void WriteJournal(
      IFileSystem fs,
      string path,
      string root,
      List<JournalEntry> entries,
      bool committed,
      string? started) {
    StringBuilder text = new();
    foreach (JournalEntry entry in (List<JournalEntry>)[
        new JournalEntry(JournalOp.Begin, "", started),
        .. entries,
        .. committed ? [new JournalEntry(JournalOp.Commit, "")] : (JournalEntry[])[]]) {
      text.Append(Serialize(ToJournal(fs, root, entry))).Append('\n');
    }

    string temp = path + ".tmp";
    fs.File.WriteAllText(temp, text.ToString());
    fs.File.Move(temp, path, overwrite: true);
  }

  /// The workspace root of a journal at &lt;root&gt;/.lip/journal.
  private static string JournalRoot(IFileSystem fs, string journalPath) =>
      fs.Path.GetDirectoryName(fs.Path.GetDirectoryName(fs.Path.GetFullPath(journalPath))!)!;

  /// Whether the path of an entry, and the backup of a Backup, are workspace paths.
  private static bool HasPaths(JournalOp op) => op is not (JournalOp.Begin or JournalOp.Commit);

  private static bool HasDestination(JournalOp op) => op is JournalOp.Backup;

  /// An entry as the journal records it: its paths relative to the root, '/'-separated.
  private static JournalEntry ToJournal(IFileSystem fs, string root, JournalEntry entry) {
    if (!HasPaths(entry.Op)) {
      return entry;
    }

    // Only the separator becomes '/': outside Windows '\' is a character of a name.
    string Relative(string path) =>
        fs.Path.GetRelativePath(root, path).Replace(fs.Path.DirectorySeparatorChar, '/');
    return entry with {
      Path = Relative(entry.Path),
      To = HasDestination(entry.Op) ? Relative(entry.To!) : entry.To,
    };
  }

  /// An entry read from the journal: its paths made absolute against the root.
  private static JournalEntry FromJournal(IFileSystem fs, string root, JournalEntry entry) {
    if (!HasPaths(entry.Op)) {
      return entry;
    }

    string Absolute(string path) => fs.Path.GetFullPath(fs.Path.Combine(root, path));
    return entry with {
      Path = Absolute(entry.Path),
      To = HasDestination(entry.Op) && entry.To is not null ? Absolute(entry.To) : entry.To,
    };
  }

  /// A record as a line of the journal, with only the members its op has (protocol/workspace.md
  /// §7.3).
  private static string Serialize(JournalEntry entry) {
    JsonObject record = new() { ["op"] = entry.Op.ToString() };
    if (entry.Op != JournalOp.Commit) {
      record["path"] = entry.Path;
    }

    if (HasDestination(entry.Op) || (entry.Op == JournalOp.Begin && entry.To is not null)) {
      record["to"] = entry.To;
    }

    if (entry.Op == JournalOp.Backup) {
      record["hidden"] = entry.Hidden;
    }

    return record.ToJsonString();
  }

  /// A line of the journal as a record, or null when it is none (protocol/workspace.md §7.3): a
  /// JSON object whose op is one of the table, with the members of that op of their types.
  private static JournalEntry? Parse(ReadOnlyMemory<byte> line) {
    try {
      using JsonDocument document = JsonDocument.Parse(line);
      JsonElement root = document.RootElement;
      string? name = root.ValueKind == JsonValueKind.Object
          && root.TryGetProperty("op", out JsonElement op)
          && op.ValueKind == JsonValueKind.String
              ? op.GetString()
              : null;
      // Only the names of the table, not numbers.
      if (name is null || !Enum.GetNames<JournalOp>().Contains(name)) {
        return null;
      }

      JournalOp kind = Enum.Parse<JournalOp>(name);
      if (kind == JournalOp.Commit) {
        return new JournalEntry(kind, "");
      }

      bool hasTo = root.TryGetProperty("to", out JsonElement to);
      bool hasHidden = root.TryGetProperty("hidden", out JsonElement hidden);
      if (!root.TryGetProperty("path", out JsonElement path)
          || path.ValueKind != JsonValueKind.String
          || ((HasDestination(kind) || (kind == JournalOp.Begin && hasTo))
              && (!hasTo || to.ValueKind != JsonValueKind.String))
          || (kind == JournalOp.Backup && hasHidden
              && hidden.ValueKind is not (JsonValueKind.True or JsonValueKind.False))) {
        return null;
      }

      return new JournalEntry(
          kind,
          path.GetString()!,
          hasTo && to.ValueKind == JsonValueKind.String ? to.GetString() : null,
          kind == JournalOp.Backup && hasHidden && hidden.GetBoolean());
    }
    catch (Exception ex) when (ex is JsonException or InvalidOperationException) {
      // Not JSON, or not UTF-8.
      return null;
    }
  }

  /// What the journal of an interrupted operation held, and what was done about it.
  /// Committed: the operation had committed, and its backups were deleted; otherwise its
  /// changes were undone. Started: when the operation started, as recorded.
  public sealed record RecoveryResult(
      bool Committed, string? Started, int Changes, IReadOnlyList<Exception> Errors);

  /// Finishes or undoes the operation a journal left behind by an interrupted process, then
  /// deletes the journal; returns null when there is no journal. commitLock makes the
  /// operation's result durable for a committed operation (it must do nothing when that was
  /// already done); discardLock throws that result away for an uncommitted one. The result
  /// lists what could not be done: a change that cannot be undone stays in the journal for the
  /// next attempt, a backup that cannot be deleted is left.
  public static RecoveryResult? Recover(
      IFileSystem fs, string journalPath, Action commitLock, Action discardLock) {
    if (!fs.File.Exists(journalPath)) {
      return null;
    }

    string root = JournalRoot(fs, journalPath);
    string shown = WorkspaceErrors.Show(fs, root, journalPath);
    List<JournalEntry> entries = [];
    bool committed = false;
    string? started = null;
    byte[] journal;
    try {
      journal = fs.File.ReadAllBytes(journalPath);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
      RunTrace.Write("journal", $"Cannot read the journal {journalPath}: {ex.Message}");
      throw WorkspaceErrors.Wrap(
          ex,
          $"Cannot read the journal '{shown}' of an interrupted lip operation, so lip cannot"
          + " finish or undo that operation. If a program is using the file, close it; check that"
          + " you may read and write .lip; then try again.");
    }

    int ignored = 0;
    // A record is a line ended by LF: what follows the last LF, such as the cut-off last line of
    // a killed process, whose change was not made yet, is none.
    for (int start = 0, end; (end = Array.IndexOf(journal, (byte)'\n', start)) >= 0;
        start = end + 1) {
      JournalEntry? entry = Parse(journal.AsMemory(start, end - start));
      switch (entry?.Op) {
        case null:
          ignored++;
          continue;
        case JournalOp.Begin:
          started ??= entry.To;
          break;
        case JournalOp.Commit:
          committed = true;
          break;
        default:
          entries.Add(FromJournal(fs, root, entry));
          break;
      }
    }

    RunTrace.Write("journal", $"Found the journal {journalPath} of an interrupted operation"
        + $" (started {started ?? "at an unknown time"}): {entries.Count} changes,"
        + $" {(committed ? "committed, finishing it" : "not committed, undoing it")}"
        + (ignored > 0 ? $"; ignored {ignored} unreadable lines" : ""));
    List<Exception> errors;
    List<JournalEntry> left;
    try {
      if (committed) {
        commitLock();
        Finish(fs, root, entries, out errors);
        left = [];
      } else {
        discardLock();
        left = Undo(fs, root, entries, out errors);
      }
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
      RunTrace.Write("journal", $"Cannot recover the interrupted operation: {ex.Message}");
      throw WorkspaceErrors.Wrap(
          ex,
          $"Cannot {(committed ? "finish" : "undo")} an interrupted lip operation on this"
          + $" workspace. The journal '{shown}' is kept, so the next lip command that changes"
          + " the workspace tries again.");
    }

    try {
      if (left.Count == 0) {
        fs.File.Delete(journalPath);
        RunTrace.Write("journal", $"Deleted the journal {journalPath}");
      } else {
        WriteJournal(fs, journalPath, root, left, committed, started);
        RunTrace.Write("journal", $"Kept {left.Count} changes left to undo in the journal"
            + $" {journalPath}");
      }
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
      RunTrace.Write("journal", $"Cannot update the journal {journalPath}: {ex.Message}");
      throw WorkspaceErrors.Wrap(
          ex,
          "An interrupted lip operation on this workspace was"
          + $" {(committed ? "finished" : "undone")}, but its journal '{shown}' cannot be"
          + $" {(left.Count == 0 ? "deleted" : "updated")}. If a program is using the file, close"
          + " it; check that you may write to .lip; then try again.");
    }

    return new RecoveryResult(committed, started, entries.Count, errors);
  }

  /// Undoes the entries in reverse order; returns those that could not be undone, in order.
  /// Every undo checks the state first, so undoing an entry whose change was never made (the
  /// process was killed right after recording it) does nothing, and so does undoing an entry
  /// undone before (a journal left by a process killed while undoing it).
  private static List<JournalEntry> Undo(
      IFileSystem fs, string root, List<JournalEntry> entries, out List<Exception> errors) {
    errors = [];
    List<JournalEntry> left = [];
    Dictionary<string, int> firstBackup = new(WorkspaceLayout.PathComparer);
    for (int i = 0; i < entries.Count; i++) {
      if (entries[i].Op == JournalOp.Backup) {
        firstBackup.TryAdd(entries[i].Path, i);
      }
    }

    for (int i = entries.Count - 1; i >= 0; i--) {
      JournalEntry entry = entries[i];
      // A file created where an earlier change backed one up: once that backup is gone (undone
      // before, or deleted by someone else), the file at the path is not deleted.
      if (entry.Op == JournalOp.CreateFile
          && firstBackup.TryGetValue(entry.Path, out int backup) && backup < i
          && !fs.File.Exists(entries[backup].To!)) {
        continue;
      }

      // A created directory still holding a change that could not be undone is kept for the
      // next attempt. (One holding other files, e.g. written by the server after lip was
      // killed, is left as it is.)
      if (entry.Op == JournalOp.CreateDirectory && left.Any(e => e.Path.StartsWith(
          fs.Path.TrimEndingDirectorySeparator(entry.Path) + fs.Path.DirectorySeparatorChar,
          OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase
              : StringComparison.Ordinal))) {
        left.Insert(0, entry);
        continue;
      }

      try {
        UndoEntry(fs, root, entry);
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
        RunTrace.Write("workspace", $"Cannot undo {entry.Describe(fs, root)}: {ex}");
        errors.Add(WorkspaceErrors.Wrap(
            ex, $"Could not undo {entry.Describe(fs, root)}: {WorkspaceErrors.Reason(ex)}"));
        left.Insert(0, entry);
      }
    }

    return left;
  }

  private static void UndoEntry(IFileSystem fs, string root, JournalEntry entry) {
    string Show(string path) => WorkspaceErrors.Show(fs, root, path);
    switch (entry.Op) {
      case JournalOp.CreateFile:
        if (fs.File.Exists(entry.Path)) {
          ClearReadOnly(fs, entry.Path);
          fs.File.Delete(entry.Path);
          RunTrace.Write("workspace", $"Undo: deleted {Show(entry.Path)}");
        }

        break;
      case JournalOp.Backup:
        if (fs.File.Exists(entry.To!)) {
          if (fs.File.Exists(entry.Path)) {
            ClearReadOnly(fs, entry.Path);
            fs.File.Delete(entry.Path);
          }

          fs.File.Move(entry.To!, entry.Path);
          fs.SetHidden(entry.Path, entry.Hidden);
          RunTrace.Write("workspace", $"Undo: restored {Show(entry.Path)} from {Show(entry.To!)}");
        }

        break;
      case JournalOp.CreateDirectory:
        if (fs.Directory.Exists(entry.Path)
            && !fs.Directory.EnumerateFileSystemEntries(entry.Path).Any()) {
          fs.Directory.Delete(entry.Path);
          RunTrace.Write("workspace", $"Undo: deleted the directory {Show(entry.Path)}");
        }

        break;
      default:
        break;
    }
  }

  /// Deletes the backups, then the directories to delete at commit that are empty, deepest
  /// first. Best effort: what is left behind is harmless.
  private static void Finish(
      IFileSystem fs, string root, List<JournalEntry> entries, out List<Exception> errors) {
    string Show(string path) => WorkspaceErrors.Show(fs, root, path);
    errors = [];
    foreach (JournalEntry entry in entries.Where(e => e.Op == JournalOp.Backup)) {
      try {
        if (fs.File.Exists(entry.To!)) {
          ClearReadOnly(fs, entry.To!);
          fs.File.Delete(entry.To!);
          RunTrace.Write("workspace", $"Deleted the backup {Show(entry.To!)}");
        }
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
        RunTrace.Write("workspace", $"Cannot delete the backup {Show(entry.To!)}: {ex}");
        errors.Add(WorkspaceErrors.Wrap(
            ex,
            $"Could not delete '{Show(entry.To!)}', the backup of the old '{Show(entry.Path)}':"
            + $" {WorkspaceErrors.Reason(ex)} It is no longer needed; delete it yourself."));
      }
    }

    foreach (JournalEntry entry in entries
        .Where(e => e.Op == JournalOp.DeleteDirectoryOnCommit)
        .OrderByDescending(e => e.Path.Length)) {
      try {
        if (fs.Directory.Exists(entry.Path)
            && fs.DirectoryInfo.New(entry.Path).LinkTarget is null
            && !fs.Directory.EnumerateFileSystemEntries(entry.Path).Any()) {
          fs.Directory.Delete(entry.Path);
          RunTrace.Write("workspace", $"Deleted the empty directory {Show(entry.Path)}");
        }
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
        RunTrace.Write("workspace", $"Cannot delete the directory {Show(entry.Path)}: {ex}");
        errors.Add(WorkspaceErrors.Wrap(
            ex,
            $"Could not delete the empty directory '{Show(entry.Path)}':"
            + $" {WorkspaceErrors.Reason(ex)} You may delete it yourself."));
      }
    }
  }

  /// A read-only file cannot be deleted on Windows.
  private static void ClearReadOnly(IFileSystem fs, string path) {
    FileAttributes attributes = fs.File.GetAttributes(path);
    if (attributes.HasFlag(FileAttributes.ReadOnly)) {
      fs.File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
    }
  }

  #endregion

  private enum JournalOp {
    Begin,
    CreateDirectory,
    CreateFile,
    Backup,
    DeleteDirectoryOnCommit,
    Commit,
  }

  /// One record of the journal. To: the backup of a Backup, the start time of a Begin. Hidden: whether the file of a Backup was hidden.
  private sealed record JournalEntry(
      JournalOp Op, string Path, string? To = null, bool Hidden = false) {
    public string Describe(IFileSystem fs, string root) {
      string Show(string path) => WorkspaceErrors.Show(fs, root, path);
      return Op switch {
        JournalOp.CreateFile => $"writing '{Show(Path)}'",
        JournalOp.Backup =>
            $"replacing or deleting '{Show(Path)}' (the original file is kept as"
            + $" '{Show(To!)}'; rename it back to restore it)",
        JournalOp.CreateDirectory => $"creating the directory '{Show(Path)}'",
        _ => $"{Op} '{Show(Path)}'",
      };
    }
  }
}
