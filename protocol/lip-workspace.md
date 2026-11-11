# lip-workspace(7)

## NAME

lip-workspace - workspaces and operations on them

## DESCRIPTION

### Terms

- **workspace**: A directory into which packages are installed. Paths in this page are relative to it and `/`-separated, unless said to be absolute.
- **package**: In this page, a candidate ([lip-package(7), "Resolution"](lip-package.md#resolution)); its **entry** is its selected entry.
- **mod**: A package that is a mod ([lip-package(7), "Resolution"](lip-package.md#resolution)). Its **mod directory** is `mods/<mod name>/`.
- **file package**: A package that is not a mod.
- **identity**: The identity of a package ([lip-package(7), "Resolution"](lip-package.md#resolution)).
- **source of a package**: The source of the request or the key of the dependency that it was resolved for.
- **operation**: Installing, uninstalling or updating packages in a workspace as one unit, including every package it installs, replaces or uninstalls as a dependency.
- **request**: A pair (source, range) given to an operation.
- **failure**: A condition that aborts the operation in progress.
- **change**: Creating, replacing or deleting a file or a directory in the workspace, other than below `.lip`.
- **Windows platform**: A platform that starts with `win`.
- **hook command**: A string of `hooks`, run as a program ("Running programs") by the shell of the platform of the environment: for a Windows platform, `cmd` with the arguments `/s` and `/c` followed by the string, which on Windows is written on the command line in double quotes, so that `cmd` runs it as it is, and under Wine is passed as "Running programs" defines; for any other platform, `sh` with the arguments `-c` and the string. A non-zero exit code is a failure. Running a member of `hooks` runs its hook commands in order.

### Environment and layout

Each operation has one environment: it resolves sources for it ("Valid states"), and the state file it writes records it ([state.json(5)]). How an operation gets its environment is not defined here.

`.lip` at the workspace root is the directory of "The .lip directory".

Mod directories are below `mods` at the workspace root, the game directory LeviLamina loads mods from. Before an operation changes the workspace, it creates `mods` as a directory when it is missing; when `mods` is a link, it is a failure.

When the platform of the environment is a Windows platform and `lip` does not run on Windows, the paths of the workspace are **case-insensitive**, as they are to the programs that Wine runs ("Running programs"):

- wherever this page compares a path in the workspace with another path, or matches it against a glob of `preserve` or `purge`, `A`–`Z` equal `a`–`z`;
- a file is written in place of the existing file, and below the existing directories, that its path names so, not beside them; and
- when a path names more than one existing file or directory so, it is a failure.

### Running programs

A **program** is a command line that `lip` runs in a workspace: a hook command, or the server ([lip-run(1)](lip-run.md)).

A program runs with the workspace as working directory and with the environment variables of `lip`. A program that cannot be started is a failure.

When the platform of the environment is a Windows platform and `lip` does not run on Windows, the program is run by [Wine]: `lip` runs `wine`, found through `PATH`, with these arguments: for a hook command, `cmd`, `/s`, `/c` and the string, as one argument; for the server, the absolute path of `bedrock_server_mod.exe`. Wine passes them to the Windows program as one command line, writing each `"` of an argument as `\"`, which `cmd` reads as it is; a hook command whose string contains `"` should therefore not be run by Wine. `lip` sets for `wine`:

| Variable | Value |
|---|---|
| `WINEPREFIX` | When it is unset or empty for `lip`: the absolute path of `.lip/wine`. |
| `WINEDEBUG` | When it is unset for `lip`: `-all`. |

The **exit code** of a program is the status that the process `lip` started exits with, `wine` included, or 128 + n when signal n ([signal(7)]) ends it. Wine creates its prefix when it is missing; what the prefix holds is not defined here.

In every other case `lip` runs the program itself, finding `sh` through `PATH`.

### Valid states

A package whose source is a remote repository `r#v@x`, `x` a ref, **stands in** for every candidate of `r#v` (same `r` and `v`, no ref) for a range its version satisfies. A source with a ref is satisfied only by its own candidates.

The set S of packages installed in a workspace, given the requests that made its explicitly installed packages, satisfies:

1. For every request (s, range), S contains a candidate of Resolve(s, range, environment), or a package that stands in for one.
2. For every dependency (k, R) of every package in S, S contains a candidate of Resolve(k, R, environment), or a package that stands in for one.
3. No two packages in S have the same identity, and no two mods in S have the same mod name, compared ASCII case-insensitively.

An operation that cannot end in such an S is a failure. When several such S exist, packages earlier in the sequences of candidates should be preferred.

### Installing and uninstalling

A package **owns** every file it writes to the workspace by the steps below. The keys named are those of its entry ([lip.toml(5)]).

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

When a file is written to the workspace:

- When it is `.lip` or below it, or is owned by another installed package, it is a failure.
- When `lip` runs on Windows or the platform of the environment is a Windows platform, and a segment of its path other than `.` contains `:` or ends in `.` or a space, it is a failure.
- When it exists and matches a glob of the `preserve` of P, the existing file is kept.
- Otherwise it is written, replacing an existing one.

The globs of `preserve` and `purge` are matched against paths relative to the workspace.

**Uninstalling** a package P is, in order:

1. Run `hooks.pre_uninstall`.
2. Delete each file owned by P that matches no glob of its `preserve`.
3. Delete each path that matches a glob of its `purge` and is not owned by another package; a directory is deleted with everything below it. `mods` and `.lip` at the workspace root, and everything below `.lip`, never match, and a path below `mods` does not match a glob only because `mods` does.
4. Run `hooks.post_uninstall`.

Each directory that steps 2 and 3 left empty, except `mods`, is deleted when the operation completes, if it is empty then ([journal(5), "Recovery"](journal.md#recovery)).

**Updating** a package uninstalls the old package, then installs the new one.

### Operations

Within an operation, a package is installed after the packages that satisfy its dependencies; the packages of a cycle of dependencies are installed in ascending order of their identities with `A`–`Z` replaced by `a`–`z`. This is the **installation order**.

Updating an explicitly installed package makes a request of its source again:

- a remote repository without a ref gives its highest version tag;
- `@<version>` and `rev:` give the same commit;
- `branch:` and `tag:` give the commit the branch or tag points to now;
- a URL is downloaded again;
- a local path is read again.

The package is replaced only when the package obtained differs in identity, version or commit.

Installing a package again from the state file installs it with the package file the state file records: for a remote repository, from the commit it records; otherwise from the archive its source names now.

### Atomicity

An operation succeeds or fails as a whole.

1. **Early failures change nothing.** When an operation fails for a reason that does not depend on changing the workspace ("Valid states", an archive or asset that cannot be obtained, a missing `src`, a file owned by another package, a mod name that cannot be read, a `mods` that is a link), no hook command of it runs, no change is made, and the state file does not change. Each step is checked against the workspace as the steps before it leave it.
2. **Later failures are undone.** When a step fails while the workspace is being changed (a file cannot be written or deleted, a hook command fails, the operation is cancelled), every change the operation made is undone ([journal(5), "Recovery"](journal.md#recovery)). The state file does not change.
3. **Hook commands are not undone.** Hook commands that already ran are neither run again nor reversed. They should be safe to run again, and should not assume that the files of the operation stay in place.
4. The paths that uninstallation steps 2 and 3 delete are those that match when the step is carried out, including paths that hook commands of the operation created.
5. **Interruption.** When an operation is interrupted while changing the workspace, the next operation on the workspace first recovers it ([journal(5), "Recovery"](journal.md#recovery)).

### The .lip directory

`.lip` holds:

| Entry | Is |
|---|---|
| `state.json` | The state file ([state.json(5)]). |
| `state.json.tmp` | The state file being written ("Carrying out an operation"). |
| `lock` | The file of the workspace lock ("Workspace lock"). |
| `journal` | The journal ([journal(5)]). |
| `logs` | The directory of failure reports ([lip(1), EXIT STATUS](lip.md#exit-status)). |
| `wine` | The Wine prefix that "Running programs" names. |

On Windows `.lip` should have the hidden attribute. Its other entries are not defined here, and an implementation must not read meaning into them.

Creating `.lip` and `.lip/lock` is not a change, and an operation that fails early ("Atomicity", rule 1) may leave them.

Creating `.lip/logs` and writing or deleting below it is not a change, and any process may do so at any time. Neither is anything that Wine does below `.lip/wine`.

### Workspace lock

The **workspace lock** is held on `.lip/lock`. To take it, a process creates `.lip` and the empty file `.lip/lock` when they do not exist, opens `.lip/lock`, and keeps it open with a share mode of 0 ([CreateFileW]) on Windows, or takes an exclusive `flock` lock (`LOCK_EX`, [flock(2)]) on it elsewhere. When this fails because another process has the file open with a share mode of 0 or holds a `flock` lock on it, another process holds the workspace lock. Closing the file releases the lock.

`.lip/lock` must not be deleted, renamed or replaced while the workspace lock is held. Its content is not defined here.

A process that only reads the state file need not take the workspace lock.

### Carrying out an operation

An operation does, in order:

1. Take the workspace lock. When another process holds it, it is a failure.
2. When `.lip/journal` exists, recover ([journal(5), "Recovery"](journal.md#recovery)).
3. Read the state file, and decide what the operation does ("Valid states", "Installing and uninstalling", "Operations"). A failure here is an early failure ("Atomicity", rule 1).
4. When the operation makes no change and runs no hook command, write the new state file to `.lip/state.json.tmp`, rename it to `.lip/state.json`, and go to step 10.
5. Create `.lip/journal` with a `"Begin"` record. A journal that already exists is never overwritten.
6. Make the changes and run the hook commands ("Environment and layout", "Installing and uninstalling"), each change after its record. When a step fails or the operation is cancelled, undo the operation ([journal(5), "Recovery"](journal.md#recovery)) and fail.
7. Write the new state file to `.lip/state.json.tmp`.
8. Append a `"Commit"` record to the journal, and flush it to the operating system.
9. Rename `.lip/state.json.tmp` to `.lip/state.json`, then complete the operation ([journal(5), "Recovery"](journal.md#recovery)).
10. Release the workspace lock.

When step 9 fails to rename the file, the implementation replaces the journal by one without the `"Commit"` record before it undoes the operation.

## SEE ALSO

[lip(1)](lip.md), [lip-install(1)](lip-install.md), [lip-uninstall(1)](lip-uninstall.md), [lip-update(1)](lip-update.md), [lip-run(1)](lip-run.md), [state.json(5)], [journal(5)], [lip-package(7)](lip-package.md)

[lip.toml(5)]: lip.toml.md
[state.json(5)]: state.json.md
[journal(5)]: journal.md
[CreateFileW]: https://learn.microsoft.com/windows/win32/api/fileapi/nf-fileapi-createfilew
[flock(2)]: https://man7.org/linux/man-pages/man2/flock.2.html
[Wine]: https://www.winehq.org/
[signal(7)]: https://man7.org/linux/man-pages/man7/signal.7.html
