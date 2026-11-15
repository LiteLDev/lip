# Internals

After this page you know how lip finds the versions of a package, how it talks to git repositories without `git`, what it caches, how it chooses between versions, and how it prepares the system for the server. This page is for the curious and for package authors. [How lip works](how_lip_works.md) gives the overview, and the [lip protocol](https://github.com/LiteLDev/lip/blob/main/protocol/README.md) is the exact definition.

## The steps of an operation

`lip setup`, `lip install`, `lip uninstall` and `lip update` run the same steps:

1. **Lock and recover.** Take the workspace lock, `.lip/lock`. Finish or undo an operation that was interrupted.
2. **State file.** Read the installed packages from `.lip/state.json`. A workspace without it has no packages, unless it has a `tooth_lock.json` of lip v0.x, which is read instead.
3. **Environment.** Packages are chosen for the side `server` and the platform `win-x64`, on every system.
4. **Resolve.** Turn each source into a sequence of candidates: the versions it names, newest first, each with the entry of the requested variant for the environment.
5. **Solve.** Choose one candidate for each request and each dependency, so that every package appears once and no two mods have the same name.
6. **Plan.** Plan to uninstall the packages that leave the set, in reverse installation order, then to install the packages that enter it, dependencies first. Planning downloads every archive and asset that will be written, and checks everything that can fail: who owns each file, `preserve`, the paths, the `src` of each placement, the name of each mod in its `manifest.json`, and whether `mods` is a link.
7. **Prepare the system**, when programs for Windows will run. See [System dependencies](#system-dependencies).
8. **Apply.** Create `mods` if it is missing, then run the planned hook commands and file changes, each noted in the journal first.
9. **State file.** Save the state file once, when every step has succeeded.

A failure before step 8 leaves the workspace untouched. A failure during step 8, or Ctrl+C, makes lip undo every change of the operation. What hook commands did is not undone.

A dry run stops after step 6, takes no lock in step 1, and repairs nothing there.

### One operation at a time

An operation holds `.lip/lock` open for itself alone: without sharing on Windows, with an exclusive `flock` on Linux and macOS. The file is kept, never deleted. A second operation on the same workspace does not wait. It fails at once with:

```text
lip cannot take the workspace lock '<workspace>/.lip/lock': most likely another lip command is changing the workspace '<workspace>'. Wait for the other lip command to finish, then try again. If no other lip command is running, a program may be holding the lock file open: close it, then try again.
```

`lip run` takes the same lock only for the moment in which it recovers an interrupted operation, before it starts the server. While the server runs, it holds the server lock, `.lip/server.lock`, instead, in the same way. A second server of the workspace fails with:

```text
A server of the workspace '<workspace>' is already running: another lip run, the interface of lip or the daemon of lip runs it, and holds the server lock '<workspace>/.lip/server.lock'. Stop that server first, or attach to it with 'lip run --managed' when it is a managed server.
```

The two locks are independent. A process that is killed releases its locks with its file handles, so a lock is never left behind.

## Package resolution

lip has no central registry and no module proxy. A package id such as `github.com/user/repo` is the git repository `https://github.com/user/repo.git`.

- **Versions.** The versions of a repository are its tags named exactly `v` followed by a [SemVer 2.0.0](https://semver.org/) version, such as `v1.2.3`. Other tags are ignored, including loose ones such as `v1.2` or `v01.2.3`. An annotated tag counts as the commit it points to.
- **Refs.** A source with a ref has one candidate. `@branch:<name>` and `@tag:<name>` are looked up once per command. `@rev:<commit>` needs no lookup: the commit is fetched by its id. Short commit ids are refused, because resolving one would need the whole history, which lip never downloads.
- **Lazy candidates.** The candidates of a repository are produced newest first and only on demand. lip opens only the versions that solving really looks at.
- **What makes a candidate.** A version is a candidate only when its `lip.toml` is valid, its `id` is the path of the repository, its `version` equals the version of the tag, and the requested variant has an entry for the environment. Otherwise lip goes on to the next version, and remembers why, to tell you if nothing fits.
- **Without a tag version.** At a branch, at a commit, or at a tag that is not a version, the version of the package is the `version` in its `lip.toml`.
- **The game** is the exception. `github.com/LiteLDev/bds` is not a repository. lip makes its packages itself from the list of server builds at `https://levimc.org/api/v4/mc/versions/`, and downloads the server from minecraft.net.
- **Other sources.** A URL names one ZIP archive, downloaded over HTTPS. A local path names one ZIP file or one folder. Each has one candidate.

A version can be installed only while its tag exists in the repository. Only public repositories reachable over HTTPS work. Package ids are compared ignoring the case of ASCII letters.

## The git client

lip talks to git repositories with a client of its own, for version 2 of git's smart HTTP protocol. **The `git` program is not needed and is never run.**

- **Listing tags.** lip asks the server for its refs with `ls-refs`, limited to `refs/tags/v`. For a branch or a tag ref, it asks for exactly `refs/heads/<name>` or `refs/tags/<name>`.
- **Reading a `lip.toml`.** lip fetches only the trees of the one commit: a shallow, partial fetch (`deepen 1`, `filter blob:none`) that transfers no file contents. Then it fetches that one file, or `tooth.json` when there is no `lip.toml`. Looking at a version therefore costs a few kilobytes, however large the repository is.
- **Installing.** File contents are fetched only when a package is installed, and only the files it places from its own archive.
- **Integrity.** Every git object is checked against its object id.
- **Proxy.** With `github_proxy` set to a URL, requests for `github.com` repositories go through it first, and to `github.com` directly when that fails. Files downloaded from `github.com` URLs follow the same rule. See [When GitHub is slow or blocked](../guides/configuration.md#when-github-is-slow-or-blocked).

A server must support these features, as GitHub does. A proxy that only passes on file downloads cannot serve repositories.

## The cache

The cache directory holds what lip downloads over HTTPS: ZIP archives named by URL sources, the assets of packages, and the game.

- **Entries.** Each download is stored as `http-<hash>`, where the hash is the SHA-256 of its URL. The URL is the one the package names, whatever proxy was used.
- **Writing.** A download is written to a `.tmp` file in the cache directory and renamed into place when it is complete. So an entry that exists is complete, and several lip processes can share the cache.
- **Repair.** When lip needs a ZIP archive and the cached file is not one, it deletes the entry and downloads it again, once.
- **Not cached.** Nothing of git repositories is cached on disk. Tag lists and trees are kept in memory for the length of a command, and looked up again by the next one, so a moved tag takes effect at once.
- **No checksums of content.** ZIP archives and assets are checked to be valid ZIP archives. No checksum of their content is recorded or compared.

`lip cache clean` deletes the whole directory. A dry run downloads what it needs to check but writes nothing to the cache. lip never writes to the temporary directory of the system.

## Solving

Solving chooses which candidate to install for each request and each dependency.

- **Pinning.** Installed packages enter as they are, rebuilt from the state file without the network. If the request cannot be satisfied that way, lip solves again with only the packages you asked for held in place, so that dependencies may change version. Their installed versions are tried first, so only those that must change do.
- **Updating.** `lip update` solves the second way from the start, so that an installed dependency never holds back the newest version of a package you update.
- **Preference.** Earlier candidates win: the newest version of a repository.
- **The game and the loader are fixed.** `lip install` and `lip update` solve around them. When nothing fits, the failure names their versions and `lip setup`. A setup solves with the new game and loader, and leaves out a mod only when no version of it fits them.
- **Order.** Packages are installed dependencies first. The packages of a dependency cycle are ordered by identity.
- **Clean-up.** Dependencies that no package you asked for needs any more are uninstalled.
- **Same package.** An installed package is replaced only when what lip resolved differs from it in identity, version or commit. A package from a URL or a local path has no commit.
- **Installing again from the state file.** When lip needs the files of an installed package again, it installs it with the package file recorded in the state file, not a newly resolved one: for a repository, from the recorded commit; otherwise from the archive its source names now.

## Package file validation

`lip.toml` is described by [`lip.schema.toml`](https://github.com/LiteLDev/lip/blob/main/protocol/lip.schema.toml), the user settings by [`config.schema.toml`](https://github.com/LiteLDev/lip/blob/main/protocol/config.schema.toml), the workspace settings by [`workspace-config.schema.toml`](https://github.com/LiteLDev/lip/blob/main/protocol/workspace-config.schema.toml), and the state file by [`state.schema.json`](https://github.com/LiteLDev/lip/blob/main/protocol/state.schema.json).

- **Beyond the schema**, lip checks what [lip.toml(5)](https://github.com/LiteLDev/lip/blob/main/protocol/lip.toml.md) requires: a valid package id and version, valid dependency keys and ranges, sides and platforms, no two entries of a variant for the same side and platform, at most one `mcmod` asset per entry, and valid globs. An archive whose `lip.toml` is invalid is not a package. It gives no candidate, and lip reports why.
- **Files of lip v0.x.** An archive without `lip.toml` is read from its `tooth.json` of format 1, 2 or 3, which is converted completely to a package file while resolving. The same installer therefore handles every package. A `tooth.json` at a version tag is installed with the version of the tag, whatever its own `version` says. A `tooth_lock.json` becomes `.lip/state.json` and a `liprc.json` becomes `config.toml`, by themselves, the first time lip works with them. See [lip-compatibility(7)](https://github.com/LiteLDev/lip/blob/main/protocol/lip-compatibility.md).

Authors find the format in [lip.toml](lip_toml.md).

## Workspace state and the journal

The [state file](state_file.md), `.lip/state.json`, records for each installed package whether you asked for it, the package file it was installed with, its source, its commit when it came from a repository, and the files it owns.

An operation is made all-or-nothing by three kinds of files:

- **The journal**, `.lip/journal`. Each change is recorded there, and flushed to the disk, before it is made. Paths in it are relative to the workspace, so a workspace that is moved or copied takes an interrupted operation with it.
- **Backups.** A file that an operation replaces or deletes is first renamed to `.<name>.bak` in the same directory, or `.<name>.bak1`, `.<name>.bak2` and so on when that name is taken. On Windows it also gets the hidden attribute.
- **The staged state file.** The new state is written to `.lip/state.json.tmp` and renamed over `.lip/state.json`.

When the operation succeeds, lip marks the journal as committed, renames the state file into place, deletes the backups and the directories that the operation emptied, and deletes the journal. A backup that cannot be deleted, because a program has it open, is left and named in a warning.

When the operation fails or is cancelled, lip walks the journal backwards: it deletes what it created and renames each backup back.

When lip is killed, the journal stays. The next operation, or the next `lip run`, reads it once it holds the workspace lock. A journal with a commit mark is completed. One without is undone. If something cannot be undone yet, the journal is rewritten with what is left, and the command fails until it can.

The formats are fixed in [journal(5)](https://github.com/LiteLDev/lip/blob/main/protocol/journal.md) and [lip-workspace(7)](https://github.com/LiteLDev/lip/blob/main/protocol/lip-workspace.md), so that other programs can take part.

## System dependencies

The programs of a workspace, that is, the hook commands of packages and the Windows server, need two things of the system. lip installs both itself.

- **Wine**, on Linux and macOS. Each version of lip names one build of Wine for Linux on x64, from [Kron4ek/Wine-Builds](https://github.com/Kron4ek/Wine-Builds), and one for macOS, from [Gcenx/macOS_Wine_builds](https://github.com/Gcenx/macOS_Wine_builds), which Rosetta 2 runs on arm64. lip downloads it once and extracts it into `wine/` in the data directory, where every workspace shares it. lip never runs another `wine`.
- **The Microsoft Visual C++ Redistributable** for x64. It counts as installed when the registry has the value `Installed` = 1 under `HKLM\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64`. Otherwise lip downloads `vc_redist.x64.exe` and runs it with `/install /quiet /norestart`: on Windows as an administrator, which may show a User Account Control prompt; elsewhere through Wine, in the Wine prefix of the workspace, whose registry it reads with `wine reg query`.

How lip keeps this safe:

- **Pinned downloads.** The URL and the SHA-256 of each file are written into lip. A download whose SHA-256 differs is deleted, and nothing is extracted or run.
- **Never half installed.** Wine is extracted into a `.tmp` directory and renamed into place. A build that was cut off is never run.
- **One at a time.** lip processes that install at the same moment wait for each other through the file `lock` in the data directory.
- **Before the workspace changes.** lip prepares the system before an operation that runs hook commands starts to change the workspace, during a `lip setup` of the Windows build, and before `lip run` starts a Windows server. A failure there leaves the workspace as it was.

The Linux server needs neither. When everything is installed, preparing costs one registry lookup.

Through Wine, lip runs a hook command as `wine cmd /s /c <command>` and the server as `wine` with the absolute path of the server file, with `WINEPREFIX` set to `.lip/wine` in the workspace and `WINEDEBUG` to `-all`, unless you set them. See [Linux and macOS](../guides/linux_macos.md).

## The daemon

Every command except those of `lip daemon`, and the interface, are carried out by the daemon of the user: a `lip` process started with the single argument `--daemon`. The `lip` you start is its client.

1. **Connect.** On Windows through the named pipe `\\.\pipe\lip-<SID>`, which only your user can open. On Linux and macOS through the socket `$XDG_RUNTIME_DIR/lip/daemon.sock`, or `daemon.sock` in the state directory when `XDG_RUNTIME_DIR` is not set, with the mode `0600` in a directory of mode `0700`.
2. **Start.** When nothing listens, the client starts the daemon from its own executable, detached from the terminal, and connects once it listens. Two clients that do this at the same moment end up with one daemon: the second cannot create the endpoint and exits.
3. **Replace.** When the daemon is of another version or another executable, as after `lip self-update`, the client asks it to exit and starts a new one. A daemon that is carrying out a command or runs a managed server stays. The client then does its command itself, except `lip run --managed`, which the old daemon carries out.
4. **Send.** The client sends the command with its folder, its environment variables, whether its standard streams are terminals, and the size and colours of the terminal. The daemon carries the command out with them. Commands of different clients run side by side, each with its own folder, environment, proxy and failure report.
5. **Relay.** The daemon reads the input of the client only when the command needs it, and sends back what the command and its programs write. Signals that the client receives are passed on. The client exits with the exit code of the command.

The interface is carried out the same way. The daemon draws it, and the client writes what it draws to the terminal and passes on the keys you press and the size of the terminal.

The programs that the daemon runs, the server among them, are not in the session of your terminal. Their output reaches lip through pipes, not through a terminal.

The daemon exits after 10 minutes without a connection and without a managed server, after `lip self-update` replaced lip, or on `lip daemon stop`. The exact rules are in [lip-daemon(1)](https://github.com/LiteLDev/lip/blob/main/protocol/lip-daemon.md).

## Notes on the specification

lip manages server workspaces only: the side is always `server`. Client workspaces are left to launchers that implement the same protocol, such as LeviLauncher.

lip implements the [lip protocol](https://github.com/LiteLDev/lip/blob/main/protocol/README.md). Where this page and the protocol disagree, the protocol is right.
