# Internal Mechanism

This guide explains how lip works under the hood.

## Architecture

lip provides two primary entry points:

1.  **CLI (`lip`)**: The standalone command-line tool. It contains its own logic for package management and **does not** depend on the Daemon.
2.  **Daemon (`lipd`)**: A separate process designed for external applications (IDEs, GUIs, scripts) to interact with lip programmatically.

Both the CLI and the Daemon share the same core logic, ensuring consistent behavior regardless of which interface is used.

The Daemon (`lipd`) communicates via **JSON-RPC over stdio**, allowing tools to integrate with lip without spawning new processes for every command or parsing text output.

## Package Resolution

lip does not use a central package registry. A package path such as `github.com/user/repo` maps directly to the git repository `https://github.com/user/repo.git`:

-   **Versions**: The available versions of a package are the repository's git tags of the form `v<semver>` (e.g., `v1.2.3`). Tags that are not valid semantic versions are ignored.
-   **Downloads**: To install a specific version, lip fetches the repository contents at the corresponding `v<semver>` tag and reads `tooth.json` from it.
-   **Proxy**: If `github_proxy` is configured, requests to `github.com` repositories are routed through it.

A version can only be installed if its tag still exists in the repository.

## Manifest Validation

lip uses JSON Schema to validate `tooth.json` files. The schema is defined in `schemas/tooth.v3.schema.json`.

-   **Validation**: When loading a manifest, lip checks it against this schema to ensure all required fields are present and have the correct types.
-   **Versioning**: The `format_version` field in `tooth.json` corresponds to the schema version. lip checks this version to know how to interpret the file. If the version is older than supported, you may need to run `lip migrate`.

## Workspace State

lip maintains the state of your installed packages in a local database (currently JSON-based) within the `.lip` directory. This tracks:
-   Which packages are explicitly requested versus implicit dependencies.
-   The exact versions installed.
-   Metadata about where and how files were placed.
