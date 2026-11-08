# Architecture

`lip` manages the state of installed packages within a workspace.

## Workspace State

Represents the current state of the workspace, tracking which packages are installed and how.
- **Packages**: The installed packages, recorded in the [state file](lockfiles.md), which lip keeps in the workspace as `.lip/state.json`.
- **Environment**: The platform (`server` or `client`) and the .NET RID of the workspace. See [Environment](lip_toml.md#environment).

## Configuration

Manages global configuration settings for the `lip` tool itself, such as proxies and default behaviors.
- **Properties**: Key-value pairs for configuration options (e.g., `github_proxy`).

## Package Sources

A package is an archive with a `lip.toml` at its root. See [lip.toml](lip_toml.md).
- **git repositories**: A package id such as `github.com/user/repo` is the git repository `https://github.com/user/repo.git`, read with lip's built-in git smart-HTTP (protocol version 2) client; the `git` command-line tool is not required. There is no central registry: the versions are the repository's `v<semver>` tags, and a git ref (`@branch:`, `@tag:`, `@rev:`) picks one commit instead.
- **URLs**: ZIP archives downloaded over HTTPS.
- **Local files and directories**: ZIP files and directories with a `lip.toml` at their root, given to `lip install` by path.
- **Cache**: Stores HTTP(S) downloads by the SHA-256 of their URL (`http-<hash>`). Git archives are not cached.

See [Internal Mechanism](../guides/internal_mechanism.md#package-resolution) for details.
