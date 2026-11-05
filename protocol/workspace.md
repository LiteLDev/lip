# Workspace

## 1. Terminology

- **workspace**: A directory into which packages are installed. Paths in this document are relative to it and `/`-separated, unless said to be absolute.
- **mod**: A concrete package whose manifest has the key `mod`.
- **file package**: A concrete package that is not a mod.
- **identity**: The `id` of the manifest of a concrete package.
- **operation**: Installing, uninstalling or updating packages in a workspace as one unit, including every package it installs, replaces or uninstalls as a dependency.
- **request**: A pair (source, range) given to an operation.
- **failure**: A condition that aborts the operation in progress.
- **change**: Creating, replacing, moving or deleting a file, a directory or a link in the workspace, other than below `.lip`.
- **hook command**: A string of `hooks`, run by the operating system shell (`cmd /c` on Windows, `sh -c` elsewhere) with the workspace as working directory; a non-zero exit code is a failure. Running a member of `hooks` runs its hook commands in order.

## 2. Environment and Layout

A workspace has one environment, recorded in its workspace file; every operation on it resolves sources for that environment. How a workspace without a recorded environment gets one is not defined here.

`.lip` at the workspace root is the directory of Section 7.

**Server layout.** In a workspace whose platform is `"server"`, mods are installed below `mods`, and `plugins` at the root MUST be a link to `mods`: a directory junction on Windows, a relative symbolic link elsewhere. A path through `plugins/` is compared, and recorded in the workspace file, in its `mods/` form. Before an operation changes the workspace, it establishes the layout:

- `mods` missing: it is created as a directory.
- `plugins` missing: it is created as a link to `mods`.
- `plugins` a link to `mods`: nothing changes.
- `plugins` a file or a link to anything else: failure.
- `plugins` a directory: its entries are moved into `mods`, then `plugins` is replaced by a link to `mods`. When an entry has the name of an entry of `mods`, it is a failure, before any entry is moved.

## 3. Valid States

The **install edges** of a concrete package are the members of its `dependencies` whose value has `source`; the others are ignored in this document.

A concrete package whose chain starts with a remote repository `r#v@x`, `x` a ref, **stands in** for every candidate of `r#v` (same `r` and `v`, no ref) for a range its version satisfies. A source with a ref is satisfied only by its own candidates.

The set S of packages installed in a workspace, given the requests that made its explicitly installed packages, MUST satisfy:

1. For every request (s, range), S contains a candidate of Resolve(s, none, range, environment), or a package that stands in for one.
2. For every install edge (key k, value d) of every package P in S, S contains a package Q whose identity equals k and that, for some source s of the `source` of d, is a candidate of Resolve(s, the archive of P, the `version` of d, environment), or stands in for one.
3. No two packages in S have the same identity.
4. For every member (key k, range R) of the `conflicts` of every package in S, S contains no package whose identity equals k and whose version satisfies R.

An operation that cannot end in such an S is a failure. When several such S exist, packages earlier in the sequences of candidates SHOULD be preferred.

## 4. Installing and Uninstalling a Package

A package **owns** every file it writes to the workspace by the steps below.

**Installing** a package P is, in order:

1. Run `hooks.preInstall`.
2. Write the files:
   - Mod: every file of the archive of P is written to `mods/<id>/`, `<id>` the identity of P.
   - File package: every entry of `files` is carried out, in order, its `src` a path in the archive of P:
     - File form: the file `src` is written to `dest`; without that file, it is a failure.
     - Directory form: every file below the directory `src` is written into `dest`, at its path relative to `src`; without a file below `src`, it is a failure.
     - Glob form: every file whose path matches the glob `src` is written into `dest`, at its path relative to the leading segments of `src` containing none of `*`, `?` and `[`. A glob matching no file writes nothing.
     - The manifest of P is never written.
     - When two entries write the same path, the later one wins.
3. Run `hooks.postInstall`.

**Write rules.** When a file is written to the workspace:

- When it is `.lip` or below it, or is owned by another installed package, it is a failure.
- When it exists and matches a glob of the `preserve` of P, the existing file is kept.
- Otherwise it is written, replacing an existing one.

The globs of `preserve` and `remove` are matched against paths relative to `mods/<id>/` for a mod, and to the workspace for a file package.

**Uninstalling** a package P is, in order:

1. Run `hooks.preUninstall`.
2. Delete each file owned by P that matches no glob of its `preserve`.
3. Delete each path that matches a glob of its `remove` and is not owned by another package; a directory is deleted with everything below it. `mods`, `plugins` and `.lip` at the workspace root never match, and nothing below `mods` or `plugins` is deleted through them; nothing below `.lip` matches.
4. Delete each directory that steps 2 and 3 left empty, except `mods` and `plugins`.
5. Run `hooks.postUninstall`.

**Updating** a package uninstalls the old package, then installs the new one.

## 5. Operations

**Order.** Within an operation, a package is installed after the packages that satisfy its install edges; the packages of a cycle of install edges are installed in ascending order of their identities with `A`–`Z` replaced by `a`–`z`. This is the **installation order**.

Updating an explicitly installed package makes a request of the first source of its chain again:

- a remote repository without a ref gives its highest version tag;
- `@<version>` and `rev:` give the same commit;
- `branch:` and `tag:` give the commit the branch or tag points to now;
- a URL is downloaded again;
- a local path is read again.

The package is replaced only when the package obtained differs in identity, version, archive name or archive identifier.

**Reinstalling.** Installing a package again from the workspace file installs the archive the workspace file records, whatever the sources of its chain name now, and is a failure when its archive identifier differs.

## 6. Atomicity

An operation succeeds or fails as a whole.

1. **Early failures change nothing.** When an operation fails for a reason that does not depend on changing the workspace (Section 3, an archive that cannot be obtained, a missing `src`, a file owned by another package, a server layout that cannot be established), no hook command of it runs, no change is made, and the workspace file does not change. Each step is checked against the workspace as the steps before it leave it.
2. **Later failures are undone.** When a step fails while the workspace is being changed (a file cannot be written or deleted, a hook command fails, the operation is cancelled), every change the operation made, including to directories and the server layout, is undone (Section 7.5). The workspace file does not change.
3. **Hook commands are not undone.** Hook commands that already ran are neither run again nor reversed. They SHOULD be safe to run again, and SHOULD NOT assume that the files of the operation stay in place.
4. The paths that uninstallation steps 2–4 delete are those that match before any hook command of the operation runs.
5. **Interruption.** When an operation is interrupted while changing the workspace, the next operation on the workspace first recovers it (Section 7.5).

## 7. The `.lip` Directory

`.lip` holds `workspace.json` (Section 7.1), `workspace.json.new` (Section 7.4), `lock` (Section 7.2) and `journal` (Section 7.3). On Windows it SHOULD have the hidden attribute. Its other entries are not defined here, and an implementation MUST NOT read meaning into them.

Creating `.lip` and `.lip/lock` is not a change, and an operation that fails early (Section 6 rule 1) MAY leave them.

### 7.1. Workspace File

The **workspace file** is `.lip/workspace.json`, valid against `workspace.schema.json`. A workspace without a workspace file and without the files of Section 7.6 has no installed packages and no recorded environment.

The workspace file is replaced only as a whole, by renaming `.lip/workspace.json.new` over it (Section 7.4), so a process reading it without the workspace lock reads either the file before an operation or the file after it.

It records the environment (`platform` and `runtime`, each `null` until known), and for each installed package:

- its identity (`id`) and version (`version`);
- its kind (`kind`): `"mcmod"` for a package of [PACKAGE];
- whether a request named it (`explicit`): such a package is **explicitly installed**; any other is a **dependency package**;
- its chain (`chain`): for each source followed, in order, the source (`ref`), the index in `targets` of the target selected in the meta package reached there, or `null` when a concrete package was reached (`target`), and, for a remote repository, the commit its archive was at (`commit`). A local path of a request is recorded as the `file:` URI [RFC8089] of its absolute path;
- its archive name (`archiveKey`) and archive identifier (`archive`);
- its manifest (`manifest`);
- the paths of the files it owns (`files`), in the `mods/` form.

The **archive identifier** of a package identifies the content of its archive:

| Archive | `archive` |
|---|---|
| A ZIP archive that is a file | `{"kind": "zip", "sha256": <SHA-256 of the file>, "url": <the URL it was downloaded from, without its fragment; the file: URI of its absolute path, for a local file; null, for a file inside another archive>}` |
| A directory inside a ZIP archive | The identifier of that ZIP archive, with `"path"`: the path of the directory in it, without a leading or trailing `/` |
| A git archive, or a directory inside one | `{"kind": "git", "repo": "https://<repo>.git", "commit": <the commit id>, "path": <the path of the directory in the commit, without a leading or trailing "/"; "" for the root>}` |
| A directory archive on the local file system, or a directory inside one | `{"kind": "directory", "path": <its absolute path>}` |

SHA-256 values and commit ids are written in lowercase hexadecimal. A ZIP archive that is a file inside another archive is obtained again through the chain.

The **archive name** of a package names where its archive was obtained:

| Archive | Archive name |
|---|---|
| A git archive selected through a version tag, with no ref or the ref `<version>` | `<repo>@<version>` |
| Any other git archive | `<repo>@rev:<commit>`, the commit id in lowercase |
| The archive of a URL | The URL, without its fragment |
| The archive of a local file or local directory | The `file:` URI of its absolute path |
| A file or directory inside another archive | The archive name of that archive, then `!`, then the local path that names it without its leading `./` |

`<repo>` and `<version>` are as written in the source that selected the archive, after templates are replaced (Section 3 of [PACKAGE]).

Entries whose `kind` is not `"mcmod"` record packages of other formats. An implementation that does not know such a `kind` MUST NOT install, uninstall or update that package, and MUST keep its entry unchanged.

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
| `"Move"` | `path`, `to` | The file, directory or link `path` was moved to `to`. |
| `"DeleteDirectory"` | `path` | The empty directory `path` was deleted. |
| `"DeleteDirectoryOnCommit"` | `path` | The directory `path` is to be deleted when the operation is completed, if it is empty then. |
| `"CreateLink"` | `path` | The link `path` was created (Section 2). |
| `"Commit"` | none | The operation committed (Section 7.4). |

Every change an operation makes is one of these records: replacing a file is a `"Backup"` record followed by a `"CreateFile"` record, and deleting a file is a `"Backup"` record. Each record MUST be appended to the journal, and the journal flushed to the operating system, before the change it records is made.

The journal is replaced as a whole only by writing its new content to another file in `.lip` and renaming that file over `.lip/journal`.

### 7.4. Carrying Out an Operation

An operation does, in order:

1. Take the workspace lock. When another process holds it, it is a failure.
2. When `.lip/journal` exists, recover (Section 7.5).
3. Read the workspace file, and decide what the operation does (Sections 3 to 5). A failure here is an early failure (Section 6 rule 1).
4. When the operation makes no change, write the new workspace file to `.lip/workspace.json.new`, rename it over `.lip/workspace.json`, and go to step 10.
5. Create `.lip/journal` with a `"Begin"` record. A journal that already exists is never overwritten.
6. Make the changes and run the hook commands (Sections 2 and 4), each change after its record. When a step fails or the operation is cancelled, undo the operation (Section 7.5) and fail.
7. Write the new workspace file to `.lip/workspace.json.new`.
8. Append a `"Commit"` record to the journal, and flush it to the operating system.
9. Rename `.lip/workspace.json.new` over `.lip/workspace.json`, then complete the operation (Section 7.5).
10. Release the workspace lock.

When step 9 fails to rename the file, the implementation MUST replace the journal by one without the `"Commit"` record before it undoes the operation.

### 7.5. Recovery

**Completing** an operation whose journal has a `"Commit"` record:

1. When `.lip/workspace.json.new` exists, rename it over `.lip/workspace.json`.
2. For each `"Backup"` record, delete the file `to` when it exists.
3. For each `"DeleteDirectoryOnCommit"` record, longest `path` first, delete the directory `path` when it exists, is not a link and is empty.
4. Delete the journal.

A file or directory that step 2 or 3 cannot delete is left in place.

**Undoing** an operation whose journal has no `"Commit"` record:

1. Delete `.lip/workspace.json.new` when it exists.
2. Undo each record from the last to the first:
   - `"CreateFile"`: delete the file `path` when it exists, unless an earlier `"Backup"` record of the same `path` exists whose `to` no longer exists.
   - `"Backup"`: when `to` exists, delete `path` when it exists, rename `to` to `path`, and, on Windows, set the hidden attribute of `path` to `hidden`.
   - `"CreateDirectory"`: delete the directory `path` when it exists and is empty.
   - `"DeleteDirectory"`: create the directory `path` when it does not exist.
   - `"Move"`: when `to` exists and `path` does not, move `to` back to `path`.
   - `"CreateLink"`: when `path` is a link, delete the link.
   - `"Begin"`, `"DeleteDirectoryOnCommit"`: nothing.
3. When every record was undone, delete the journal. Otherwise replace the journal by one with a `"Begin"` record followed by the records that could not be undone, in their order, and fail. A `"CreateDirectory"` record whose `path` is a directory above the `path` of such a record is not undone, and is kept too.

Undoing a record is checked against the workspace as it is.

**Recovering** is completing the operation when the journal has a `"Commit"` record, and undoing it otherwise. The process that recovers MUST hold the workspace lock.

### 7.6. Earlier Formats

- When `.lip/workspace.json` does not exist and `.lip/lock.json` does, `.lip/lock.json` is read as the workspace file; it has the same format. The next operation writes `.lip/workspace.json` and then deletes `.lip/lock.json`.
- When neither exists, how a `tooth_lock.json` at the root of the workspace is taken over is not defined here.
