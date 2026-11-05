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

The packages must already be installed, and become explicitly installed. Updating a package uninstalls the old version and installs the new one; its dependencies are updated as needed. A package is reinstalled only when the result differs from what is installed: another identity, another version, another archive, or other assets.

Without arguments, every explicitly installed package is requested again by the source it was installed from, as recorded in the [workspace file](../../concepts/lockfiles.md):

| Installed from | `lip update` installs |
| --- | --- |
| `<path>` | The newest version. |
| `<path>@<version>` | The same version: nothing changes. |
| `<path>@branch:<branch>` | The current head of the branch. |
| `<path>@tag:<tag>` | The commit the tag points to now (it only changes if the tag was moved). |
| `<path>@rev:<commit>` | The same commit: nothing changes. |
| An `https://` URL | The ZIP file downloaded again from that URL. |
| A local ZIP file | The same file; if its content changed, the package is reinstalled from it. If the file no longer exists, the package is kept as it is. |
| A local directory | The same directory, read again. A directory is identified by its path, so the package is reinstalled only when its version or its assets changed. If the directory no longer exists, the package is kept as it is. |

To move a package installed from a git ref back to released versions, update it by its plain tooth path, e.g. `lip update github.com/LiteLDev/LeviLamina`.

## Options

| Option | Description |
| --- | --- |
| `-n, --dry-run` | Run without making any changes. |
| `--ignore-scripts` | Skip running install and uninstall hooks while updating packages. |
| `--rid <RID>` | The .NET RID of the workspace, e.g. `win-x64`. |
