# Architecture

`lip` manages the state of installed packages within a workspace.

## Workspace State

Represents the current state of the workspace, tracking which packages are installed and how.
- **Packages**: The installed packages, recorded in the [lockfile](lockfiles.md), which lip keeps in its per-user data directory.
- **Environment**: The platform (`server` or `client`) and the .NET RID of the workspace. See [Environment](package_manifest.md#environment).

## Configuration

Manages global configuration settings for the `lip` tool itself, such as proxies and default behaviors.
- **Properties**: Key-value pairs for configuration options (e.g., `github_proxy`).

## Package Sources

A package is a `manifest.json` (or a legacy `tooth.json`) at the root of an archive. See [Package Manifest](package_manifest.md).
- **git repositories**: A package path such as `github.com/user/repo` is the git repository `https://github.com/user/repo.git`, read with lip's built-in git smart-HTTP (protocol version 2) client; the `git` command-line tool is not required. There is no central registry: the versions are the repository's `v<semver>` tags.
- **ZIP archives**: `.mcmod` files and other ZIP files, downloaded over HTTPS or found inside another archive.
- **Directories**: Local directories, including directories inside an archive.
- **Cache**: Stores downloaded git objects by object id (`git-<id>`), so they are shared across versions and packages, and HTTP(S) downloads by the SHA-256 of their URL (`http-<hash>`).

See [Internal Mechanism](../guides/internal_mechanism.md#package-resolution) for details.
