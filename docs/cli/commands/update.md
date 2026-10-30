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

The packages must already be installed, and become explicitly installed. Updating a package uninstalls the old version and installs the new one; its dependencies are updated as needed. A package is reinstalled only when the result differs from what is installed: another version, another archive, or the same archive with other content.

Without arguments, every explicitly installed package is requested again by what it was installed from, as recorded in the [lock](../../concepts/lockfiles.md):

| Installed from | `lip update` installs |
| --- | --- |
| `<path>` or `<path>@<version>` | The newest version. |
| `<path>@branch:<branch>` | The current head of the branch. |
| `<path>@tag:<tag>` | The commit the tag points to now (it only changes if the tag was moved). |
| `<path>@rev:<commit>` | The same commit: nothing changes. |
| An `https://` URL | The same URL. Downloads are cached by URL, so the cached copy is used; run [`lip cache clean`](cache_clean.md) first to download it again. |
| A local `.mcmod` or `.zip` file | The same file; if its content changed, the package is reinstalled from it. If the file no longer exists, the package is kept as it is, with a warning. |

To move a package installed from a git ref back to released versions, update it by its plain package path, e.g. `lip update github.com/LiteLDev/LeviLamina`.

## Options

| Option | Description |
| --- | --- |
| `-n, --dry-run` | Run without making any changes. |
| `--ignore-scripts` | Skip running install and uninstall lifecycle scripts while updating packages. |
| `--rid <RID>` | The .NET RID of the workspace, e.g. `win-x64`. |
