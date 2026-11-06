# Workspace File

`lip` records the state of a workspace in its **workspace file**: which packages are installed, their exact versions, where they came from, and the files they placed on the disk.

## Location

The workspace file is `.lip/workspace.json` in the workspace root. On Windows lip creates the `.lip` directory with the hidden attribute; elsewhere its name hides it.

Because the workspace file lives in the workspace directory rather than being tied to its path, a workspace that is moved, renamed or copied keeps its workspace file, and so its installed packages.

`.lip` is reserved for lip: installing a package that would write a file to `.lip` at the workspace root, or anywhere below it, fails, and a `purge` pattern never matches `.lip` or anything below it.

## Purpose

The workspace file serves several purposes:

1.  **State Tracking**: It is the single source of truth for what is installed in the workspace.
2.  **Reproducibility**: It records the `tooth.json` every installed package was installed with, and, for a package from a git repository, the commit it was at. When lip needs a package again, it installs it with that `tooth.json` instead of resolving the package again.
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
*   **`platform`**: The platform of the workspace, `"server"` or `"client"`. lip writes `"server"`.
*   **`runtime`**: The [.NET RID](https://learn.microsoft.com/dotnet/core/rid-catalog) of the workspace.
*   **`packages`**: The installed packages, in installation order: a package comes after the packages that satisfy its dependencies.

`platform` and `runtime` are always present and never `null`; a workspace file without them is invalid. A workspace without a workspace file gets its environment from its first `install`, `uninstall` or `update` (see [`lip install`](../cli/commands/install.md)).

### Package Entry

Each package is recorded with exactly these members:

*   **`manifest`**: The whole `tooth.json` the package was installed with, of `format_version` 4. For a `tooth.json` of an [earlier format](tooth_json.md#earlier-formats), it is the format 4 `tooth.json` lip derived from it for the environment of the workspace. Its `tooth` is the tooth path of the package and its `version` the installed version.
*   **`variant`**: The variant of the source it was installed from, `""` for the default variant. With the `tooth` of `manifest`, it makes the [identity](tooth_json.md#identity) of the package.
*   **`explicit`**:
    *   `true`: The package was explicitly installed by the user (e.g., `lip install <package>`).
    *   `false`: The package was installed as a dependency of another package.
*   **`ref`**: What the package was installed from: the argument given to `lip install` or `lip update` (e.g. `github.com/user/repo`, `github.com/user/repo#lua@branch:main` or an `https://` URL; a local path is recorded as the `file:` URI of its absolute path), or, for a dependency, the key of the dependency. [`lip update`](../cli/commands/update.md) requests it again.
*   **`commit`**: Only for a package from a git repository: the commit its archive was at, in lowercase hexadecimal. A package from a URL or a local path has no `commit`.
*   **`files`**: The files the package owns, relative to the workspace root and `/`-separated.

The installed entry is the entry of `manifest.variants[variant]` whose `platform` and `runtime` are those of the workspace.

lip compares an installed package with a package it resolves by identity, version and commit only: two packages with the same identity and version, from no git repository or from the same commit, are the same package, even if the files of a URL or local path have changed since. No checksum of a downloaded ZIP archive or asset is recorded or checked.

When lip needs the files of an installed package again, it installs it with the recorded `manifest`: for a package from a git repository, from the recorded commit (even if it came from a branch that has moved on since); otherwise from the archive its `ref` names now. Uninstalling needs neither.

## Example

```json
{
  "format_version": 4,
  "platform": "server",
  "runtime": "win-x64",
  "packages": [
    {
      "manifest": {
        "format_version": 4,
        "format_uuid": "f74a5fda-cb2a-4de8-959f-a1a76bad3102",
        "tooth": "github.com/example/MyMod",
        "version": "1.0.0",
        "variants": {
          "": [
            {
              "platform": "server",
              "runtime": "win-x64",
              "dependencies": { "github.com/LiteLDev/LeviLamina": ">=1.0.0 <2.0.0" },
              "assets": [
                {
                  "type": "mcmod",
                  "urls": ["https://github.com/example/MyMod/releases/download/v1.0.0/MyMod-server-win-x64.zip"]
                }
              ]
            }
          ]
        }
      },
      "variant": "",
      "explicit": true,
      "ref": "github.com/example/MyMod",
      "commit": "0123456789abcdef0123456789abcdef01234567",
      "files": [
        "mods/MyMod/MyMod.dll",
        "mods/MyMod/manifest.json"
      ]
    }
  ]
}
```
