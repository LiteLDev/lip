# Architecture

`lip` manages the state of installed packages within a workspace.

## Workspace State

Represents the current state of the workspace, tracking which packages are installed and how.
- **Packages**: A collection of installed packages.
- **Manifest**: The Manifest File of the current project (if applicable).

## Configuration

Manages global configuration settings for the `lip` tool itself, such as proxies and default behaviors.
- **Properties**: Key-value pairs for configuration options (e.g., `github_proxy`).
