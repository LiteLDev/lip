# Internal Mechanism

This guide explains how lip works under the hood.

## Architecture

The `lip` executable provides two interfaces:

1.  **CLI**: Commands such as `lip install`, for people and scripts at a terminal. They run on their own and **do not** go through `lip rpc`.
2.  **JSON-RPC (`lip rpc`)**: The [`lip rpc`](../cli/commands/rpc.md) command, designed for external applications (IDEs, GUIs, scripts) to interact with lip programmatically.

Both interfaces share the same core logic, ensuring consistent behavior regardless of which interface is used.

`lip rpc` communicates via **JSON-RPC over stdio**, allowing tools to integrate with lip without spawning a new process for every command or parsing text output.

## Pipeline

`lip install`, `lip uninstall` and `lip update` run the same steps:

1.  **Workspace file**: Load the workspace's state from `.lip/workspace.json`. A workspace of an earlier lip version is refused (see [Workspace File](../concepts/lockfiles.md#earlier-formats)).
2.  **Environment**: Determine the platform and RID (see [Environment](../concepts/tooth_json.md#environment)).
3.  **Resolver**: Turn every source into a sequence of candidates: the archives it names, newest version first, each with the entry of the requested variant for the environment.
4.  **Graph solver**: Choose one candidate per request and per dependency, so that every identity appears once and no two mods have the same mod name.
5.  **Plan**: Plan uninstalling the packages that leave the set, in reverse installation order, then installing the packages that enter it, dependencies first, each against the workspace as the steps before it leave it. Planning downloads every archive and asset to be written and checks everything that can fail: file ownership, `preserve`, paths, the `src` of placements, the mod name in the LeviLamina `manifest.json` of mods, and the `plugins`/`mods` layout. A `tooth.json` of an earlier format has been read as format 4 while resolving (see [Earlier Formats](../concepts/tooth_json.md#earlier-formats)), so the same installer handles every package.
6.  **Apply**: Make `plugins` a link to `mods` if needed (see [Installing Dependencies](installing_dependencies.md#server-workspaces-only)), then run the planned hooks and file changes. Files placed under `plugins/` are written under `mods/` and recorded there.
7.  **Workspace file**: Save the workspace file once, after every step has succeeded.

Operations are all or nothing ([workspace protocol](https://github.com/LiteLDev/lip/blob/main/protocol/workspace.md#6-atomicity) §6, "Atomicity"). If anything fails before step 6, the workspace is not modified. If a step fails while applying (a file cannot be written, a hook command exits with a non-zero code, or you press Ctrl+C), lip undoes every change of the operation, including the layout, and leaves the workspace file as it was. What hook commands did cannot be undone: a failing `post_install` or `pre_uninstall` command still fails the operation and undoes lip's file changes, but not the effects of the commands that already ran.

## Package Resolution

lip does not use a central package registry or module proxy. A tooth path such as `github.com/user/repo` maps directly to the git repository `https://github.com/user/repo.git`, which lip talks to with its own built-in client for the git smart-HTTP protocol (version 2). **The `git` command-line tool is not required** and is never run.

-   **Versions**: lip lists the repository's tags with `ls-refs` (only `refs/tags/v*` is requested). The available versions are the tags named exactly `v<SemVer 2.0.0>` (e.g., `v1.2.3`); other tags, including lenient ones such as `v1.2` or `v01.2.3`, are ignored. Annotated tags are peeled to the commit they point to.
-   **Git refs**: A tooth path with a git ref has one candidate. `@branch:<name>` and `@tag:<name>` are resolved with `ls-refs` for exactly `refs/heads/<name>` or `refs/tags/<name>` (annotated tags peeled), once per command; `@rev:<commit>` needs no request, and the commit is fetched by its id. Abbreviated commit ids are rejected: resolving one would need the whole history, which lip never downloads.
-   **Lazy candidates**: Candidates of a tooth path are produced newest tag first, and only on demand, so lip opens only the tags the solver actually needs.
-   **tooth.json**: To read the `tooth.json` of a version, lip fetches only the trees of the tagged commit (a shallow, partial fetch with `filter blob:none`, so no file contents are transferred) and then downloads that one file. Resolving dependencies therefore costs a few kilobytes per version, regardless of the repository's size.
-   **Candidates**: A version is a candidate only when its `tooth.json` is valid, its `tooth` is the path of the repository, its `version` equals the tag version, and the requested variant has an entry for the platform and RID of the workspace. Otherwise lip moves on to the next version.
-   **Installation**: Files are downloaded only when an installer needs them: the files a `self` asset places from the package's own archive, and the `zip` and `mcmod` assets, which are downloaded when the package is installed, like any other HTTP download.
-   **Integrity**: Every downloaded git object is checked against its git object id (SHA-1). ZIP archives and assets are identified by their SHA-256.
-   **Caching**: The global cache keys every entry by the transport it came over. Downloaded git files are stored as `git-<object id>`, so identical files are downloaded once and reused across versions and packages. Everything downloaded over HTTP(S) is stored as `http-<SHA-256 of the URL>`; when lip reopens a recorded ZIP archive or asset it checks the cached file against the SHA-256 in the workspace file, and downloads it again if it is missing or differs. Downloads are written to the hidden `.staging` directory inside the cache and moved into place when complete; lip writes nothing to the system temporary directory. Tag lists and trees are kept in memory for the duration of a command; tags are looked up again on every run, so a moved tag takes effect immediately.
-   **Proxy**: If `github_proxy` is configured, requests for `github.com` repositories are first sent through the proxy. If a proxied request fails, lip retries it against `github.com` directly. Release assets downloaded from `github.com` URLs use the same rule.

A version can only be installed if its tag still exists in the repository. Only public repositories reachable over HTTPS are supported. Tooth paths are compared ignoring ASCII case.

At a branch, a commit, or a tag not named `v<SemVer>` given by a git ref, there is no tag version: the `version` of the `tooth.json` there is used. A `tooth.json` of an earlier format at a version tag is installed with the tag version, whatever its `version` says.

## Solving

-   **Pinning**: Installed packages enter the solver as they are, rebuilt from the workspace file without network access. If the request cannot be satisfied that way, lip solves it again with only the explicitly installed packages pinned, so dependencies may change version.
-   **Preference**: Earlier candidates win: the newest version of a tooth path.
-   **Order**: Packages are installed dependencies first; packages in a dependency cycle are ordered by identity.
-   **Clean-up**: Dependencies that no explicitly installed package needs any more are uninstalled.
-   **Reinstalling from the workspace file**: When lip needs an installed package's files again, it opens the archive and assets recorded in the workspace file, with the entry recorded there, and verifies their identity (SHA-256 or commit) instead of resolving the package again.

## tooth.json Validation

`tooth.json` is described by the JSON Schema [`protocol/tooth.schema.json`](https://github.com/LiteLDev/lip/blob/main/protocol/tooth.schema.json), and the workspace file by [`protocol/workspace.schema.json`](https://github.com/LiteLDev/lip/blob/main/protocol/workspace.schema.json). The earlier formats were described by JSON Schemas, written in YAML, in the former [`specs/schemas/`](https://github.com/LiteLDev/lip/blob/1e023e6/specs/schemas) directory, under `legacy/v<format>/`: `tooth.schema.yaml` for tooth.json formats 1, 2 and 3, `tooth_lock.schema.yaml` for tooth_lock.json and `liprc.schema.yaml` for liprc.json (all format 3).

-   **Validation**: Beyond the schema, lip checks what the [package protocol](https://github.com/LiteLDev/lip/blob/main/protocol/package.md#3-tooth-files) requires: a valid tooth path, version, dependency keys and ranges, platforms and RIDs, no two entries of a variant for the same platform and RID, at most one `mcmod` asset per entry, and valid globs. An archive whose `tooth.json` is invalid is not a package and yields no candidate, and lip reports why.
-   **Versioning**: A `tooth.json` of `format_version` 4 is of the current format. Formats 1 and 2 are converted to format 3 in memory when they are read, and format 3 is read as format 4 for the environment ([package protocol](https://github.com/LiteLDev/lip/blob/main/protocol/package.md#5-earlier-formats) §5); run `lip migrate` in a package repository to rewrite its `tooth.json` as format 4.

## Workspace State

lip maintains the state of your installed packages in the workspace file kept in the workspace, at `.lip/workspace.json` (see [Workspace File](../concepts/lockfiles.md#location)). This tracks:
-   Which packages are explicitly requested versus implicit dependencies.
-   The exact versions installed, and the archives and assets they came from.
-   The files each package owns.
-   The platform and RID of the workspace.

Files written to make an operation atomic are hidden and sit next to their target: the workspace file is first written to `.lip/workspace.json.new` and then renamed over `.lip/workspace.json`, and a file that an operation overwrites or deletes is first renamed to `.<name>.bak` (or `.<name>.bak1`, `.<name>.bak2`, … if that name is taken) in the same directory. The backups are deleted once the workspace file is saved (one that cannot be deleted, for example because a program has it open, is left and named in a warning), or renamed back if the operation fails. Directories that an uninstallation empties are deleted at the same time. On Windows these files also get the hidden attribute.

The journal and the workspace lock below are kept in `.lip` next to the workspace file, so they move with the workspace: a moved or copied workspace takes an interrupted operation with it, and the journal records paths relative to the workspace. Their formats are defined in [`protocol/workspace.md`](https://github.com/LiteLDev/lip/blob/main/protocol/workspace.md#7-the-lip-directory), so that other programs can take part.

-   **Journal**: While an operation changes the workspace, lip records every change, before making it, in `.lip/journal`. If lip is killed, the next `lip install`, `uninstall` or `update` of the workspace reads the journal first: it undoes the interrupted operation (restoring the backups) and reports it, or, if the operation had already reached the point of saving its workspace file, finishes it by deleting the backups. A dry run takes no lock and only reports the journal.
-   **One operation at a time**: An operation holds `.lip/lock` open exclusively (an exclusive `flock` on Linux and macOS; the file is kept). A second `lip install`, `uninstall` or `update` of the same workspace fails at once with "Another process is changing the workspace"; run it again when the first one has finished.

## Notes on the Specification

lip manages server workspaces only: it never determines the platform from the workspace, but always uses `server` ([CLI protocol](https://github.com/LiteLDev/lip/blob/main/protocol/cli.md#4-environment-and-workspace-operations) §4). Client workspaces are left to launchers implementing the same protocol, such as LeviLauncher.

lip implements the [lip protocol](https://github.com/LiteLDev/lip/blob/main/protocol/README.md). The protocol leaves open how workspaces of earlier lip versions are taken over ([workspace protocol](https://github.com/LiteLDev/lip/blob/main/protocol/workspace.md#76-earlier-formats) §7.6); lip does not take them over, and fails on such a workspace (see [Workspace File](../concepts/lockfiles.md#earlier-formats)).
