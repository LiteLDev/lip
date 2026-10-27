# lip update

Update packages.

## Synopsis

```shell
lip update [PACKAGES] [options]
```

## Arguments

| Argument | Description |
| --- | --- |
| `[PACKAGES]` | The packages to update. Accepts the same formats as [`lip install`](./install.md#arguments): without a version, the newest version is installed; with `@<version>`, exactly that version. If omitted, every explicitly installed package is updated to its newest version. |

## Description

The packages must already be installed. Updating a package uninstalls the old version and installs the new one; its dependencies are updated as needed.

## Options

| Option | Description |
| --- | --- |
| `-n, --dry-run` | Run without making any changes. |
| `--ignore-scripts` | Skip running install and uninstall lifecycle scripts while updating packages. |
| `--platform <PLATFORM>` | The platform of the workspace: `server` or `client`. See [Environment](../../concepts/package_manifest.md#environment). |
| `--rid <RID>` | The .NET RID of the workspace, e.g. `win-x64`. |
