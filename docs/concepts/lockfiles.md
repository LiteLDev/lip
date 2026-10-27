# Lockfiles

`lip` uses a lockfile, `lip_lock.json` in the workspace root, to maintain the state of the current workspace. This file records exactly which packages are installed, their specific versions, where they came from, and the files they placed on the disk.

## Purpose

The lockfile serves several critical purposes in `lip`:

1.  **State Tracking**: It acts as the single source of truth for what is currently installed in the workspace.
2.  **Reproducibility**: It records the exact archive of every installed package. When lip needs a package again (for example to uninstall it), it opens that archive directly and verifies its identity instead of resolving the package again.
3.  **Uninstallation**: `lip` tracks every file written to disk by a package. This allows `lip uninstall` to cleanly delete all files associated with a package, preventing "file rot" in the workspace.
4.  **Dependency Graph**: It distinguishes between packages explicitly installed by the user and those installed implicitly as dependencies. This allows `lip uninstall` to also remove dependencies that no explicitly installed package needs any more (unless `--no-dependencies` is given), and `lip list` to show the two groups separately.
5.  **Environment**: It records the platform and RID of the workspace once they are known, so later commands use the same [environment](package_manifest.md#environment).

The lockfile is saved after every package that is installed or uninstalled, so it matches the workspace even if a later step fails.

## Structure

The lockfile is a JSON file described by the JSON Schema [`lock.v4.schema.json`](https://github.com/LiteLDev/lip/blob/main/schemas/lock.v4.schema.json):

```json
{
  "formatVersion": 4,
  "formatUuid": "0c6b977a-64ad-441f-9a29-c7f4b196882f",
  "platform": "server",
  "rid": "win-x64",
  "packages": [
    ...
  ]
}
```

### Fields

*   **`formatVersion`**: The version of the lockfile format (currently `4`).
*   **`formatUuid`**: A unique identifier ensuring the file type is correct.
*   **`platform`**: The platform of the workspace. lip writes `"server"`; it is `null` in a lock migrated from `tooth_lock.json` until lip saves it.
*   **`rid`**: The [.NET RID](https://learn.microsoft.com/en-us/dotnet/core/rid-catalog) of the workspace.
*   **`packages`**: The installed packages, in install order.

### Package Entry

*   **`name`**: The identity of the package: the `name` of its `manifest.json`, or the `tooth` path of a `tooth.json` package.
*   **`version`**: The installed version.
*   **`kind`**: `"mcmod"` for a `manifest.json` package, `"tooth"` for a `tooth.json` package.
*   **`explicit`**:
    *   `true`: The package was explicitly installed by the user (e.g., `lip install <package>`).
    *   `false`: The package was installed implicitly as a dependency of another package.
*   **`chain`**: How the package was resolved: the `ref` of every hop, and the index of the routing `artifact` chosen at that hop (`null` when the hop was not a routing document).
*   **`archiveKey`**: The key of the final archive, e.g. `github.com/user/repo@1.0.0`.
*   **`archive`**: The identity of the final archive:
    *   `{ "kind": "zip", "sha256": ..., "url": ... }`: a ZIP archive. `url` is `null` for a ZIP file nested in another archive; `path` is set when the package is a directory inside the ZIP file.
    *   `{ "kind": "git", "repo": ..., "commit": ..., "path": ... }`: a commit of a git repository; `path` is `""` for the repository root.
    *   `{ "kind": "directory", "path": ... }`: a local directory.
    *   `null`: an entry migrated from `tooth_lock.json`.
*   **`files`**: The files the package owns, relative to the workspace root and `/`-separated.
*   **`manifest`**: The package's document (`manifest.json`, or `tooth.json` for `tooth` packages), so lip does not have to fetch it again.

## Example

```json
{
  "name": "LegacyMoney",
  "version": "0.22.0",
  "kind": "mcmod",
  "explicit": true,
  "chain": [
    { "ref": "github.com/LiteLDev/LegacyMoney", "artifact": 0 },
    {
      "ref": "https://github.com/LiteLDev/LegacyMoney/releases/download/v0.22.0/LegacyMoney-server-win-x64.mcmod",
      "artifact": null
    }
  ],
  "archiveKey": "https://github.com/LiteLDev/LegacyMoney/releases/download/v0.22.0/LegacyMoney-server-win-x64.mcmod",
  "archive": {
    "kind": "zip",
    "sha256": "…",
    "url": "https://github.com/LiteLDev/LegacyMoney/releases/download/v0.22.0/LegacyMoney-server-win-x64.mcmod"
  },
  "files": [
    "plugins/LegacyMoney/LegacyMoney.dll",
    "plugins/LegacyMoney/manifest.json"
  ],
  "manifest": { "...": "..." }
}
```

## Legacy: tooth_lock.json

lip v0.x used `tooth_lock.json` (format 3). If a workspace has no `lip_lock.json` but has a `tooth_lock.json`, lip migrates it when it loads it, writes `lip_lock.json` on the next save and deletes `tooth_lock.json`. Migrated entries have the kind `"tooth"` and no `archive`; when lip needs their files it resolves the tag `path@version` again.

The old format is described by [`workspace_state.schema.json`](https://github.com/LiteLDev/lip/blob/main/schemas/workspace_state.schema.json):

```json
{
  "format_version": 3,
  "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
  "packages": [
    {
      "manifest": { "...": "the tooth.json" },
      "variant": "",
      "locked": true,
      "files": ["bin/example.exe"]
    }
  ]
}
```

*   **`manifest`**: The full `tooth.json` of the package.
*   **`variant`**: The label of the installed variant.
*   **`locked`**: `true` if the package was explicitly installed.
*   **`files`**: The files the package placed.
