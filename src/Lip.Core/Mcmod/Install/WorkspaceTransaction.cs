using System.IO.Abstractions;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lip.Core.Infrastructure;

namespace Lip.Core.Mcmod.Install;

/// Changes to a workspace that can be undone (spec §7): an operation applies all of its file
/// changes through one transaction, and either commits them or rolls all of them back.
///
/// - A created directory or file is recorded, and deleted again on rollback.
/// - A file that is replaced or deleted is first renamed to a hidden backup next to it
///   (".&lt;name&gt;.bak", or ".&lt;name&gt;.bak1", ... when that name is taken): a cheap
///   rename on the same volume. Rollback renames it back; commit deletes it. On Windows backups
///   also get the hidden attribute.
/// - A directory that empties is deleted only at commit, since backups are still in it.
/// - Moves, directory removals and directory links (the workspace layout) are recorded and undone
///   too.
///
/// With a journal path, every change is appended to the journal (one JSON object per line) and
/// flushed to the operating system before it is made, so that a later process can undo the
/// changes of a process that was killed (Recover). Commit appends a "commit" line; from then on
/// recovery finishes the operation (deleting the backups) instead of undoing it.
public sealed class WorkspaceTransaction : IDisposable {
  private static int s_open;

  private readonly IFileSystem _fs;
  private readonly string? _journalPath;
  private readonly HashSet<string> _reserved;
  private readonly List<JournalEntry> _entries = [];
  private Stream? _journal;
  private bool _committed;
  private bool _disposed;

  /// reserved: paths that backups must never take, such as the files the operation writes.
  public WorkspaceTransaction(
      IFileSystem fileSystem, string? journalPath = null, IEnumerable<string>? reserved = null) {
    _fs = fileSystem;
    _journalPath = journalPath;
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
  }

  /// Deletes a file: renames it to a backup, deleted at commit.
  public void DeleteFile(string path) => Backup(path);

  /// Deletes the directory at commit, if it is empty then.
  public void DeleteDirectoryOnCommit(string path) =>
      Record(new JournalEntry(JournalOp.DeleteDirectoryOnCommit, path));

  /// Deletes an empty directory now.
  public void DeleteEmptyDirectory(string path) {
    Record(new JournalEntry(JournalOp.DeleteDirectory, path));
    _fs.Directory.Delete(path);
  }

  /// Moves a file, a directory or a directory link (the link, not its target).
  public void Move(string from, string to) {
    Record(new JournalEntry(JournalOp.Move, from, to));
    MoveAny(_fs, from, to);
  }

  /// Creates a directory link with the linker.
  public void CreateLink(string linkPath, string targetPath, IDirectoryLinker linker) {
    Record(new JournalEntry(JournalOp.CreateLink, linkPath));
    linker.CreateLink(linkPath, targetPath);
  }

  private void Backup(string path) {
    string backup = BackupPath(path);
    bool wasHidden = _fs.File.GetAttributes(path).HasFlag(FileAttributes.Hidden);
    Record(new JournalEntry(JournalOp.Backup, path, backup, wasHidden));
    _fs.File.Move(path, backup);
    _fs.SetHidden(backup, true);
  }

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
    if (_journalPath is not null && _entries.Count > 0) {
      Append(new JournalEntry(JournalOp.Commit, ""));
    }

    _committed = true;
  }

  /// After Commit: deletes the backups and the directories to delete at commit, then the
  /// journal. Best effort; returns what could not be done. A backup that cannot be deleted is
  /// left (hidden, and named in the error) rather than kept in the journal, so that it does not
  /// hold up the next operation.
  public IReadOnlyList<string> Complete() {
    if (!_committed) {
      throw new InvalidOperationException("The transaction is not committed.");
    }

    Finish(_fs, _entries, out List<string> errors);
    CloseJournal([], committed: true, errors);
    _entries.Clear();
    return errors;
  }

  /// Undoes every change in reverse order. Best effort; returns what could not be undone, in
  /// which case the journal keeps those changes so that the next operation tries again. Also
  /// undoes a committed transaction whose result could not be made durable: the journal loses
  /// its "commit" line first, so that recovery undoes it too.
  public IReadOnlyList<string> Rollback() {
    List<string> errors = [];
    if (_committed && _journal is not null) {
      try {
        _journal.Dispose();
        _journal = null;
        WriteJournal(_fs, _journalPath!, _entries, committed: false);
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
        // Undoing is still right; only recovery after a crash during it would finish instead.
        errors.Add($"cannot update the journal {_journalPath}: {ex.Message}");
      }
    }

    List<JournalEntry> left = Undo(_fs, _entries, replay: false, out List<string> undoErrors);
    errors.AddRange(undoErrors);
    CloseJournal(left, committed: false, errors);
    _entries.Clear();
    _committed = false;
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
    if (_committed) {
      throw new InvalidOperationException("The transaction is committed.");
    }

    if (_journalPath is not null) {
      if (_journal is null) {
        if (_entries.Count > 0) {
          throw new InvalidOperationException("The journal is closed.");
        }

        // CreateNew: the journal of an interrupted operation is recovered before a new
        // operation starts, and never overwritten.
        _fs.Directory.CreateDirectory(_fs.Path.GetDirectoryName(_journalPath)!);
        _journal = _fs.FileStream.New(
            _journalPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        Append(new JournalEntry(
            JournalOp.Begin, "", DateTimeOffset.UtcNow.ToString("O"), false));
      }

      Append(entry);
    }

    _entries.Add(entry);
  }

  private void Append(JournalEntry entry) {
    _journal!.Write(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry, s_json) + "\n"));
    // To the operating system, which keeps it when lip is killed.
    _journal.Flush();
  }

  /// Deletes the journal this transaction wrote when nothing is left, else replaces it with what
  /// is left. Adds a failure to errors rather than throwing: the journal only matters after a
  /// crash, and recovering from an outdated one changes nothing that is already right.
  private void CloseJournal(List<JournalEntry> left, bool committed, List<string> errors) {
    bool written = _journal is not null || _entries.Count > 0;
    _journal?.Dispose();
    _journal = null;
    if (_journalPath is null || !written) {
      return;
    }

    try {
      if (left.Count == 0) {
        if (_fs.File.Exists(_journalPath)) {
          _fs.File.Delete(_journalPath);
        }
      } else {
        WriteJournal(_fs, _journalPath, left, committed);
      }
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
      errors.Add($"cannot update the journal {_journalPath}: {ex.Message}");
    }
  }

  private static void WriteJournal(
      IFileSystem fs, string path, List<JournalEntry> entries, bool committed) {
    StringBuilder text = new();
    foreach (JournalEntry entry in (List<JournalEntry>)[
        new JournalEntry(JournalOp.Begin, "", DateTimeOffset.UtcNow.ToString("O"), false),
        .. entries,
        .. committed ? [new JournalEntry(JournalOp.Commit, "")] : (JournalEntry[])[]]) {
      text.Append(JsonSerializer.Serialize(entry, s_json)).Append('\n');
    }

    string temp = path + ".tmp";
    fs.File.WriteAllText(temp, text.ToString());
    fs.File.Move(temp, path, overwrite: true);
  }

  private static readonly JsonSerializerOptions s_json = new() {
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
  };

  /// What the journal of an interrupted operation held, and what was done about it.
  /// Committed: the operation had committed, and its backups were deleted; otherwise its
  /// changes were undone. Started: when the operation started, as recorded.
  public sealed record RecoveryResult(
      bool Committed, string? Started, int Changes, IReadOnlyList<string> Errors);

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

    List<JournalEntry> entries = [];
    bool committed = false;
    string? started = null;
    foreach (string line in fs.File.ReadAllLines(journalPath)) {
      JournalEntry? entry;
      try {
        entry = JsonSerializer.Deserialize<JournalEntry>(line, s_json);
      }
      catch (JsonException) {
        // The last line of a killed process may be cut off; the change it announced was not
        // made yet.
        continue;
      }

      switch (entry?.Op) {
        case null:
          continue;
        case JournalOp.Begin:
          started ??= entry.To;
          break;
        case JournalOp.Commit:
          committed = true;
          break;
        default:
          entries.Add(entry);
          break;
      }
    }

    List<string> errors;
    List<JournalEntry> left;
    if (committed) {
      commitLock();
      Finish(fs, entries, out errors);
      left = [];
    } else {
      discardLock();
      left = Undo(fs, entries, replay: true, out errors);
    }

    if (left.Count == 0) {
      fs.File.Delete(journalPath);
    } else {
      WriteJournal(fs, journalPath, left, committed);
    }

    return new RecoveryResult(committed, started, entries.Count, errors);
  }

  /// Undoes the entries in reverse order; returns those that could not be undone, in order.
  /// Every undo checks the state first, so undoing an entry whose change was never made (the
  /// process was killed right after recording it) does nothing. With replay, the entries may
  /// have been undone before (a journal left by a process killed while undoing it).
  private static List<JournalEntry> Undo(
      IFileSystem fs, List<JournalEntry> entries, bool replay, out List<string> errors) {
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
      // A file created where an earlier change backed one up: once that backup is gone, the
      // original is back at the path (undone before), and it is not to be deleted.
      if (replay && entry.Op == JournalOp.CreateFile
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
        UndoEntry(fs, entry);
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
        errors.Add($"cannot undo {entry.Describe()}: {ex.Message}");
        left.Insert(0, entry);
      }
    }

    return left;
  }

  private static void UndoEntry(IFileSystem fs, JournalEntry entry) {
    switch (entry.Op) {
      case JournalOp.CreateFile:
        if (fs.File.Exists(entry.Path)) {
          ClearReadOnly(fs, entry.Path);
          fs.File.Delete(entry.Path);
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
        }

        break;
      case JournalOp.CreateDirectory:
        if (fs.Directory.Exists(entry.Path)
            && !fs.Directory.EnumerateFileSystemEntries(entry.Path).Any()) {
          fs.Directory.Delete(entry.Path);
        }

        break;
      case JournalOp.DeleteDirectory:
        if (!fs.Directory.Exists(entry.Path)) {
          fs.Directory.CreateDirectory(entry.Path);
        }

        break;
      case JournalOp.Move:
        if (Exists(fs, entry.To!) && !Exists(fs, entry.Path)) {
          MoveAny(fs, entry.To!, entry.Path);
        }

        break;
      case JournalOp.CreateLink:
        if (fs.DirectoryInfo.New(entry.Path).LinkTarget is not null) {
          // Deletes the link, not its target.
          fs.Directory.Delete(entry.Path);
        }

        break;
      default:
        break;
    }
  }

  /// Deletes the backups, then the directories to delete at commit that are empty, deepest
  /// first. Best effort: what is left behind is harmless.
  private static void Finish(
      IFileSystem fs, List<JournalEntry> entries, out List<string> errors) {
    errors = [];
    foreach (JournalEntry entry in entries.Where(e => e.Op == JournalOp.Backup)) {
      try {
        if (fs.File.Exists(entry.To!)) {
          ClearReadOnly(fs, entry.To!);
          fs.File.Delete(entry.To!);
        }
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
        errors.Add($"cannot delete the backup {entry.To}, delete it yourself: {ex.Message}");
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
        }
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
        errors.Add($"cannot delete the empty directory {entry.Path}: {ex.Message}");
      }
    }
  }

  private static bool Exists(IFileSystem fs, string path) =>
      fs.File.Exists(path) || fs.Directory.Exists(path)
      || fs.DirectoryInfo.New(path).LinkTarget is not null;

  private static void MoveAny(IFileSystem fs, string from, string to) {
    if (fs.File.Exists(from)) {
      fs.File.Move(from, to);
    } else {
      fs.Directory.Move(from, to);
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
    Move,
    DeleteDirectory,
    DeleteDirectoryOnCommit,
    CreateLink,
    Commit,
  }

  /// One line of the journal. To: the backup of a Backup, the destination of a Move, the start
  /// time of a Begin. Hidden: whether the file of a Backup was hidden.
  private sealed record JournalEntry(
      [property: JsonPropertyName("op")]
      [property: JsonConverter(typeof(JsonStringEnumConverter<JournalOp>))]
      JournalOp Op,
      [property: JsonPropertyName("path")] string Path,
      [property: JsonPropertyName("to")] string? To = null,
      [property: JsonPropertyName("hidden")] bool Hidden = false) {
    public string Describe() => Op switch {
      JournalOp.CreateFile => $"the creation of {Path}",
      JournalOp.Backup => $"the change of {Path} (the original is {To})",
      JournalOp.CreateDirectory => $"the creation of {Path}",
      JournalOp.DeleteDirectory => $"the deletion of {Path}",
      JournalOp.Move => $"the move of {Path} to {To}",
      JournalOp.CreateLink => $"the link {Path}",
      _ => $"{Op} {Path}",
    };
  }
}
