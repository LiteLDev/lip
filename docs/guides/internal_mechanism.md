# Internal Mechanism

This guide explains how lip works under the hood.

## Pipeline

`lip install`, `lip uninstall` and `lip update` run the same steps:

1.  **State file**: Load the workspace's state from `.lip/state.json`. A workspace without it has no installed packages, unless it has a `tooth_lock.json` of lip v0.x, which is read instead (see [Workspaces of lip v0.x](../concepts/lockfiles.md#workspaces-of-lip-v0-x)).
2.  **Environment**: Determine the side and platform (see [Environment](../concepts/lip_toml.md#environment)).
3.  **Resolver**: Turn every source into a sequence of candidates: the archives it names, newest version first, each with the entry of the requested variant for the environment.
4.  **Graph solver**: Choose one candidate per request and per dependency, so that every identity appears once and no two mods have the same mod name.
5.  **Plan**: Plan uninstalling the packages that leave the set, in reverse installation order, then installing the packages that enter it, dependencies first, each against the workspace as the steps before it leave it. Planning downloads every archive and asset to be written and checks everything that can fail: file ownership, `preserve`, paths, the `src` of placements, the mod name in the LeviLamina `manifest.json` of mods, and, when the operation writes or deletes files, whether `mods` is a link. A `tooth.json` of an earlier format has been converted completely to a package file while resolving (see [Earlier Formats](../concepts/lip_toml.md#earlier-formats)), so the same installer handles every package, with the rules of `lip.toml` only.
6.  **Apply**: When the operation writes or deletes files, create `mods` if it is missing (see [Installing Dependencies](installing_dependencies.md#server-workspaces-only)), then run the planned hooks and file changes. A file matching `preserve` is kept when it exists as it is about to be written, after the hook commands before it, and the package owns only the files it wrote.
7.  **State file**: Save the state file once, after every step has succeeded.

Operations are all or nothing ([workspace protocol](https://github.com/LiteLDev/lip/blob/main/protocol/workspace.md#6-atomicity) §6, "Atomicity"). If anything fails before step 6, the workspace is not modified. If a step fails while applying (a file cannot be written, a hook command exits with a non-zero code, or you press Ctrl+C), lip undoes every change of the operation and leaves the state file as it was. What hook commands did cannot be undone: a failing `post_install` or `pre_uninstall` command still fails the operation and undoes lip's file changes, but not the effects of the commands that already ran.

## Package Resolution

lip does not use a central package registry or module proxy. A package id such as `github.com/user/repo` maps directly to the git repository `https://github.com/user/repo.git`, which lip talks to with its own built-in client for the git smart-HTTP protocol (version 2). **The `git` command-line tool is not required** and is never run.

-   **Versions**: lip lists the repository's tags with `ls-refs` (only `refs/tags/v*` is requested). The available versions are the tags named exactly `v<SemVer 2.0.0>` (e.g., `v1.2.3`); other tags, including lenient ones such as `v1.2` or `v01.2.3`, are ignored. Annotated tags are peeled to the commit they point to.
-   **Git refs**: A source with a git ref has one candidate. `@branch:<name>` and `@tag:<name>` are resolved with `ls-refs` for exactly `refs/heads/<name>` or `refs/tags/<name>` (annotated tags peeled), once per command; `@rev:<commit>` needs no request, and the commit is fetched by its id. Abbreviated commit ids are rejected: resolving one would need the whole history, which lip never downloads.
-   **Lazy candidates**: Candidates of a repository are produced newest tag first, and only on demand, so lip opens only the tags the solver actually needs.
-   **lip.toml**: To read the `lip.toml` of a version, lip fetches only the trees of the tagged commit (a shallow, partial fetch with `filter blob:none`, so no file contents are transferred) and then downloads that one file (or `tooth.json`, when there is no `lip.toml`). Resolving dependencies therefore costs a few kilobytes per version, regardless of the repository's size.
-   **Candidates**: A version is a candidate only when its `lip.toml` is valid, its `id` is the path of the repository, its `version` equals the tag version, and the requested variant has an entry for the side and platform of the workspace. Otherwise lip moves on to the next version.
-   **Installation**: Files are downloaded only when an installer needs them: the files a `self` asset places from the package's own archive, and the `zip` and `mcmod` assets, which are downloaded when the package is installed, like any other HTTP download.
-   **Integrity**: Every downloaded git object is checked against its git object id (SHA-1). ZIP archives and assets are checked to be valid ZIP archives; no checksum of their content is recorded or checked.
-   **Caching**: Git archives are not cached: only the files an operation reads are fetched, every time. Everything downloaded over HTTP(S) is stored in the cache as `http-<SHA-256 of the URL>`; a cached file that is not a valid ZIP archive is downloaded again. Downloads are written to `.tmp` files in the cache directory and renamed into place when complete; lip writes nothing to the system temporary directory. Tag lists and trees are kept in memory for the duration of a command; tags are looked up again on every run, so a moved tag takes effect immediately.
-   **Proxy**: If `github_proxy` is configured, requests for `github.com` repositories are first sent through the proxy. If a proxied request fails, lip retries it against `github.com` directly. Release assets downloaded from `github.com` URLs use the same rule.

A version can only be installed if its tag still exists in the repository. Only public repositories reachable over HTTPS are supported. Package ids are compared ignoring ASCII case.

At a branch, a commit, or a tag not named `v<SemVer>` given by a git ref, there is no tag version: the `version` of the `lip.toml` there is used. A `tooth.json` of an earlier format at a version tag is installed with the tag version, whatever its `version` says.

## Solving

-   **Pinning**: Installed packages enter the solver as they are, rebuilt from the state file without network access. If the request cannot be satisfied that way, lip solves it again with only the explicitly installed packages pinned, so dependencies may change version; their installed versions are tried first, so only the dependencies that must change do. `lip update` solves this way from the start, so that installed dependencies never hold back the newest version of an updated package.
-   **Preference**: Earlier candidates win: the newest version of a repository.
-   **Order**: Packages are installed dependencies first; packages in a dependency cycle are ordered by identity.
-   **Clean-up**: Dependencies that no explicitly installed package needs any more are uninstalled.
-   **Same package**: An installed package is replaced only when the package lip resolves differs in identity, version or commit; a package from a URL or a local path has no commit.
-   **Reinstalling from the state file**: When lip needs an installed package's files again, it installs it with the package file recorded in the state file instead of resolving the package again: for a package from a git repository, from the recorded commit; otherwise from the archive its source names now.

## Package File Validation

`lip.toml` is described by the JSON Schema [`protocol/lip.schema.toml`](https://github.com/LiteLDev/lip/blob/main/protocol/lip.schema.toml) and `config.toml` by [`protocol/config.schema.toml`](https://github.com/LiteLDev/lip/blob/main/protocol/config.schema.toml), both written as TOML, and the state file by [`protocol/state.schema.json`](https://github.com/LiteLDev/lip/blob/main/protocol/state.schema.json). The files of lip v0.x were described by JSON Schemas in the former [`specs/schemas/`](https://github.com/LiteLDev/lip/blob/1e023e6/specs/schemas) directory, under `legacy/v<format>/`: `tooth.schema.yaml` for tooth.json formats 1, 2 and 3, `tooth_lock.schema.yaml` for tooth_lock.json and `liprc.schema.yaml` for liprc.json (all format 3).

-   **Validation**: Beyond the schema, lip checks what the [package protocol](https://github.com/LiteLDev/lip/blob/main/protocol/package.md#3-package-files) requires: a valid package id, version, dependency keys and ranges, sides and platforms, no two entries of a variant for the same side and platform, at most one `mcmod` asset per entry, and valid globs. An archive whose `lip.toml` is invalid is not a package and yields no candidate, and lip reports why.
-   **lip v0.x files**: An archive without `lip.toml` is read from its `tooth.json` of format 1, 2 or 3: formats 1 and 2 are converted to format 3 in memory, and format 3 is converted completely to a package file for the environment ([compatibility protocol](https://github.com/LiteLDev/lip/blob/main/protocol/compatibility.md#2-toothjson) §2); run `lip migrate` in a package repository to replace its `tooth.json` by `lip.toml` (§5). A `tooth_lock.json` is migrated to `.lip/state.json` (§3), and a `liprc.json` to `config.toml` (§4), automatically.

## Workspace State

lip maintains the state of your installed packages in the state file kept in the workspace, at `.lip/state.json` (see [State File](../concepts/lockfiles.md#location)). This tracks:
-   Which packages are explicitly requested versus implicit dependencies.
-   The package file each package was installed with (and so its exact version), its source, and, for a package from a git repository, its commit.
-   The files each package owns.
-   The side and platform of the workspace.

Files written to make an operation atomic are hidden and sit next to their target: the state file is first written to `.lip/state.json.tmp` and then renamed over `.lip/state.json`, and a file that an operation overwrites or deletes is first renamed to `.<name>.bak` (or `.<name>.bak1`, `.<name>.bak2`, … if that name is taken) in the same directory. The backups are deleted once the state file is saved (one that cannot be deleted, for example because a program has it open, is left and named in a warning), or renamed back if the operation fails. Directories that an uninstallation empties are deleted at the same time. On Windows these files also get the hidden attribute.

The journal and the workspace lock below are kept in `.lip` next to the state file, so they move with the workspace: a moved or copied workspace takes an interrupted operation with it, and the journal records paths relative to the workspace. Their formats are defined in [`protocol/workspace.md`](https://github.com/LiteLDev/lip/blob/main/protocol/workspace.md#7-the-lip-directory), so that other programs can take part.

-   **Journal**: While an operation changes the workspace, lip records every change, before making it, in `.lip/journal`. If lip is killed, the next `lip install`, `uninstall` or `update` of the workspace reads the journal first: it undoes the interrupted operation (restoring the backups) and reports it, or, if the operation had already reached the point of saving its state file, finishes it by deleting the backups. A dry run takes no lock and only reports the journal.
-   **One operation at a time**: An operation holds `.lip/lock` open exclusively (an exclusive `flock` on Linux and macOS; the file is kept). A second `lip install`, `uninstall` or `update` of the same workspace fails at once with "Another process is changing the workspace"; run it again when the first one has finished.

## Notes on the Specification

lip manages server workspaces only: it never determines the side from the workspace, but always uses `server` ([CLI protocol](https://github.com/LiteLDev/lip/blob/main/protocol/cli.md#4-environment-and-workspace-operations) §4). Client workspaces are left to launchers implementing the same protocol, such as LeviLauncher.

lip implements the [lip protocol](https://github.com/LiteLDev/lip/blob/main/protocol/README.md).
