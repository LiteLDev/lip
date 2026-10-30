# Internal Mechanism

This guide explains how lip works under the hood.

## Architecture

lip provides two primary entry points:

1.  **CLI (`lip`)**: The standalone command-line tool. It contains its own logic for package management and **does not** depend on the Daemon.
2.  **Daemon (`lipd`)**: A separate process designed for external applications (IDEs, GUIs, scripts) to interact with lip programmatically.

Both the CLI and the Daemon share the same core logic, ensuring consistent behavior regardless of which interface is used.

The Daemon (`lipd`) communicates via **JSON-RPC over stdio**, allowing tools to integrate with lip without spawning new processes for every command or parsing text output.

## Pipeline

`lip install`, `lip uninstall` and `lip update` run the same steps:

1.  **Lock**: Load the workspace's lock from the per-user data directory, or a `lip_lock.json` or legacy `tooth_lock.json` left in the workspace by earlier versions (see [Lockfiles](../concepts/lockfiles.md)).
2.  **Environment**: Determine the platform and RID (see [Environment](../concepts/package_manifest.md#environment)).
3.  **Resolver**: Turn every ref into a sequence of candidate concrete packages, following routing documents.
4.  **Graph solver**: Choose one candidate per request and per install edge, so that every identity appears once and no `conflicts` entry is violated.
5.  **Plan**: Plan uninstalling the packages that leave the set, in reverse install order, then installing the packages that enter it, dependencies first, each against the workspace as the steps before it leave it. Planning downloads every file to be written and checks everything that can fail: file ownership, `preserveFiles`, paths, placement sources and the `plugins`/`mods` layout. A `tooth.json` package has been converted to a format 4 file package while resolving (see [Legacy: tooth.json](../concepts/package_manifest.md#legacy-tooth-json)), so the same installer handles every package.
6.  **Apply**: Make `plugins` a link to `mods` if needed (see [Installing Dependencies](installing_dependencies.md#server-workspaces-only)), then run the planned scripts and file changes. Files placed under `plugins/` are written under `mods/` and recorded there.
7.  **Lock**: Save the lock once, after every step has succeeded.

Operations are all or nothing (spec §7, "Atomicity"). If anything fails before step 6, the workspace is not modified. If a step fails while applying (a file cannot be written, a script exits with a non-zero code, or you press Ctrl+C), lip undoes every change of the operation, including the layout, and leaves the lock as it was. What scripts did cannot be undone: a failing `postInstall` or `preUninstall` script still fails the operation and undoes lip's file changes, but not the effects of the scripts that already ran.

## Package Resolution

lip does not use a central package registry or module proxy. A package path such as `github.com/user/repo` maps directly to the git repository `https://github.com/user/repo.git`, which lip talks to with its own built-in client for the git smart-HTTP protocol (version 2). **The `git` command-line tool is not required** and is never run.

-   **Versions**: lip lists the repository's tags with `ls-refs` (only `refs/tags/v*` is requested). The available versions are the tags named exactly `v<SemVer 2.0.0>` (e.g., `v1.2.3`); other tags, including lenient ones such as `v1.2` or `v01.2.3`, are ignored. Annotated tags are peeled to the commit they point to.
-   **Git refs**: A package path with a git ref has one candidate. `@branch:<name>` and `@tag:<name>` are resolved with `ls-refs` for exactly `refs/heads/<name>` or `refs/tags/<name>` (annotated tags peeled), once per command; `@rev:<commit>` needs no request, and the commit is fetched by its id. Abbreviated commit ids are rejected: resolving one would need the whole history, which lip never downloads.
-   **Lazy candidates**: Candidates of a package path are produced newest tag first, and only on demand, so lip opens only the tags the solver actually needs.
-   **Documents**: To read the `manifest.json` (or `tooth.json`) of a version, lip fetches only the trees of the tagged commit (a shallow, partial fetch with `filter blob:none`, so no file contents are transferred) and then downloads that one file. Resolving dependencies therefore costs a few kilobytes per version, regardless of the repository's size.
-   **Routing**: A routing document selects an artifact by variant and environment and resolves its refs in order. A chain longer than 8 hops, or one that visits the same archive and variant twice, yields no candidate.
-   **Installation**: Files are downloaded only when an installer needs them: the whole tree for a mod, the placed files for a file package. The release assets of a `tooth.json` package are downloaded when it is installed, like any other HTTP download.
-   **Integrity**: Every downloaded git object is checked against its git object id (SHA-1). ZIP archives are identified by their SHA-256.
-   **Caching**: The global cache keys every entry by the transport it came over. Downloaded git files are stored as `git-<object id>`, so identical files are downloaded once and reused across versions and packages. Everything downloaded over HTTP(S) is stored as `http-<SHA-256 of the URL>`, so a URL is downloaded once; when lip reopens a locked ZIP archive it checks the cached file against the SHA-256 in the lock, and downloads it again if it is missing or differs. A ZIP archive inside another archive is not cached on its own: lip reads it from its parent archive again. Downloads are written to the hidden `.staging` directory inside the cache and moved into place when complete; lip writes nothing to the system temporary directory. Tag lists and trees are kept in memory for the duration of a command; tags are looked up again on every run, so a moved tag takes effect immediately.
-   **Redirects**: Renamed or transferred GitHub repositories are followed through GitHub's HTTP redirects. `github.com` owner and repository names are matched case-insensitively.
-   **Proxy**: If `github_proxy` is configured, requests for `github.com` repositories are first sent through the proxy. If a proxied request fails, lip retries it against `github.com` directly. Release assets downloaded from `github.com` URLs use the same rule.

A version can only be installed if its tag still exists in the repository. Only public repositories reachable over HTTPS are supported.

A `manifest.json` at a tag that has a `version` must declare the tag's version. A legacy `tooth.json` at a tag that declares another version is installed with the tag version, and lip prints a warning. At a branch, a commit, or a tag not named `v<SemVer>` given by a git ref, there is no tag version: the document's own `version` is used.

## Solving

-   **Pinning**: Installed packages enter the solver as they are, rebuilt from the lock without network access. If the request cannot be satisfied that way, lip solves it again with only the explicitly installed packages pinned, so dependencies may change version.
-   **Preference**: Earlier candidates win: the newest version of a package path, and the first ref of an artifact or dependency.
-   **Order**: Packages are installed dependencies first; packages in a dependency cycle are ordered by name.
-   **Clean-up**: Dependencies that no explicitly installed package needs any more are uninstalled.
-   **Reinstalling from the lock**: When lip needs an installed package's files again, it opens the archive recorded in the lock and verifies its identity (SHA-256 or commit) instead of resolving the package again.

## Manifest Validation

The formats are described by JSON Schemas in the repository's `schemas/` directory: `manifest.v4.schema.json` for `manifest.json`, `lock.v4.schema.json` for the lock, and, for the legacy formats, `manifest.schema.json` (tooth.json format 3), `manifest.v1.schema.json`, `manifest.v2.schema.json` and `workspace_state.schema.json` (tooth_lock.json). The schemas are meant for editors and other tools; lip itself does not run JSON Schema validation.

-   **Validation**: lip parses `manifest.json` strictly (UTF-8, no comments, no duplicate keys) and rejects a document whose keys have the wrong type, such as an invalid `name`, `version`, ref, range or template. A ref to an invalid document yields no candidate, and lip reports why.
-   **Versioning**: A `manifest.json` with `formatVersion` 4 and the format UUID is a format 4 document; one without either is a plain LeviLamina manifest. `tooth.json` in format 1 or 2 is converted to format 3 in memory when it is read, and then to a format 4 file package (spec §9); run `lip migrate` in a package repository to migrate it from `tooth.json` to `manifest.json`.

## Workspace State

lip maintains the state of your installed packages in a lockfile kept outside the workspace, in the per-user data directory (see [Lockfiles](../concepts/lockfiles.md#location)). This tracks:
-   Which packages are explicitly requested versus implicit dependencies.
-   The exact versions installed, and the archives they came from.
-   The files each package owns.
-   The platform and RID of the workspace.

Files written to make an operation atomic are hidden and sit next to their target: the lock is first written to `.<name>.tmp` beside it and then renamed over it, and a file that an operation overwrites or deletes is first renamed to `.<name>.bak` (or `.<name>.bak1`, `.<name>.bak2`, … if that name is taken) in the same directory. The backups are deleted once the lock is saved (one that cannot be deleted, for example because a program has it open, is left and named in a warning), or renamed back if the operation fails. Directories that an uninstallation empties are deleted at the same time. On Windows these files also get the hidden attribute.

-   **Journal**: While an operation changes the workspace, lip records every change, before making it, in `<hash>.journal` next to the lock. If lip is killed, the next `lip install`, `uninstall` or `update` of the workspace reads the journal first: it undoes the interrupted operation (restoring the backups) and reports it, or, if the operation had already reached the point of saving its lock, finishes it by deleting the backups. A dry run only reports the journal.
-   **One operation at a time**: An operation holds `<hash>.lock` next to the lock open exclusively (deleted when it ends). A second `lip install`, `uninstall` or `update` of the same workspace fails at once with "Another lip process is changing the workspace"; run it again when the first one has finished.

## Deviations from the Specification

lip manages server workspaces only: it never determines the platform from the workspace, but always uses `server` as if the user had specified it ([CLI specification](https://github.com/LiteLDev/lip/blob/main/refs/cli.md) §3, spec §5.1). Client workspaces are left to launchers implementing the same specification, such as LeviLauncher.

lip implements the [lip package specification](https://github.com/LiteLDev/lip/blob/main/refs/package_format.md) (format version 4) with these deliberate differences:

1.  **Migrated lock entries**: Entries migrated from `tooth_lock.json` record no archive identity. When lip needs their archive, it resolves the tag `path@version` again.
2.  **Nested ZIP archives**: A ZIP archive inside another archive is locked by its SHA-256 only, and is not cached on its own. To open it again, lip resolves the recorded chain again, which reopens the parent archive and extracts the ZIP file, and accepts the result only if its SHA-256 matches.
3.  **Entries of one identity**: The variants of one `tooth.json` package have distinct identities (`path` and `path#label`, spec §9.1), also when migrated from `tooth_lock.json`, and lip gives the entries of locks written before that rule the identity it requires. Should a lock still hold two entries of one identity, both are kept and listed, and can be uninstalled separately with `path#variant`. New solves require unique identities.
