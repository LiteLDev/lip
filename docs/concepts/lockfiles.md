# Lockfiles

`lip` uses a lockfile, `tooth_lock.json` in the workspace root, to maintain the state of the current workspace. This file records exactly which packages are installed, their specific versions, the variants used, and the files they placed on the disk.

## Purpose

The lockfile serves several critical purposes in `lip`:

1.  **State Tracking**: It acts as the single source of truth for what is currently installed in the workspace.
2.  **Reproducibility**: By recording the exact version and variant of every installed package, the lockfile ensures that the workspace state is deterministic.
3.  **Uninstallation**: `lip` tracks every file written to disk by a package. This allows `lip uninstall` to cleanly delete all files associated with a package, preventing "file rot" in the workspace.
4.  **Dependency Graph**: It distinguishes between packages explicitly installed by the user and those installed implicitly as dependencies. This allows `lip uninstall` to also remove dependencies that no explicitly installed package needs any more (unless `--no-dependencies` is given), and `lip list` to show the two groups separately.

## Structure

The lockfile is a JSON file with the following high-level structure:

```json
{
  "format_version": 3,
  "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
  "packages": [
    ...
  ]
}
```

### Fields

*   **`format_version`**: An integer indicating the version of the lockfile format (currently `3`).
*   **`format_uuid`**: A unique identifier ensuring the file type is correct.
*   **`packages`**: A list of `WorkspaceStatePackage` objects representing the installed packages.

### Package Entry

Each entry in the `packages` list represents a single installed package and contains:

*   **`manifest`**: The full content of the package's manifest (`tooth.json`). This ensures `lip` has all metadata about the installed package without querying the package's repository again.
*   **`variant`**: The label of the package variant that was installed (e.g., `debug`). The default variant has the empty label `""`.
*   **`locked`**: A boolean flag.
    *   `true`: The package was explicitly installed by the user (e.g., `lip install <package>`).
    *   `false`: The package was installed implicitly as a dependency of another package.
*   **`files`**: A list of file paths (relative to the workspace root) that belong to this package. This list is used during uninstallation to remove the package's files.

## Example

An example entry for a package `github.com/example/cli` might look like this:

```json
{
  "manifest": {
    "format_version": 3,
    "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
    "tooth": "github.com/example/cli",
    "version": "1.0.0",
    "info": {
      "name": "Example CLI",
      "description": "An example CLI tool",
      "tags": [],
      "avatar_url": ""
    },
    "variants": [
      ...
    ]
  },
  "variant": "",
  "locked": true,
  "files": [
    "bin/example.exe",
    "lib/example.dll"
  ]
}
```
