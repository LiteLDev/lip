# State File

`lip` records the state of a workspace in its **state file**: which packages are installed, their exact versions, where they came from, and the files they placed on the disk.

## Location

The state file is `.lip/state.json` in the workspace root. On Windows lip creates the `.lip` directory with the hidden attribute; elsewhere its name hides it.

Because the state file lives in the workspace directory rather than being tied to its path, a workspace that is moved, renamed or copied keeps its state file, and so its installed packages.

`.lip` is reserved for lip: installing a package that would write a file to `.lip` at the workspace root, or anywhere below it, fails, and a `purge` pattern never matches `.lip` or anything below it.

## Purpose

The state file serves several purposes:

1.  **State Tracking**: It is the single source of truth for what is installed in the workspace.
2.  **Reproducibility**: It records the package file every installed package was installed with, and, for a package from a git repository, the commit it was at. When lip needs a package again, it installs it with that package file instead of resolving the package again.
3.  **Uninstallation**: `lip` tracks every file written to disk by a package. This allows `lip uninstall` to cleanly delete all files associated with a package, preventing "file rot" in the workspace.
4.  **Dependency Graph**: It distinguishes between packages explicitly installed by the user and those installed as dependencies. This allows `lip uninstall` to also remove dependencies that no explicitly installed package needs any more, and `lip list` to show the two groups separately.
5.  **Environment**: It records the side and platform of the workspace once they are known, so later commands use the same [environment](lip_toml.md#environment).

The state file is replaced once, after every package of an operation has been installed or uninstalled. If the operation fails, lip undoes its changes to the workspace and the state file stays as it was, so the two always match (see [Internal Mechanism](../guides/internal_mechanism.md#pipeline)).

## Structure

The state file is a JSON file described by the JSON Schema [`state.schema.json`](https://github.com/LiteLDev/lip/blob/main/protocol/state.schema.json):

```json
{
  "format": 4,
  "side": "server",
  "platform": "win-x64",
  "packages": [...]
}
```

### Fields

*   **`format`**: The format of the state file, `4`.
*   **`side`**: The side of the workspace, `"server"` or `"client"`. lip writes `"server"`.
*   **`platform`**: The platform of the workspace, a [.NET RID](https://learn.microsoft.com/dotnet/core/rid-catalog).
*   **`packages`**: The installed packages, in installation order: a package comes after the packages that satisfy its dependencies.

`side` and `platform` are always present and never `null`; a state file without them is invalid. A workspace without a state file gets its environment from its first `install`, `uninstall` or `update` (see [`lip install`](../cli/commands/install.md)).

### Package Entry

Each package is recorded with exactly these members:

*   **`manifest`**: The whole package file the package was installed with. For a `tooth.json` of an [earlier format](lip_toml.md#earlier-formats), it is the package file lip derived from it for the environment of the workspace. Its `id` is the package id and its `version` the installed version.
*   **`variant`**: The variant of the source it was installed from, `""` for the default variant. With the `id` of `manifest`, it makes the [identity](lip_toml.md#identity) of the package.
*   **`explicit`**:
    *   `true`: The package was explicitly installed by the user (e.g., `lip install <package>`).
    *   `false`: The package was installed as a dependency of another package.
*   **`ref`**: What the package was installed from: the argument given to `lip install` or `lip update` (e.g. `github.com/user/repo`, `github.com/user/repo#lua@branch:main` or an `https://` URL; a local path is recorded as the `file:` URI of its absolute path), or, for a dependency, the key of the dependency. [`lip update`](../cli/commands/update.md) requests it again.
*   **`commit`**: Only for a package from a git repository: the commit its archive was at, in lowercase hexadecimal. A package from a URL or a local path has no `commit`.
*   **`files`**: The files the package owns, relative to the workspace root and `/`-separated.

The installed entry is the entry of `manifest.variants[variant]` whose `side` and `platform` are those of the workspace.

lip compares an installed package with a package it resolves by identity, version and commit only: two packages with the same identity and version, from no git repository or from the same commit, are the same package, even if the files of a URL or local path have changed since. No checksum of a downloaded ZIP archive or asset is recorded or checked.

When lip needs the files of an installed package again, it installs it with the recorded `manifest`: for a package from a git repository, from the recorded commit (even if it came from a branch that has moved on since); otherwise from the archive its `ref` names now. Uninstalling needs neither.

## Example

```json
{
  "format": 4,
  "side": "server",
  "platform": "win-x64",
  "packages": [
    {
      "manifest": {
        "format": 4,
        "id": "github.com/example/MyMod",
        "version": "1.0.0",
        "variants": {
          "": [
            {
              "side": "server",
              "platform": "win-x64",
              "deps": { "github.com/LiteLDev/LeviLamina": ">=1.0.0 <2.0.0" },
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
      "files": ["mods/MyMod/MyMod.dll", "mods/MyMod/manifest.json"]
    }
  ]
}
```

## Workspaces of lip v0.39.x

The state file of lip v0.39.x, also of format 4, named the side `platform` and the platform `runtime`, at its root and in the entries of each recorded `manifest`. lip does not read such a state file: a root or an entry that has `runtime`, or whose `platform` is `"server"` or `"client"`, fails the command with a message to rename `platform` to `side` and `runtime` to `platform`. lip does not migrate it; rename the two keys at the root and in every entry of every `manifest`. The next section covers the workspaces of lip v0.37.0 and earlier.

## Workspaces of lip v0.x

A workspace of lip v0.37.0 and earlier has a `tooth_lock.json` of format version 3 in its root instead of a state file. lip migrates it automatically:

*   `lip list` reads it as the state file it stands for, without changing it.
*   The next `lip install`, `lip uninstall` or `lip update` writes that state file as `.lip/state.json` and deletes `tooth_lock.json`.

Each package keeps its variant, whether it was explicitly installed, and the files below the workspace that it owns; its `ref` is its package id (with `#<variant>`), and its `commit` is looked up from the version tag `v<version>` of its repository. When that commit cannot be looked up, the command fails. Packages whose `tooth.json` lip cannot read for the environment of the workspace are dropped.
