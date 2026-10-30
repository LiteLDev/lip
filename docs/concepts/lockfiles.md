# Lockfiles

`lip` uses a lockfile to maintain the state of the current workspace. This file records exactly which packages are installed, their specific versions, where they came from, and the files they placed on the disk.

## Location

The lockfile is `.lip/lock.json` in the workspace root. On Windows lip creates the `.lip` directory with the hidden attribute; elsewhere its name hides it.

Because the lock lives in the workspace directory rather than being tied to its path, a workspace that is moved, renamed or copied keeps its lock, and so its installed packages.

`.lip` is reserved for lip: installing a package that would write a file to `.lip` at the workspace root, or anywhere below it, fails, and a `removeFiles` pattern never matches `.lip` or anything below it.

lip v0.x kept the lock in `tooth_lock.json` in the workspace root. When there is no `.lip/lock.json`, lip migrates it (see [below](#legacy-tooth-lock-json)).

## Purpose

The lockfile serves several critical purposes in `lip`:

1.  **State Tracking**: It acts as the single source of truth for what is currently installed in the workspace.
2.  **Reproducibility**: It records the exact archive of every installed package. When lip needs a package again (for example to uninstall it), it opens that archive directly and verifies its identity instead of resolving the package again.
3.  **Uninstallation**: `lip` tracks every file written to disk by a package. This allows `lip uninstall` to cleanly delete all files associated with a package, preventing "file rot" in the workspace.
4.  **Dependency Graph**: It distinguishes between packages explicitly installed by the user and those installed implicitly as dependencies. This allows `lip uninstall` to also remove dependencies that no explicitly installed package needs any more (unless `--no-dependencies` is given), and `lip list` to show the two groups separately.
5.  **Environment**: It records the platform and RID of the workspace once they are known, so later commands use the same [environment](package_manifest.md#environment).

The lockfile is saved once, after every package of an operation has been installed or uninstalled. If the operation fails, lip undoes its changes to the workspace and the lockfile stays as it was, so the two always match (see [Internal Mechanism](../guides/internal_mechanism.md#pipeline)).

## Structure

The lockfile is a JSON file described by the JSON Schema [`lock.v4.schema.json`](https://github.com/LiteLDev/lip/blob/main/refs/schemas/lock.v4.schema.json):

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

*   **`name`**: The identity of the package: the `name` of its `manifest.json`, or for a `tooth.json` package its `tooth` path, followed by `#<label>` for a labeled variant (e.g. `github.com/LiteLDev/LegacyScriptEngine#lua`).
*   **`version`**: The installed version.
*   **`kind`**: `"mcmod"` for a `manifest.json` package, `"tooth"` for a `tooth.json` package. Both are installed and uninstalled the same way; the kind says what `manifest` holds.
*   **`explicit`**:
    *   `true`: The package was explicitly installed by the user (e.g., `lip install <package>`).
    *   `false`: The package was installed implicitly as a dependency of another package.
*   **`chain`**: How the package was resolved: one entry per hop, with
    *   `ref`: the ref of the hop as it was resolved, e.g. `github.com/user/repo`, `github.com/user/repo@branch:main` for a [git ref](../cli/commands/install.md#git-refs), an `https://` URL, `./sub/` inside the previous archive, or the `file:///` URI of a local file given to `lip install`;
    *   `artifact`: the index of the routing `artifact` chosen at that hop (`null` when the hop was not a routing document);
    *   `commit` (only for package paths): the commit the hop's repository was at: the commit of the version tag, or the commit the git ref resolved to. Entries migrated from `tooth_lock.json` have none.

    The first hop is what [`lip update`](../cli/commands/update.md) requests again: a branch is followed to its new head, a tag is resolved again, a rev stays.
*   **`archiveKey`**: The key of the final archive, e.g. `github.com/user/repo@1.0.0`, or `github.com/user/repo@rev:<commit>` for a package path with a git ref.
*   **`archive`**: The identity of the final archive:
    *   `{ "kind": "zip", "sha256": ..., "url": ... }`: a ZIP archive. `url` is the `https://` URL it was downloaded from, the `file:///` URI of a local file, or `null` for a ZIP file nested in another archive; `path` is set when the package is a directory inside the ZIP file.
    *   `{ "kind": "git", "repo": ..., "commit": ..., "path": ... }`: a commit of a git repository; `path` is `""` for the repository root.
    *   `null`: an entry migrated from `tooth_lock.json`.

    When lip needs the files of an installed package again, it opens this archive and checks its identity: a git archive is fetched at the recorded commit (even if it came from a branch that has moved on), and a downloaded or local ZIP file must still have the recorded SHA-256. Uninstalling needs neither.
*   **`files`**: The files the package owns, relative to the workspace root and `/`-separated. Since `plugins` is a link to `mods` on a server, a file under `plugins/` is recorded under `mods/`; lip rewrites the `plugins/` paths of entries migrated from `tooth_lock.json`.
*   **`manifest`**: The package's document (`manifest.json`, or `tooth.json` for `tooth` packages, which lip converts to format 4 again when it needs the package), so lip does not have to fetch it again.

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
    "mods/LegacyMoney/LegacyMoney.dll",
    "mods/LegacyMoney/manifest.json"
  ],
  "manifest": { "...": "..." }
}
```

## Legacy: tooth_lock.json

lip v0.x used `tooth_lock.json` (format 3) in the workspace root. If there is no `.lip/lock.json` but there is a `tooth_lock.json`, lip migrates it when it loads it, writes `.lip/lock.json` on the next save and deletes `tooth_lock.json`. Migrated entries have the kind `"tooth"` and no `archive`; when lip needs their files it resolves the tag `path@version` again.

`tooth_lock.json` recorded the absolute paths of the files. If the workspace was moved after those packages were installed, the paths point to its old location: lip infers the old root from where the recorded files now exist under the workspace and rebases the paths onto the workspace, so the packages still own their files and can be uninstalled.

The old format is described by [`workspace_state.schema.json`](https://github.com/LiteLDev/lip/blob/main/refs/schemas/workspace_state.schema.json):

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
