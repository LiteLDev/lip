# journal(5)

## NAME

journal - the journal of a workspace, and recovery from it

## SYNOPSIS

`.lip/journal`, in a workspace ([lip-workspace(7)]).

## DESCRIPTION

The **journal** is `.lip/journal`. It is a sequence of **records**, each a [JSON] object written in UTF-8 on one line ended by LF (U+000A). Any other line, including a cut-off last line, is ignored.

The journal is replaced only by writing it atomically.

### Records

Every record has the member `op`. The members `path` and `to` are paths relative to the workspace, `/`-separated; `hidden` is a boolean that defaults to `false`.

| `op` | Members | Records that |
|---|---|---|
| `"Begin"` | `path`: `""`; `to`, optional: the time the operation started, as an [RFC 3339] date-time | The operation started. It is the first record. |
| `"CreateDirectory"` | `path` | The directory `path` was created. |
| `"CreateFile"` | `path` | The file `path` was created. A file that existed at `path` was renamed by an earlier `"Backup"` record. |
| `"Backup"` | `path`, `to`, `hidden` | The file `path` was renamed to `to`, a path in the same directory that named nothing before. `hidden` records whether the file had the Windows hidden attribute. |
| `"DeleteDirectoryOnCommit"` | `path` | The directory `path` is to be deleted when the operation is completed, if it is empty then. |
| `"Commit"` | none | The operation committed ([lip-workspace(7), "Carrying out an operation"](lip-workspace.md#carrying-out-an-operation)). |

Every change an operation makes is one of these records: replacing a file is a `"Backup"` record followed by a `"CreateFile"` record, and deleting a file is a `"Backup"` record. Each record must be appended to the journal, and the journal flushed to the operating system, before the change it records is made.

### Recovery

**Recovering** is completing the operation when the journal has a `"Commit"` record, and undoing it otherwise. The process that recovers must hold the workspace lock.

**Completing** an operation whose journal has a `"Commit"` record:

1. When `.lip/state.json.tmp` exists, rename it to `.lip/state.json`.
2. For each `"Backup"` record, delete the file `to` when it exists.
3. For each `"DeleteDirectoryOnCommit"` record, longest `path` first, delete the directory `path` when it exists, is not a link and is empty.
4. Delete the journal.

A file or directory that step 2 or 3 cannot delete is left in place.

**Undoing** an operation whose journal has no `"Commit"` record:

1. Delete `.lip/state.json.tmp` when it exists.
2. Undo each record from the last to the first:
   - `"CreateFile"`: delete the file `path` when it exists, unless an earlier `"Backup"` record of the same `path` exists whose `to` no longer exists.
   - `"Backup"`: when `to` exists, delete `path` when it exists, rename `to` to `path`, and, on Windows, set the hidden attribute of `path` to `hidden`.
   - `"CreateDirectory"`: delete the directory `path` when it exists and is empty.
   - `"Begin"`, `"DeleteDirectoryOnCommit"`: nothing.
3. When every record was undone, delete the journal. Otherwise replace the journal by one with a `"Begin"` record followed by the records that could not be undone, in their order, and fail. A `"CreateDirectory"` record whose `path` is a directory above the `path` of such a record is not undone, and is kept too.

Undoing a record is checked against the workspace as it is.

## SEE ALSO

[state.json(5)](state.json.md), [lip-workspace(7)]

[lip-workspace(7)]: lip-workspace.md
[JSON]: https://www.rfc-editor.org/info/rfc8259
[RFC 3339]: https://www.rfc-editor.org/info/rfc3339
