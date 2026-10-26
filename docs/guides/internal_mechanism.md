# Internal Mechanism

This guide explains how lip works under the hood.

## Architecture

lip provides two primary entry points:

1.  **CLI (`lip`)**: The standalone command-line tool. It contains its own logic for package management and **does not** depend on the Daemon.
2.  **Daemon (`lipd`)**: A separate process designed for external applications (IDEs, GUIs, scripts) to interact with lip programmatically.

Both the CLI and the Daemon share the same core logic, ensuring consistent behavior regardless of which interface is used.

The Daemon (`lipd`) communicates via **JSON-RPC over stdio**, allowing tools to integrate with lip without spawning new processes for every command or parsing text output.

## Package Resolution

lip does not use a central package registry or module proxy. A package path such as `github.com/user/repo` maps directly to the git repository `https://github.com/user/repo.git`, which lip talks to with its own built-in client for the git smart-HTTP protocol (version 2). **The `git` command-line tool is not required** and is never run.

-   **Versions**: lip lists the repository's tags with `ls-refs` (only `refs/tags/v*` is requested). The available versions are the tags of the form `v<semver>` (e.g., `v1.2.3`); tags that are not valid semantic versions are ignored. Annotated tags are peeled to the commit they point to.
-   **Manifests**: To read the `tooth.json` of a version, lip fetches only the trees of the tagged commit (a shallow, partial fetch with `filter blob:none`, so no file contents are transferred) and then downloads the single `tooth.json` blob. Resolving dependencies therefore costs a few kilobytes per version, regardless of the repository's size.
-   **Installation**: The files of a package version are listed from the same tree. lip matches the list against the `self` asset placements of the selected variant and then downloads only the matched files, in one batched request. Files that no placement matches are never downloaded.
-   **Integrity**: Every downloaded object is checked against its git object id (SHA-1).
-   **Caching**: Downloaded files are stored in the global cache by object id (`git-blob:<id>`), so identical files are downloaded once and reused across versions and packages. Tag lists and trees are kept in memory for the duration of a command; tags are looked up again on every run, so a moved tag takes effect immediately.
-   **Redirects**: Renamed or transferred GitHub repositories are followed through GitHub's HTTP redirects. The package keeps the path it was requested with, and `github.com` owner and repository names are matched case-insensitively.
-   **Proxy**: If `github_proxy` is configured, requests for `github.com` repositories are first sent through the proxy. If a proxied request fails, lip retries it against `github.com` directly. Release assets downloaded from `github.com` URLs use the same rule.

A version can only be installed if its tag still exists in the repository. Only public repositories reachable over HTTPS are supported.

The manifest at a tag identifies the package's contents, but not its identity: if its `tooth` path or `version` differs from the requested package path or tag (for example after a repository was renamed), lip prints a warning and records the package under the requested path and version.

## Manifest Validation

The `tooth.json` format is described by JSON Schemas in the repository's `schemas/` directory: `manifest.schema.json` for the current format (version 3), and `manifest.v1.schema.json` and `manifest.v2.schema.json` for the older formats. The schemas are meant for editors and other tools; lip itself does not run JSON Schema validation.

-   **Validation**: When loading a manifest, lip parses it into its typed model and rejects invalid values, such as a `tooth` that is not a valid Go module path, an invalid `version`, tag, or variant label, or a wrong `format_uuid`.
-   **Versioning**: lip reads the `format_version` field to know how to interpret the file. Manifests in format version 1 or 2 are converted to version 3 in memory when they are read; run `lip migrate` to rewrite such a file in the current format.

## Workspace State

lip maintains the state of your installed packages in the lockfile `tooth_lock.json` in the workspace root (see [Lockfiles](../concepts/lockfiles.md)). This tracks:
-   Which packages are explicitly requested versus implicit dependencies.
-   The exact versions installed.
-   Metadata about where and how files were placed.
