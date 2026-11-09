# lip update

Update packages.

## Synopsis

```shell
lip update [PACKAGES] [options]
```

## Arguments

| Argument | Description |
| --- | --- |
| `[PACKAGES]` | The packages to update. Accepts the same forms as [`lip install`](./install.md#arguments): without a version, the newest version is installed; with `@<version>`, exactly that version; with a git ref, the commit it names now. If omitted, every explicitly installed package is updated as described below. |

## Description

The packages given as arguments must already be installed, and become explicitly installed. When two arguments name the same package, both must be satisfied: `lip update <path> <path>@1.0.0` installs version 1.0.0, which also satisfies `<path>`, while `lip update <path>@1.0.0 <path>@1.1.0` fails. Updating a package uninstalls the old version and installs the new one; its dependencies are updated as needed. A package is replaced only when the result differs from what is installed in its identity, its version or its commit (see [State File](../../concepts/lockfiles.md#package-entry)). A package from a URL or a local path has no commit, so it is replaced only when its version changes. A package that is not replaced keeps the package file recorded in the state file, and with it its dependencies, even when its `lip.toml` has changed since.

Without arguments, every explicitly installed package is requested again by the source it was installed from, as recorded in the [state file](../../concepts/lockfiles.md):

| Installed from | `lip update` installs |
| --- | --- |
| `<path>` | The newest version. |
| `<path>@<version>` | The same version: nothing changes. |
| `<path>@branch:<branch>` | The current head of the branch. |
| `<path>@tag:<tag>` | The commit the tag points to now (it only changes if the tag was moved). |
| `<path>@rev:<commit>` | The same commit: nothing changes. |
| An `https://` URL | The ZIP file downloaded again from that URL; the package is replaced only when its version changed. |
| A local ZIP file | The same file, read again; the package is replaced only when its version changed. If the file no longer exists, the package is kept as it is. |
| A local directory | The same directory, read again; the package is replaced only when its version changed. If the directory no longer exists, the package is kept as it is. |

Without arguments, a package whose source now gives a package of another identity (e.g. a ZIP file whose `lip.toml` now has another `id`) is replaced by that package, which becomes explicitly installed. When two sources then give packages of the same identity, or a source gives a package of the identity of a package kept as it is, the update fails unless one package satisfies both.

To move a package installed from a git ref back to released versions, update it by its plain repository path, e.g. `lip update github.com/LiteLDev/LeviLamina`.

## Options

| Option | Description |
| --- | --- |
| `-n, --dry-run` | Run without making any changes. |
| `--ignore-scripts` | Skip running install and uninstall hooks while updating packages. |
| `--platform <RID>` | The platform of the workspace, a .NET RID such as `win-x64`. |
