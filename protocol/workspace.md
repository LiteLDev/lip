# Workspace

## 1. Terminology

- **workspace**: A directory into which packages are installed. Paths in this document are relative to it and `/`-separated, unless said to be absolute.
- **package**: In this document, a candidate (Section 4 of [PACKAGE]); its **entry** is its selected entry.
- **mod**: A package that is a mod (Section 4 of [PACKAGE]). Its **mod directory** is `mods/<mod name>/`.
- **file package**: A package that is not a mod.
- **identity**: The identity of a package (Section 4 of [PACKAGE]).
- **source of a package**: The source of the request or the key of the dependency that it was resolved for.
- **operation**: Installing, uninstalling or updating packages in a workspace as one unit, including every package it installs, replaces or uninstalls as a dependency.
- **request**: A pair (source, range) given to an operation.
- **failure**: A condition that aborts the operation in progress.
- **change**: Creating, replacing or deleting a file or a directory in the workspace, other than below `.lip`.
- **hook command**: A string of `hooks`, run by the operating system shell (`cmd /c` on Windows, `sh -c` elsewhere) with the workspace as working directory; a non-zero exit code is a failure. Running a member of `hooks` runs its hook commands in order.

## 2. Environment and Layout

A workspace has one environment, recorded in its workspace file; every operation on it resolves sources for that environment. How a workspace without a recorded environment gets one is not defined here.

`.lip` at the workspace root is the directory of Section 7.

**Layout.** Mod directories are below `mods` at the workspace root, the game directory LeviLamina loads mods from. Before an operation changes the workspace, it creates `mods` as a directory when it is missing; when `mods` is a link, it is a failure.

## 3. Valid States

A package whose source is a remote repository `r#v@x`, `x` a ref, **stands in** for every candidate of `r#v` (same `r` and `v`, no ref) for a range its version satisfies. A source with a ref is satisfied only by its own candidates.

The set S of packages installed in a workspace, given the requests that made its explicitly installed packages, MUST satisfy:

1. For every request (s, range), S contains a candidate of Resolve(s, range, environment), or a package that stands in for one.
2. For every dependency (k, R) of every package in S, S contains a candidate of Resolve(k, R, environment), or a package that stands in for one.
3. No two packages in S have the same identity, and no two mods in S have the same mod name, compared ASCII case-insensitively.

An operation that cannot end in such an S is a failure. When several such S exist, packages earlier in the sequences of candidates SHOULD be preferred.

## 4. Installing and Uninstalling a Package

A package **owns** every file it writes to the workspace by the steps below.

**Installing** a package P is, in order:

1. Run `hooks.pre_install`.
2. Write the files of each asset of the entry, in order. The archive of an asset is the archive of P for `"self"`, and the ZIP archive downloaded over HTTPS from the first of its `urls` that can be obtained otherwise; when none can, it is a failure.
   - `"self"` and `"zip"`: every member of `place` is carried out, in order, its `src` a path in the archive of the asset:
     - File form: the file `src` is written to `dest`; without that file, it is a failure.
     - Directory form: every file below the directory `src` is written into `dest`, at its path relative to `src`; without a file below `src`, it is a failure.
     - Glob form: every file whose path matches the glob `src` is written into `dest`, at its path relative to the leading segments of `src` containing none of `*`, `?` and `[`. A glob matching no file writes nothing.
     - The package file of P is never written.
   - `"mcmod"`: every file of the archive of the asset is written into the mod directory, at its path in the archive.

   When two members of `place` or two assets write the same path, the later one wins.
3. Run `hooks.post_install`.

**Write rules.** When a file is written to the workspace:

- When it is `.lip` or below it, or is owned by another installed package, it is a failure.
- On Windows, when a segment of its path other than `.` contains `:` or ends in `.` or a space, it is a failure.
- When it exists and matches a glob of the `preserve` of P, the existing file is kept.
- Otherwise it is written, replacing an existing one.

The globs of `preserve` and `purge` are matched against paths relative to the workspace.

**Uninstalling** a package P is, in order:

1. Run `hooks.pre_uninstall`.
2. Delete each file owned by P that matches no glob of its `preserve`.
3. Delete each path that matches a glob of its `purge` and is not owned by another package; a directory is deleted with everything below it. `mods` and `.lip` at the workspace root, and everything below `.lip`, never match, and a path below `mods` does not match a glob only because `mods` does.
4. Run `hooks.post_uninstall`.

Each directory that steps 2 and 3 left empty, except `mods`, is deleted when the operation completes, if it is empty then (Section 7.5).

**Updating** a package uninstalls the old package, then installs the new one.

## 5. Operations

**Order.** Within an operation, a package is installed after the packages that satisfy its dependencies; the packages of a cycle of dependencies are installed in ascending order of their identities with `A`–`Z` replaced by `a`–`z`. This is the **installation order**.

Updating an explicitly installed package makes a request of its source again:

- a remote repository without a ref gives its highest version tag;
- `@<version>` and `rev:` give the same commit;
- `branch:` and `tag:` give the commit the branch or tag points to now;
- a URL is downloaded again;
- a local path is read again.

The package is replaced only when the package obtained differs in identity, version or commit.

**Reinstalling.** Installing a package again from the workspace file installs it with the package file the workspace file records: for a remote repository, from the commit it records; otherwise from the archive its source names now.

## 6. Atomicity

An operation succeeds or fails as a whole.

1. **Early failures change nothing.** When an operation fails for a reason that does not depend on changing the workspace (Section 3, an archive or asset that cannot be obtained, a missing `src`, a file owned by another package, a mod name that cannot be read, a `mods` that is a link), no hook command of it runs, no change is made, and the workspace file does not change. Each step is checked against the workspace as the steps before it leave it.
2. **Later failures are undone.** When a step fails while the workspace is being changed (a file cannot be written or deleted, a hook command fails, the operation is cancelled), every change the operation made is undone (Section 7.5). The workspace file does not change.
3. **Hook commands are not undone.** Hook commands that already ran are neither run again nor reversed. They SHOULD be safe to run again, and SHOULD NOT assume that the files of the operation stay in place.
4. The paths that uninstallation steps 2 and 3 delete are those that match when the step is carried out, including paths that hook commands of the operation created.
5. **Interruption.** When an operation is interrupted while changing the workspace, the next operation on the workspace first recovers it (Section 7.5).

## 7. The `.lip` Directory

`.lip` holds `workspace.yaml` (Section 7.1), `workspace.yaml.tmp` (Section 7.4), `lock` (Section 7.2) and `journal` (Section 7.3). On Windows it SHOULD have the hidden attribute. Its other entries are not defined here, and an implementation MUST NOT read meaning into them.

Creating `.lip` and `.lip/lock` is not a change, and an operation that fails early (Section 6 rule 1) MAY leave them.

`.lip/logs` holds failure reports (Section 2 of [CLI]). Creating it and writing or deleting below it is not a change, and any process MAY do so at any time.

### 7.1. Workspace File

The **workspace file** is the YAML file `.lip/workspace.yaml`, valid against `workspace.schema.yaml`. A workspace without a workspace file has no installed packages and no recorded environment, unless Section 3 of [COMPATIBILITY] reads one for it.

The workspace file is only written atomically, through `.lip/workspace.yaml.tmp` (Section 7.4), so a process reading it without the workspace lock reads either the file before an operation or the file after it.

It records the environment (`platform` and `runtime`), and for each installed package, in installation order:

- its package file (`manifest`), as Section 4 of [PACKAGE] read it;
- the variant of its source (`variant`);
- whether a request named it (`explicit`): such a package is **explicitly installed**; any other is a **dependency package**;
- its source (`ref`), a local path recorded as the `file:` URI [RFC8089] of its absolute path;
- for a remote repository, the commit its archive was at (`commit`), in lowercase hexadecimal;
- the paths of the files it owns (`files`).

### 7.2. Workspace Lock

The **workspace lock** is held on `.lip/lock`. To take it, a process creates `.lip` and the empty file `.lip/lock` when they do not exist, opens `.lip/lock`, and keeps it open with a share mode of 0 [CREATEFILE] on Windows, or takes an exclusive `flock` lock (`LOCK_EX`) [FLOCK] on it elsewhere. When this fails because another process has the file open with a share mode of 0 or holds a `flock` lock on it, another process holds the workspace lock. Closing the file releases the lock.

`.lip/lock` MUST NOT be deleted, renamed or replaced while the workspace lock is held. Its content is not defined here.

A process that only reads the workspace file need not take the workspace lock.

### 7.3. Journal

The **journal** is `.lip/journal`. It is a sequence of **records**, each a JSON [RFC8259] object written in UTF-8 on one line ended by LF (U+000A). Any other line, including a cut-off last line, is ignored.

Every record has the member `op`. The members `path` and `to` are paths relative to the workspace; `hidden` is a boolean that defaults to `false`.

| `op` | Members | Records that |
|---|---|---|
| `"Begin"` | `path`: `""`; `to`, OPTIONAL: the time the operation started, as an RFC 3339 [RFC3339] date-time | The operation started. It is the first record. |
| `"CreateDirectory"` | `path` | The directory `path` was created. |
| `"CreateFile"` | `path` | The file `path` was created. A file that existed at `path` was renamed by an earlier `"Backup"` record. |
| `"Backup"` | `path`, `to`, `hidden` | The file `path` was renamed to `to`, a path in the same directory that named nothing before. `hidden` records whether the file had the Windows hidden attribute. |
| `"DeleteDirectoryOnCommit"` | `path` | The directory `path` is to be deleted when the operation is completed, if it is empty then. |
| `"Commit"` | none | The operation committed (Section 7.4). |

Every change an operation makes is one of these records: replacing a file is a `"Backup"` record followed by a `"CreateFile"` record, and deleting a file is a `"Backup"` record. Each record MUST be appended to the journal, and the journal flushed to the operating system, before the change it records is made.

The journal is replaced only by writing it atomically.

### 7.4. Carrying Out an Operation

An operation does, in order:

1. Take the workspace lock. When another process holds it, it is a failure.
2. When `.lip/journal` exists, recover (Section 7.5).
3. Read the workspace file, and decide what the operation does (Sections 3 to 5). A failure here is an early failure (Section 6 rule 1).
4. When the operation makes no change and runs no hook command, write the new workspace file to `.lip/workspace.yaml.tmp`, rename it to `.lip/workspace.yaml`, and go to step 10.
5. Create `.lip/journal` with a `"Begin"` record. A journal that already exists is never overwritten.
6. Make the changes and run the hook commands (Sections 2 and 4), each change after its record. When a step fails or the operation is cancelled, undo the operation (Section 7.5) and fail.
7. Write the new workspace file to `.lip/workspace.yaml.tmp`.
8. Append a `"Commit"` record to the journal, and flush it to the operating system.
9. Rename `.lip/workspace.yaml.tmp` to `.lip/workspace.yaml`, then complete the operation (Section 7.5).
10. Release the workspace lock.

When step 9 fails to rename the file, the implementation MUST replace the journal by one without the `"Commit"` record before it undoes the operation.

### 7.5. Recovery

**Completing** an operation whose journal has a `"Commit"` record:

1. When `.lip/workspace.yaml.tmp` exists, rename it to `.lip/workspace.yaml`.
2. For each `"Backup"` record, delete the file `to` when it exists.
3. For each `"DeleteDirectoryOnCommit"` record, longest `path` first, delete the directory `path` when it exists, is not a link and is empty.
4. Delete the journal.

A file or directory that step 2 or 3 cannot delete is left in place.

**Undoing** an operation whose journal has no `"Commit"` record:

1. Delete `.lip/workspace.yaml.tmp` when it exists.
2. Undo each record from the last to the first:
   - `"CreateFile"`: delete the file `path` when it exists, unless an earlier `"Backup"` record of the same `path` exists whose `to` no longer exists.
   - `"Backup"`: when `to` exists, delete `path` when it exists, rename `to` to `path`, and, on Windows, set the hidden attribute of `path` to `hidden`.
   - `"CreateDirectory"`: delete the directory `path` when it exists and is empty.
   - `"Begin"`, `"DeleteDirectoryOnCommit"`: nothing.
3. When every record was undone, delete the journal. Otherwise replace the journal by one with a `"Begin"` record followed by the records that could not be undone, in their order, and fail. A `"CreateDirectory"` record whose `path` is a directory above the `path` of such a record is not undone, and is kept too.

Undoing a record is checked against the workspace as it is.

**Recovering** is completing the operation when the journal has a `"Commit"` record, and undoing it otherwise. The process that recovers MUST hold the workspace lock.
