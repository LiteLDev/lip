# lip Workspace Protocol

## Abstract

This document defines workspaces: which packages a workspace may hold, what installing and uninstalling a package does to it and in what order, and the files in the directory `.lip` of the workspace that record its packages, keep two operations apart and let an interrupted operation be recovered.

## Status of This Memo

This document is part of the lip protocol and is normative. The format of the workspace file is defined by the JSON Schema [`workspace.schema.json`](workspace.schema.json).

## Table of Contents

1. [Introduction](#1-introduction)
   1. [Requirements Language](#11-requirements-language)
2. [Terminology](#2-terminology)
3. [Environment and Layout](#3-environment-and-layout)
4. [Valid States](#4-valid-states)
5. [Installing and Uninstalling a Package](#5-installing-and-uninstalling-a-package)
6. [Operations](#6-operations)
7. [Atomicity](#7-atomicity)
8. [The `.lip` Directory](#8-the-lip-directory)
   1. [Workspace File](#81-workspace-file)
   2. [Workspace Lock](#82-workspace-lock)
   3. [Journal](#83-journal)
   4. [Carrying Out an Operation](#84-carrying-out-an-operation)
   5. [Recovery](#85-recovery)
   6. [Earlier Formats](#86-earlier-formats)
9. [Security Considerations](#9-security-considerations)
10. [IANA Considerations](#10-iana-considerations)
11. [References](#11-references)
    1. [Normative References](#111-normative-references)
    2. [Informative References](#112-informative-references)

## 1. Introduction

An operation changes the set of packages installed in a workspace from one valid state (Section 4) to another, as a whole (Section 7), and records the result in the workspace file (Section 8.1). While it runs it holds the workspace lock (Section 8.2), and it records each change in the journal (Section 8.3) before making it, so that the next operation can complete or undo it when it is interrupted (Section 8.5).

### 1.1. Requirements Language

The key words "MUST", "MUST NOT", "REQUIRED", "SHALL", "SHALL NOT", "SHOULD", "SHOULD NOT", "RECOMMENDED", "NOT RECOMMENDED", "MAY", and "OPTIONAL" in this document are to be interpreted as described in BCP 14 [RFC2119] [RFC8174] when, and only when, they appear in all capitals, as shown here.

## 2. Terminology

Terms not defined in this document are defined in [PACKAGE].

- **workspace**: A directory into which packages are installed. Paths in this document are relative to it and separated by `/`, unless they are said to be absolute.
- **mod**: A concrete package whose manifest has the key `mod`. It is loaded by LeviLamina [LEVILAMINA].
- **file package**: A concrete package that is not a mod.
- **identity**: The `id` of the manifest of a concrete package. Identities are compared ASCII case-insensitively.
- **operation**: One change to the set of packages installed in a workspace (installing, uninstalling or updating packages), including every package it installs, replaces or uninstalls as a dependency.
- **request**: A pair (source, range) given to an operation. A request is inside no archive, so a local path in it names a file or directory on the local file system.
- **failure**: A condition that aborts the operation in progress. Section 7 defines what is left of its changes.
- **change**: Creating, replacing, moving or deleting a file, a directory or a link in the workspace, other than below `.lip`.

## 3. Environment and Layout

Each workspace has one environment, recorded in its workspace file (Section 8.1). Every operation on the workspace resolves sources for that environment. How a workspace without a recorded environment gets one is not defined by this document.

`.lip` at the root of the workspace is a directory that holds the files of Section 8. No package writes or deletes anything there (Section 5).

**Server layout.** In a workspace whose platform is `"server"`, mods are installed below `mods`, and `plugins` at the root MUST be a link to `mods`: a directory junction on Windows, a relative symbolic link elsewhere. A path through `plugins/` and the corresponding path through `mods/` are the same file; they are compared, and recorded in the workspace file, in the `mods/` form. Before an operation changes the workspace, it establishes the layout:

- When `mods` does not exist, it is created as a directory.
- When `plugins` does not exist, it is created as a link to `mods`.
- When `plugins` is a link to `mods`, nothing changes.
- When `plugins` is a link to anything else, or a file, it is a failure.
- When `plugins` is a directory, its entries are moved into `mods` and `plugins` is replaced by a link to `mods`. When an entry has the same name as an entry of `mods`, it is a failure, before any entry is moved.

## 4. Valid States

The **install edges** of a concrete package are the members of its `dependencies` whose value has `source`. Other members of `dependencies` take no part in this document.

**Standing in.** A concrete package whose chain starts with a remote repository `r#v@x` with a ref `x` stands in for every candidate of `r#v` without a ref (same `r` and `v`) for a range its version satisfies. A source with a ref is satisfied only by its own candidates: a package from `r@branch:a` does not satisfy `r@branch:b`, and a package from a version tag does not satisfy `r@branch:a`.

The set S of packages installed in a workspace, given the requests that made its explicitly installed packages, MUST satisfy all of the following:

1. For every request (s, range), S contains a candidate of Resolve(s, none, range, environment), or a package that stands in for one.
2. For every install edge (key k, value d) of every package P in S, S contains a package Q such that:
   - for some source s of the `source` of d, Q is a candidate of Resolve(s, the archive of P, the `version` of d, environment), or stands in for one; and
   - the identity of Q equals k.
3. No two packages in S have the same identity.
4. For every member (key k, range R) of the `conflicts` of every package in S, S contains no package whose identity equals k and whose version satisfies R.

An operation whose result cannot satisfy these conditions is a failure. When more than one S satisfies them, packages earlier in the sequences of candidates SHOULD be preferred.

## 5. Installing and Uninstalling a Package

**Commands.** A **command** is a string of `hooks`. It is run by the operating system shell (`cmd /c` on Windows, `sh -c` elsewhere) with the workspace as the working directory. A command that exits with a non-zero code is a failure.

**Ownership.** A package **owns** every file it writes to the workspace by the steps below.

**Installing** a package P consists of these steps, in order:

1. Run each command of `hooks.preInstall`, in order.
2. Write the files:
   - Mod: every file of the archive of P is written to `mods/<id>/`, `<id>` being the identity of P.
   - File package: every entry of `files` is carried out, in order, its `src` being a path in the archive of P:
     - File form: the file `src` is written to `dest`. When there is no such file, it is a failure.
     - Directory form: every file below the directory `src` is written into `dest`, at its path relative to `src`. When there is no file below `src`, it is a failure.
     - Glob form: every file whose path matches the glob `src` is written into `dest`, at its path relative to the leading segments of `src` that contain none of `*`, `?` and `[`. A glob that matches no file writes nothing.
     - The manifest of P is never written.
     - When two entries write the same path, the later one wins.
3. Run each command of `hooks.postInstall`, in order.

**Write rules.** When a file is written to the workspace:

- When it is `.lip` or below it, it is a failure.
- When it is owned by another installed package, it is a failure.
- When it exists and matches a glob of the `preserve` of P, the existing file is kept.
- Otherwise it is written, replacing an existing one.

The globs of `preserve` and `remove` are matched against paths relative to `mods/<id>/` for a mod, and relative to the workspace for a file package.

**Uninstalling** a package P consists of these steps, in order:

1. Run each command of `hooks.preUninstall`, in order.
2. Delete each file owned by P that matches no glob of its `preserve`.
3. Delete each path that matches a glob of its `remove` and is not owned by another package. A directory is deleted with everything below it. `mods`, `plugins` and `.lip` at the root of the workspace never match, and nothing below `mods` or `plugins` is deleted through them; nothing below `.lip` matches.
4. Delete each directory that steps 2 and 3 left empty, except `mods` and `plugins`.
5. Run each command of `hooks.postUninstall`, in order.

**Updating** a package uninstalls the old package, then installs the new one.

## 6. Operations

**Order.** Within an operation, a package is installed after the packages that satisfy its install edges. The packages of a cycle of install edges are installed in ascending order of their identities with `A`–`Z` replaced by `a`–`z`. This is the **installation order**.

**Updating.** Updating an explicitly installed package makes a request of the first source of its chain again:

- a remote repository without a ref gives its highest version tag;
- `@<version>` and `rev:` give the same commit;
- `branch:` and `tag:` give the commit the branch or tag points to now;
- a URL is downloaded again; the content behind a URL SHOULD NOT change;
- a local path is read again.

The package is replaced only when the result differs in identity, version, archive name or archive identifier (Section 8.1).

**Reinstalling.** Installing a package again from the workspace file installs the archive the workspace file records, whatever the sources of its chain name now, and is a failure when its archive identifier differs.

## 7. Atomicity

An operation succeeds or fails as a whole.

1. **Early failures change nothing.** When an operation fails for a reason that does not depend on changing the workspace (Section 4, an archive that cannot be obtained, a missing `src`, a file owned by another package, a server layout that cannot be established), no command of it runs, no change is made, and the workspace file does not change. Each step is checked against the workspace as the steps before it leave it.
2. **Later failures are undone.** When a step fails while the workspace is being changed (a file cannot be written or deleted, a command fails, the operation is cancelled), every change the operation made is undone as Section 8.5 defines: files it created are deleted, files it replaced or deleted are restored, and directories and the server layout are restored. The workspace file does not change.
3. **Commands are not undone.** Commands that already ran are neither run again nor reversed. Commands SHOULD be safe to run again, and SHOULD NOT assume that the files of the operation stay in place.
4. The paths that uninstallation steps 2–4 delete are those that match before any command of the operation runs.
5. **Interruption.** When an operation is interrupted while changing the workspace (for example, its process is killed), the next operation on the workspace first recovers it (Section 8.5): it completes it when the journal records its commit, and undoes it otherwise.
6. **Concurrency.** An operation holds the workspace lock (Section 8.2) for its whole duration (Section 8.4). Two operations therefore never change the same workspace at the same time.

The workspace file records an operation only once every step of it has succeeded.

## 8. The `.lip` Directory

`.lip` holds the following entries. On Windows it SHOULD have the hidden attribute. Entries of `.lip` not named in this section are not defined by this document, and an implementation MUST NOT read meaning into them.

| Entry | Section |
|---|---|
| `workspace.json` | The workspace file (Section 8.1). |
| `workspace.json.new` | The next workspace file, while an operation commits (Section 8.4). |
| `lock` | The workspace lock (Section 8.2). |
| `journal` | The journal (Section 8.3). |

Creating `.lip` and `.lip/lock` is not a change, and an operation that fails early (Section 7 rule 1) MAY leave them.

### 8.1. Workspace File

The **workspace file** of a workspace is `.lip/workspace.json`, valid against `workspace.schema.json`. It belongs to the directory, not to its path: a workspace that is moved, renamed or copied keeps its workspace file, and so its installed packages. A workspace without a workspace file (and without the files of Section 8.6) has no installed packages and no recorded environment.

The workspace file is replaced only as a whole, by renaming `.lip/workspace.json.new` over it (Section 8.4). A process that reads it without holding the workspace lock therefore reads either the file before an operation or the file after it.

It records the environment, and for each installed package:

- its identity (`id`) and version (`version`);
- its kind (`kind`): `"mcmod"` for a package as defined in [PACKAGE];
- whether a request named it (`explicit`): such a package is **explicitly installed**; any other is a **dependency package**;
- its chain (`chain`): for each source followed, in order, the source (`ref`), the index in `targets` of the target selected in the meta package reached there, or `null` when a concrete package was reached (`target`), and, for a remote repository, the commit its archive was at (`commit`). A local path of a request is recorded as the `file:` URI [RFC8089] of its absolute path;
- its archive name (`archiveKey`) and archive identifier (`archive`), defined below;
- its manifest (`manifest`);
- the paths of the files it owns (`files`), in the `mods/` form of Section 3.

The **archive identifier** of a package identifies the content of its archive:

| Archive | `archive` |
|---|---|
| A ZIP archive downloaded from a URL | `{"kind": "zip", "sha256": <SHA-256 of the file>, "url": <the URL, without its fragment>}` |
| A ZIP archive that is a local file | `{"kind": "zip", "sha256": <SHA-256 of the file>, "url": <the file: URI of its absolute path>}` |
| A ZIP archive that is a file inside another archive | `{"kind": "zip", "sha256": <SHA-256 of the file>, "url": null}` |
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

`<repo>` and `<version>` are as written in the source that selected the archive, after templates are replaced (Section 5 of [PACKAGE]).

Entries whose `kind` is not `"mcmod"` record packages of formats this protocol does not define. An implementation that does not know such a `kind` MUST NOT install, uninstall or update that package, and MUST keep its entry unchanged.

### 8.2. Workspace Lock

The **workspace lock** is held on the file `.lip/lock`. To take it, a process creates `.lip` and `.lip/lock` (an empty file) when they do not exist, opens `.lip/lock`, and:

- on Windows, keeps it open with a share mode of 0 [CREATEFILE];
- elsewhere, takes an exclusive `flock` lock on it [FLOCK] (`LOCK_EX`).

When this fails because another process has the file open with a share mode of 0, or holds a `flock` lock on it, another process holds the workspace lock. The lock is released by closing the file, which the operating system does when the process ends.

`.lip/lock` MUST NOT be deleted, renamed or replaced while the workspace lock is held, and its content is not defined by this document.

A process that only reads the workspace file need not take the workspace lock.

### 8.3. Journal

The **journal** is the file `.lip/journal`. It exists only while an operation changes the workspace, or after such an operation was interrupted. It is a sequence of **records**, each a JSON [RFC8259] object written in UTF-8 on one line ended by LF (U+000A). A line that is not such an object, including a last line that is cut off, is ignored.

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
| `"CreateLink"` | `path` | The link `path` was created (Section 3). |
| `"Commit"` | none | The operation committed (Section 8.4). |

Every change an operation makes is one of these records: replacing a file is a `"Backup"` record followed by a `"CreateFile"` record, and deleting a file is a `"Backup"` record. Each record MUST be appended to the journal, and the journal flushed to the operating system, before the change it records is made.

A journal is replaced as a whole only by writing its new content to another file in `.lip` and renaming that file over `.lip/journal`.

### 8.4. Carrying Out an Operation

An operation does the following, in order:

1. Take the workspace lock (Section 8.2). When another process holds it, it is a failure.
2. When `.lip/journal` exists, recover (Section 8.5).
3. Read the workspace file, and decide what the operation does (Sections 4 to 6). A failure here is an early failure (Section 7 rule 1).
4. When the operation makes no change, write the new workspace file to `.lip/workspace.json.new`, rename it over `.lip/workspace.json`, and go to step 10.
5. Create `.lip/journal` with a `"Begin"` record. A journal that already exists is never overwritten.
6. Make the changes and run the commands (Sections 3 and 5), each change after its record (Section 8.3). When a step fails or the operation is cancelled, undo the operation (Section 8.5) and fail.
7. Write the new workspace file to `.lip/workspace.json.new`.
8. Append a `"Commit"` record to the journal, and flush it to the operating system. This is the **commit**: from here on, the operation is completed rather than undone.
9. Rename `.lip/workspace.json.new` over `.lip/workspace.json`, then complete the operation (Section 8.5).
10. Release the workspace lock.

When step 9 fails to rename the file, the implementation MUST replace the journal by one without the `"Commit"` record before it undoes the operation.

### 8.5. Recovery

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

Undoing a record is checked against the workspace as it is, so a record whose change was never made, or was already undone, changes nothing; recovering a journal again after an interruption of recovery gives the same result.

**Recovering** is completing the operation when the journal has a `"Commit"` record, and undoing it otherwise. The process that recovers MUST hold the workspace lock.

### 8.6. Earlier Formats

- When `.lip/workspace.json` does not exist and `.lip/lock.json` written by lip v0.36 does, `.lip/lock.json` is read as the workspace file; it has the same format. The next operation writes `.lip/workspace.json` and then deletes `.lip/lock.json`.
- When neither exists, a `tooth_lock.json` at the root of the workspace was written by lip v0.x. How it is taken over is not defined by this document.
- lip v0.36 kept the journal and the inter-process lock of a workspace outside it. They are neither read nor deleted.

## 9. Security Considerations

Commands of `hooks` run with the privileges of the process that runs the operation, and are not undone (Section 7). Installing a package runs its commands.

A process that can write to `.lip` can make the next operation delete or rename files in the workspace through a journal (Section 8.5). `.lip` has the same access rights as the workspace.

## 10. IANA Considerations

This document has no IANA actions.

## 11. References

### 11.1. Normative References

- [CREATEFILE] Microsoft, "CreateFileW function (fileapi.h)", <https://learn.microsoft.com/windows/win32/api/fileapi/nf-fileapi-createfilew>.
- [FLOCK] "flock(2) - Linux manual page", <https://man7.org/linux/man-pages/man2/flock.2.html>.
- [PACKAGE] "lip Package Protocol", [`package.md`](package.md).
- [RFC2119] Bradner, S., "Key words for use in RFCs to Indicate Requirement Levels", BCP 14, RFC 2119, DOI 10.17487/RFC2119, March 1997, <https://www.rfc-editor.org/info/rfc2119>.
- [RFC3339] Klyne, G. and C. Newman, "Date and Time on the Internet: Timestamps", RFC 3339, DOI 10.17487/RFC3339, July 2002, <https://www.rfc-editor.org/info/rfc3339>.
- [RFC8089] Kerwin, M., "The "file" URI Scheme", RFC 8089, DOI 10.17487/RFC8089, February 2017, <https://www.rfc-editor.org/info/rfc8089>.
- [RFC8174] Leiba, B., "Ambiguity of Uppercase vs Lowercase in RFC 2119 Key Words", BCP 14, RFC 8174, DOI 10.17487/RFC8174, May 2017, <https://www.rfc-editor.org/info/rfc8174>.
- [RFC8259] Bray, T., Ed., "The JavaScript Object Notation (JSON) Data Interchange Format", STD 90, RFC 8259, DOI 10.17487/RFC8259, December 2017, <https://www.rfc-editor.org/info/rfc8259>.

### 11.2. Informative References

- [LEVILAMINA] LiteLDev, "LeviLamina", <https://github.com/LiteLDev/LeviLamina>.
