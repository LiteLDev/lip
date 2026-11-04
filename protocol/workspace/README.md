# Workspace

This document defines [workspaces][workspace]: which [packages][package] a [workspace] may hold, what installing and uninstalling a [package] does to it, in what order, and the [lock] that records it. The format of the [lock] is defined by the JSON Schema [`lock.schema.json`](lock.schema.json). Terms written as links into [`../package/README.md`][P] are defined there.

## Conventions

The key words "MUST", "MUST NOT", "REQUIRED", "SHOULD", "SHOULD NOT", "RECOMMENDED", "MAY" and "OPTIONAL" are to be interpreted as described in BCP 14 ([RFC 2119], [RFC 8174]) when, and only when, they appear in all capitals.

## 1. Terms

- <a id="t-workspace"></a>**workspace**: a directory into which [packages][package] are installed. Paths below are relative to it and separated by `/`.
- <a id="t-mod"></a>**mod**: a [concrete package] whose [manifest] has the key `mod`. It is loaded by [LeviLamina].
- <a id="t-file-package"></a>**file package**: a [concrete package] that is not a [mod].
- <a id="t-identity"></a>**identity**: the `id` of the [manifest] of a [concrete package]. Two [identities][identity] are equal when they are equal [ASCII case-insensitively][ASCII case-insensitive].
- <a id="t-operation"></a>**operation**: one change to the set of [packages][package] installed in a [workspace]: installing, uninstalling or updating [packages][package], together with every [package] it installs, replaces or uninstalls as a dependency.
- <a id="t-request"></a>**request**: a pair ([source], [range]) given to an [operation]. A [request] is inside no [archive], so a local path in it names a file or directory on the local file system.
- <a id="t-failure"></a>**failure**: a condition that aborts the [operation] in progress. §6 defines what is left of its changes.

## 2. Environment and Layout

Each [workspace] has one [environment], recorded in its [lock] (§7). Every [operation] on the [workspace] resolves [sources][source] for that [environment]. How a [workspace] without a recorded [environment] gets one is not defined by this document.

`.lip` at the root of the [workspace], and everything below it, belongs to the implementation. No [package] writes or deletes anything there.

<a id="t-server-layout"></a>**Server layout.** In a [workspace] whose [platform] is `"server"`, [mods][mod] are installed below `mods`, and `plugins` at the root of the [workspace] MUST be a link to `mods`: a directory junction on Windows, a relative symbolic link on other operating systems. A path through `plugins/` and the corresponding path through `mods/` are the same file; they are compared, and recorded in the [lock], in the `mods/` form. Before an [operation] changes the [workspace], the layout is established:

- When `mods` does not exist, it is created as a directory.
- When `plugins` does not exist, it is created as a link to `mods`.
- When `plugins` is a link to `mods`, nothing changes.
- When `plugins` is a link to anything else, or a file, it is a [failure].
- When `plugins` is a directory, its entries are moved into `mods`, and `plugins` is replaced by a link to `mods`. When an entry has the same name as an entry of `mods`, it is a [failure], before any entry is moved.

Establishing the layout is part of the [operation].

## 3. Valid States

<a id="t-install-edge"></a>The **install edges** of a [concrete package] are the members of its `dependencies` whose value has `source`. Other members of `dependencies` take no part in this document.

<a id="t-stands-in"></a>**Standing in.** A [concrete package] whose [chain] starts with a [remote repository] `r#v@x` with a [ref] x **stands in for** every [candidate] of `r#v` without a [ref] (same `r`, same `v`) for a [range] its version satisfies. A [source] with a [ref] is satisfied only by its own [candidates][candidate]: a [package] from `r@branch:a` does not satisfy `r@branch:b`, and a [package] from a [version tag] does not satisfy `r@branch:a`.

The set S of [packages][package] installed in a [workspace], given the [requests][request] that made its [explicitly installed] [packages][package], MUST satisfy all of the following:

1. For every [request] (s, range), S contains a [candidate] of Resolve(s, none, range, [environment]), or a [package] that [stands in][stands-in] for one.
2. For every [install edge] (key k, value d) of every [package] P in S, S contains a [package] Q such that:
   - for some [source] s of the `source` of d, Q is a [candidate] of Resolve(s, the [archive] of P, the `version` of d, [environment]), or [stands in][stands-in] for one; and
   - the [identity] of Q equals k.
3. No two [packages][package] in S have the same [identity].
4. For every member (key k, [range] R) of the `conflicts` of every [package] in S, S contains no [package] whose [identity] equals k and whose version satisfies R.

An [operation] whose result cannot satisfy these conditions is a [failure]. When more than one S satisfies them, [packages][package] that come earlier in the sequences of [candidates][candidate] SHOULD be preferred.

## 4. Installing and Uninstalling a Package

<a id="t-command"></a>**Commands.** A **command** is a string of `hooks`. It is run by the shell of the operating system (`cmd /c` on Windows, `sh -c` on other operating systems) with the [workspace] as the working directory. A [command] that exits with a non-zero code is a [failure].

<a id="t-own"></a>**Ownership.** A [package] **owns** every file it writes to the [workspace] by the steps below.

**Installing** a [package] P consists of these steps, in order:

1. Run each [command] of `hooks.preInstall`, in order.
2. Write the files:
   - [Mod]: every file of the [archive] of P is written to `mods/<id>/`, `<id>` being the [identity] of P.
   - [File package]: every entry of `files` is carried out, in order, its `src` being a path in the [archive] of P:
     - File form: the file `src` is written to `dest`. When there is no such file, it is a [failure].
     - Directory form: every file below the directory `src` is written into `dest`, at its path relative to `src`. When there is no file below `src`, it is a [failure].
     - Glob form: every file whose path matches the glob `src` is written into `dest`, at its path relative to the leading segments of `src` that contain none of `*`, `?` and `[`. A glob that matches no file writes nothing.
     - The [manifest] of P is never written.
     - When two entries write the same path, the later one wins.
3. Run each [command] of `hooks.postInstall`, in order.

**Write rules.** When a file is written to the [workspace]:
- When the file is `.lip` or below it, it is a [failure].
- When the file is [owned][own] by another installed [package], it is a [failure].
- When the file exists and matches a glob of the `preserve` of P, the existing file is kept.
- Otherwise the file is written, replacing an existing one.

The globs of `preserve` and `remove` are matched against paths relative to `mods/<id>/` for a [mod], and relative to the [workspace] for a [file package].

**Uninstalling** a [package] P consists of these steps, in order:

1. Run each [command] of `hooks.preUninstall`, in order.
2. Delete each file [owned][own] by P that matches no glob of its `preserve`.
3. Delete each path that matches a glob of its `remove` and is not [owned][own] by another [package]. A directory is deleted with everything below it. `mods`, `plugins` and `.lip` at the root of the [workspace] never match, and nothing below `mods` or `plugins` is deleted through them; nothing below `.lip` matches.
4. Delete each directory that steps 2 and 3 left empty, except `mods` and `plugins`.
5. Run each [command] of `hooks.postUninstall`, in order.

**Updating** a [package] uninstalls the old [package], then installs the new one.

## 5. Operations

<a id="t-installation-order"></a>**Order.** Within an [operation], a [package] is installed after the [packages][package] its [install edges][install edge] are satisfied by. The [packages][package] of a cycle of [install edges][install edge] are installed in ascending order of their [identities][identity] with `A`–`Z` replaced by `a`–`z`.

**Updating.** Updating an [explicitly installed] [package] makes a [request] of the first [source] of its [chain] again:
- a [remote repository] without a [ref] gives its highest [version tag];
- `@<version>` and `rev:` give the same commit;
- `branch:` and `tag:` give the commit the branch or tag points to now;
- a [URL][url source] is downloaded again; the content behind a URL SHOULD NOT change;
- a local path is read again.

The [package] is replaced only when the result differs in [identity], version, or [archive identifier] (§7).

**Reinstalling.** Installing a [package] again from the [lock] installs the [archive] the [lock] records, whatever the [sources][source] of its [chain] name now, and is a [failure] when its [archive identifier] differs.

## 6. Atomicity

An [operation] succeeds or fails as a whole.

1. **Early failures change nothing.** When an [operation] fails for a reason that does not depend on changing the [workspace] (§3, an [archive] that cannot be obtained, a missing `src`, a file [owned][own] by another [package], a [server layout] that cannot be established), no [command] of it runs, and neither the [workspace] nor the [lock] changes. Each step is checked against the [workspace] as the steps before it leave it.
2. **Later failures are undone.** When a step fails while the [workspace] is being changed (a file cannot be written or deleted, a [command] fails, the [operation] is cancelled), every change the [operation] made to the [workspace] is undone: files it created are deleted, files it replaced or deleted are restored, and directories and the [server layout] are restored. The [lock] does not change.
3. **Commands are not undone.** [Commands][command] that already ran are neither run again nor reversed. [Commands][command] SHOULD be safe to run again, and SHOULD NOT assume that the files of the [operation] stay in place.
4. The paths that uninstallation steps 2–4 delete are those that match before any [command] of the [operation] runs.
5. **Interruption.** When an [operation] is interrupted while changing the [workspace] (for example, its process is killed), the next [operation] on the [workspace] first completes it when the [lock] already records it, and undoes it otherwise.
6. **Concurrency.** Two [operations][operation] MUST NOT change the same [workspace] at the same time.

The [lock] records an [operation] only once every step of it has succeeded.

## 7. Lock

<a id="t-lock"></a>The **lock** of a [workspace] is the file `.lip/lock.json` in it, valid against [`lock.schema.json`](lock.schema.json). It belongs to the directory, not to its path: a [workspace] that is moved, renamed or copied keeps its [lock], and so its installed [packages][package].

The [lock] records the [environment], and for each installed [package]:

- its [identity] and version;
- <a id="t-explicitly-installed"></a>whether a [request] named it: such a [package] is **explicitly installed**; any other is a **dependency package**;
- its [chain]: the [source] of each element, the index of the target selected there, and for a [remote repository], the commit its [archive] was at;
- <a id="t-archive-identifier"></a>its **archive identifier**:
  - for a [ZIP archive], its SHA-256 and the URL or local file it was read from;
  - for a [ZIP archive] inside another [archive], its SHA-256 and the [archive identifier] of that [archive];
  - for a [git archive], the repository, the commit id and the path of the [archive] in it;
  - for a [directory archive] on the local file system, its absolute path;
  - for a directory inside another [archive], the path of the directory and the [archive identifier] of that [archive];
- its [manifest];
- the paths of the files it [owns][own], in the `mods/` form of §2.

Entries whose `kind` is not `"mcmod"` record [packages][package] of formats this protocol does not define. An implementation that does not know such a `kind` MUST NOT install, uninstall or update that [package].

**Earlier formats.** A `tooth_lock.json` at the root of a [workspace] without `.lip/lock.json` was written by lip v0.x. How it is taken over is not defined by this document.

[P]: ../package/README.md
[package]: ../package/README.md#t-package
[concrete package]: ../package/README.md#t-concrete-package
[archive]: ../package/README.md#t-archive
[ZIP archive]: ../package/README.md#t-zip-archive
[directory archive]: ../package/README.md#t-directory-archive
[git archive]: ../package/README.md#t-git-archive
[manifest]: ../package/README.md#t-manifest
[ASCII case-insensitive]: ../package/README.md#t-ascii-ci
[range]: ../package/README.md#t-range
[platform]: ../package/README.md#t-platform
[environment]: ../package/README.md#t-environment
[source]: ../package/README.md#t-source
[remote repository]: ../package/README.md#t-remote-repository
[url source]: ../package/README.md#t-url-source
[version tag]: ../package/README.md#t-version-tag
[ref]: ../package/README.md#t-ref
[candidate]: ../package/README.md#t-candidate
[chain]: ../package/README.md#t-chain

[workspace]: #t-workspace
[mod]: #t-mod
[file package]: #t-file-package
[identity]: #t-identity
[operation]: #t-operation
[request]: #t-request
[failure]: #t-failure
[server layout]: #t-server-layout
[install edge]: #t-install-edge
[stands-in]: #t-stands-in
[command]: #t-command
[own]: #t-own
[lock]: #t-lock
[explicitly installed]: #t-explicitly-installed
[archive identifier]: #t-archive-identifier

[RFC 2119]: https://www.rfc-editor.org/rfc/rfc2119
[RFC 8174]: https://www.rfc-editor.org/rfc/rfc8174
[LeviLamina]: https://github.com/LiteLDev/LeviLamina
