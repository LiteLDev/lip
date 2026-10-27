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

1.  **Lock**: Load `lip_lock.json`, or migrate a legacy `tooth_lock.json` (see [Lockfiles](../concepts/lockfiles.md)).
2.  **Environment**: Determine the platform and RID (see [Environment](../concepts/package_manifest.md#environment)).
3.  **Resolver**: Turn every ref into a sequence of candidate concrete packages, following routing documents.
4.  **Graph solver**: Choose one candidate per request and per install edge, so that every identity appears once and no `conflicts` entry is violated.
5.  **Installers**: Uninstall the packages that leave the set, in reverse install order, then install the packages that enter it, dependencies first. `manifest.json` packages and `tooth.json` packages have their own installer.
6.  **Lock**: Save the lock after every package installed or uninstalled.

If anything fails while resolving or solving, the workspace is not modified.

## Package Resolution

lip does not use a central package registry or module proxy. A package path such as `github.com/user/repo` maps directly to the git repository `https://github.com/user/repo.git`, which lip talks to with its own built-in client for the git smart-HTTP protocol (version 2). **The `git` command-line tool is not required** and is never run.

-   **Versions**: lip lists the repository's tags with `ls-refs` (only `refs/tags/v*` is requested). The available versions are the tags named exactly `v<SemVer 2.0.0>` (e.g., `v1.2.3`); other tags, including lenient ones such as `v1.2` or `v01.2.3`, are ignored. Annotated tags are peeled to the commit they point to.
-   **Lazy candidates**: Candidates of a package path are produced newest tag first, and only on demand, so lip opens only the tags the solver actually needs.
-   **Documents**: To read the `manifest.json` (or `tooth.json`) of a version, lip fetches only the trees of the tagged commit (a shallow, partial fetch with `filter blob:none`, so no file contents are transferred) and then downloads that one file. Resolving dependencies therefore costs a few kilobytes per version, regardless of the repository's size.
-   **Routing**: A routing document selects an artifact by variant and environment and resolves its refs in order. A chain longer than 8 hops, or one that visits the same archive and variant twice, yields no candidate.
-   **Installation**: Files are downloaded only when an installer needs them: the whole tree for a mod, the placed files for a file package or a `tooth.json` package.
-   **Integrity**: Every downloaded git object is checked against its git object id (SHA-1). ZIP archives are identified by their SHA-256.
-   **Caching**: Downloaded git files are stored in the global cache by object id, and ZIP archives by SHA-256, so identical files are downloaded once and reused across versions and packages. A URL is downloaded once. Tag lists and trees are kept in memory for the duration of a command; tags are looked up again on every run, so a moved tag takes effect immediately.
-   **Redirects**: Renamed or transferred GitHub repositories are followed through GitHub's HTTP redirects. `github.com` owner and repository names are matched case-insensitively.
-   **Proxy**: If `github_proxy` is configured, requests for `github.com` repositories are first sent through the proxy. If a proxied request fails, lip retries it against `github.com` directly. Release assets downloaded from `github.com` URLs use the same rule.

A version can only be installed if its tag still exists in the repository. Only public repositories reachable over HTTPS are supported.

A `manifest.json` at a tag that has a `version` must declare the tag's version. A legacy `tooth.json` at a tag that declares another version is installed with the tag version, and lip prints a warning.

## Solving

-   **Pinning**: Installed packages enter the solver as they are, rebuilt from the lock without network access. If the request cannot be satisfied that way, lip solves it again with only the explicitly installed packages pinned, so dependencies may change version.
-   **Preference**: Earlier candidates win: the newest version of a package path, and the first ref of an artifact or dependency.
-   **Order**: Packages are installed dependencies first; packages in a dependency cycle are ordered by name.
-   **Clean-up**: Dependencies that no explicitly installed package needs any more are uninstalled.
-   **Reinstalling from the lock**: When lip needs an installed package's files again, it opens the archive recorded in the lock and verifies its identity (SHA-256 or commit) instead of resolving the package again.

## Manifest Validation

The formats are described by JSON Schemas in the repository's `schemas/` directory: `manifest.v4.schema.json` for `manifest.json`, `lock.v4.schema.json` for `lip_lock.json`, and, for the legacy formats, `manifest.schema.json` (tooth.json format 3), `manifest.v1.schema.json`, `manifest.v2.schema.json` and `workspace_state.schema.json` (tooth_lock.json). The schemas are meant for editors and other tools; lip itself does not run JSON Schema validation.

-   **Validation**: lip parses `manifest.json` strictly (UTF-8, no comments, no duplicate keys) and rejects a document whose keys have the wrong type, such as an invalid `name`, `version`, ref, range or template. A ref to an invalid document yields no candidate, and lip reports why.
-   **Versioning**: A `manifest.json` with `formatVersion` 4 and the format UUID is a format 4 document; one without either is a plain LeviLamina manifest. `tooth.json` in format 1 or 2 is converted to format 3 in memory when it is read; run `lip migrate` to convert a `tooth.json` to `manifest.json`.

## Workspace State

lip maintains the state of your installed packages in the lockfile `lip_lock.json` in the workspace root (see [Lockfiles](../concepts/lockfiles.md)). This tracks:
-   Which packages are explicitly requested versus implicit dependencies.
-   The exact versions installed, and the archives they came from.
-   The files each package owns.
-   The platform and RID of the workspace.

## Deviations from the Specification

lip implements the lip package specification (format version 4) with these deliberate differences:

1.  **Server only**: lip does not determine the platform from the workspace (spec §5.1); it always uses `server`. Client workspaces are left to launchers implementing the same specification, such as LeviLauncher.
2.  **Migrated lock entries**: Entries migrated from `tooth_lock.json` record no archive identity. When lip needs their archive, it resolves the tag `path@version` again.
3.  **Nested ZIP archives**: A ZIP archive inside another archive is locked by its SHA-256 only, and found again in the cache. If the cache was cleaned, lip resolves the recorded chain again and accepts the result only if its SHA-256 matches.
4.  **Two variants of one tooth**: A `tooth_lock.json` may hold two variants of one `tooth.json` package under the same identity. Both entries are kept and listed, and can be uninstalled separately with `path#variant`. New solves require unique identities.
