# Architecture

`lip` manages the state of installed packages within a workspace.

## Workspace State

Represents the current state of the workspace, tracking which packages are installed and how.
- **Packages**: A collection of installed packages.
- **Manifest**: The Manifest File of the current project (if applicable).

## Configuration

Manages global configuration settings for the `lip` tool itself, such as proxies and default behaviors.
- **Properties**: Key-value pairs for configuration options (e.g., `github_proxy`).

## Package Sources

Packages are resolved and downloaded directly from their git repositories over HTTPS, using lip's built-in git smart-HTTP (protocol version 2) client; the `git` command-line tool is not required.
- **Registry**: Lists a package's versions from its `v<semver>` tags and reads `tooth.json` at a tag without downloading any other file.
- **Sources**: Expose the file list of a tagged commit and download file contents lazily, only for the files that the manifest's placements select.
- **Cache**: Stores downloaded files by git object id, so they are shared across versions and packages.

See [Internal Mechanism](../guides/internal_mechanism.md#package-resolution) for details.
