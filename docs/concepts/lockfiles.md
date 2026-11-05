# Workspace File

`lip` records the state of a workspace in its **workspace file**: which packages are installed, their exact versions, where they came from, and the files they placed on the disk.

## Location

The workspace file is `.lip/workspace.json` in the workspace root. On Windows lip creates the `.lip` directory with the hidden attribute; elsewhere its name hides it.

Because the workspace file lives in the workspace directory rather than being tied to its path, a workspace that is moved, renamed or copied keeps its workspace file, and so its installed packages.

`.lip` is reserved for lip: installing a package that would write a file to `.lip` at the workspace root, or anywhere below it, fails, and a `remove_files` pattern never matches `.lip` or anything below it.

## Purpose

The workspace file serves several purposes:

1.  **State Tracking**: It is the single source of truth for what is installed in the workspace.
2.  **Reproducibility**: It records the exact archive and assets of every installed package, and the entry that was installed. When lip needs a package again, it opens that archive and those assets and checks that they have not changed, instead of resolving the package again.
3.  **Uninstallation**: `lip` tracks every file written to disk by a package. This allows `lip uninstall` to cleanly delete all files associated with a package, preventing "file rot" in the workspace.
4.  **Dependency Graph**: It distinguishes between packages explicitly installed by the user and those installed as dependencies. This allows `lip uninstall` to also remove dependencies that no explicitly installed package needs any more (unless `--no-dependencies` is given), and `lip list` to show the two groups separately.
5.  **Environment**: It records the platform and RID of the workspace once they are known, so later commands use the same [environment](tooth_json.md#environment).

The workspace file is replaced once, after every package of an operation has been installed or uninstalled. If the operation fails, lip undoes its changes to the workspace and the workspace file stays as it was, so the two always match (see [Internal Mechanism](../guides/internal_mechanism.md#pipeline)).

## Structure

The workspace file is a JSON file described by the JSON Schema [`workspace.schema.json`](https://github.com/LiteLDev/lip/blob/main/protocol/workspace.schema.json):

```json
{
  "format_version": 4,
  "platform": "server",
  "runtime": "win-x64",
  "packages": [
    ...
  ]
}
```

### Fields

*   **`format_version`**: The format of the workspace file, `4`.
*   **`platform`**: The platform of the workspace, `null` until it is known. lip writes `"server"`.
*   **`runtime`**: The [.NET RID](https://learn.microsoft.com/dotnet/core/rid-catalog) of the workspace, `null` until it is known.
*   **`packages`**: The installed packages, in installation order: a package comes after the packages that satisfy its dependencies.

### Package Entry

*   **`tooth`**: The tooth path of the package, from its `tooth.json`.
*   **`variant`**: The variant of the source it was installed from, `""` for the default variant. With `tooth`, it makes the [identity](tooth_json.md#identity) of the package.
*   **`version`**: The installed version.
*   **`explicit`**:
    *   `true`: The package was explicitly installed by the user (e.g., `lip install <package>`).
    *   `false`: The package was installed as a dependency of another package.
*   **`source`**: What the package was installed from: the argument given to `lip install` or `lip update` (e.g. `github.com/user/repo`, `github.com/user/repo#lua@branch:main` or an `https://` URL; a local path is recorded as the `file:` URI of its absolute path), or, for a dependency, the key of the dependency. [`lip update`](../cli/commands/update.md) requests it again.
*   **`archive`**: The archive of the package:
    *   `{ "kind": "git", "repo": ..., "commit": ... }`: a commit of a git repository; `repo` is `https://<repo>.git`.
    *   `{ "kind": "zip", "sha256": ..., "url": ... }`: a ZIP archive. `url` is the `https://` URL it was downloaded from, without the `#<variant>`, or the `file:` URI of a local file.
    *   `{ "kind": "directory", "path": ... }`: a local directory, by its absolute path.
*   **`assets`**: One member for each asset of the entry, in order: `null` for a `self` asset, and `{ "url": ..., "sha256": ... }` for a downloaded one, `url` being the URL it was downloaded from.
*   **`entry`**: The entry of the `tooth.json` that was installed. For a `tooth.json` of an [earlier format](tooth_json.md#earlier-formats), it is the format 4 entry lip read it as.
*   **`files`**: The files the package owns, relative to the workspace root and `/`-separated. Since `plugins` is a link to `mods` on a server, a file under `plugins/` is recorded under `mods/`.

SHA-256 values and commit ids are written in lowercase hexadecimal.

When lip needs the files of an installed package again, it installs what the workspace file records, whatever the source names now: a git archive is fetched at the recorded commit (even if it came from a branch that has moved on), and a ZIP archive or asset must still have the recorded SHA-256, otherwise the operation fails. Uninstalling needs neither.

## Example

```json
{
  "tooth": "github.com/example/MyMod",
  "variant": "",
  "version": "1.0.0",
  "explicit": true,
  "source": "github.com/example/MyMod",
  "archive": {
    "kind": "git",
    "repo": "https://github.com/example/MyMod.git",
    "commit": "0123456789abcdef0123456789abcdef01234567"
  },
  "assets": [
    {
      "url": "https://github.com/example/MyMod/releases/download/v1.0.0/MyMod-server-win-x64.zip",
      "sha256": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
    }
  ],
  "entry": {
    "platform": "server",
    "runtime": "win-x64",
    "dependencies": { "github.com/LiteLDev/LeviLamina": ">=1.0.0 <2.0.0" },
    "assets": [
      {
        "type": "mcmod",
        "urls": ["https://github.com/example/MyMod/releases/download/v1.0.0/MyMod-server-win-x64.zip"]
      }
    ]
  },
  "files": [
    "mods/MyMod/MyMod.dll",
    "mods/MyMod/manifest.json"
  ]
}
```

## Earlier Formats

lip does not take over workspaces of earlier lip versions: a `.lip/workspace.json` that is not of `format_version` 4, the `.lip/lock.json` of lip v0.36, and the `tooth_lock.json` that lip v0.x kept in the workspace root. In a workspace with one of them, lip fails with a message instead of installing, uninstalling, updating or listing packages, so that it never writes over files those packages own. Uninstall the packages with the lip version that installed them, or delete the file to start over; lip then knows nothing about the files they installed.
