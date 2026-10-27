# Lockfiles

`lip` uses a lockfile to maintain the state of the current workspace. This file records exactly which packages are installed, their specific versions, where they came from, and the files they placed on the disk.

## Location

The lockfile is kept outside the workspace, in lip's per-user data directory, so the workspace holds only the files of its packages:

| Platform | Path |
| --- | --- |
| Windows | `%LOCALAPPDATA%\lip\workspaces\<hash>.json` |
| Linux / macOS | `$XDG_DATA_HOME/lip/workspaces/<hash>.json` (by default `~/.local/share/lip/workspaces/<hash>.json`) |

`<hash>` is the lowercase hexadecimal SHA-256 of the workspace's absolute path, without a trailing separator (lower-cased on Windows, where paths are case-insensitive). The lockfile also records that path in `workspace`; if they do not match, lip stops with an error instead of using another workspace's lock.

Because the lock belongs to the path, moving or renaming a workspace directory leaves its lock behind: lip treats the moved workspace as one with no packages installed.

### Migration from the workspace

Earlier versions kept the lock in the workspace root, as `lip_lock.json` (format 4) or `tooth_lock.json` (format 3). When the data directory has no lock for a workspace, lip reads `lip_lock.json`, or else migrates `tooth_lock.json` (see [below](#legacy-tooth-lock-json)). The next time it saves the lock (after installing, uninstalling or updating a package), lip writes it to the data directory and deletes both files from the workspace.

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
  "workspace": "C:\\bds",
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
*   **`workspace`**: The absolute path of the workspace the lockfile belongs to.
*   **`platform`**: The platform of the workspace. lip writes `"server"`; it is `null` in a lock migrated from `tooth_lock.json` until lip saves it.
*   **`rid`**: The [.NET RID](https://learn.microsoft.com/en-us/dotnet/core/rid-catalog) of the workspace.
*   **`packages`**: The installed packages, in install order.

### Package Entry

*   **`name`**: The identity of the package: the `name` of its `manifest.json`, or the `tooth` path of a `tooth.json` package.
*   **`version`**: The installed version.
*   **`kind`**: `"mcmod"` for a `manifest.json` package, `"tooth"` for a `tooth.json` package. Both are installed and uninstalled the same way; the kind says what `manifest` holds.
*   **`explicit`**:
    *   `true`: The package was explicitly installed by the user (e.g., `lip install <package>`).
    *   `false`: The package was installed implicitly as a dependency of another package.
*   **`chain`**: How the package was resolved: one entry per hop, with
    *   `ref`: the ref of the hop as it was resolved, e.g. `github.com/user/repo`, `github.com/user/repo@branch:main` for a [git ref](../cli/commands/install.md#git-refs), an `https://` URL, `./sub/` inside the previous archive, or the `file:///` URI of a local file given to `lip install`;
    *   `artifact`: the index of the routing `artifact` chosen at that hop (`null` when the hop was not a routing document);
    *   `commit` (only for package paths): the commit the hop's repository was at: the commit of the version tag, or the commit the git ref resolved to. Locks written by earlier versions have none.

    The first hop is what [`lip update`](../cli/commands/update.md) requests again: a branch is followed to its new head, a tag is resolved again, a rev stays.
*   **`archiveKey`**: The key of the final archive, e.g. `github.com/user/repo@1.0.0`, or `github.com/user/repo@rev:<commit>` for a package path with a git ref.
*   **`archive`**: The identity of the final archive:
    *   `{ "kind": "zip", "sha256": ..., "url": ... }`: a ZIP archive. `url` is the `https://` URL it was downloaded from, the `file:///` URI of a local file, or `null` for a ZIP file nested in another archive; `path` is set when the package is a directory inside the ZIP file.
    *   `{ "kind": "git", "repo": ..., "commit": ..., "path": ... }`: a commit of a git repository; `path` is `""` for the repository root.
    *   `null`: an entry migrated from `tooth_lock.json`.

    When lip needs the files of an installed package again, it opens this archive and checks its identity: a git archive is fetched at the recorded commit (even if it came from a branch that has moved on), and a downloaded or local ZIP file must still have the recorded SHA-256. Uninstalling needs neither.
*   **`files`**: The files the package owns, relative to the workspace root and `/`-separated. Since `plugins` is a link to `mods` on a server, a file under `plugins/` is recorded under `mods/`; lip rewrites `plugins/` paths in locks written by earlier versions when it loads them.
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

lip v0.x used `tooth_lock.json` (format 3) in the workspace root. If there is no lock in the data directory and no `lip_lock.json` in the workspace, but there is a `tooth_lock.json`, lip migrates it when it loads it, writes the lock to the data directory on the next save and deletes `tooth_lock.json`. Migrated entries have the kind `"tooth"` and no `archive`; when lip needs their files it resolves the tag `path@version` again.

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
