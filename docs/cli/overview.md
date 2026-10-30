# CLI Overview

The `lip` CLI is the primary interface for managing packages. It provides commands for installing, updating, and managing dependencies.

## Global Options

| Option | Description |
| :--- | :--- |
| `-h, --help` | Show help for `lip` or for a command (e.g. `lip install --help`). |
| `-v, --version` | Print the version of `lip`. |

`lip` without arguments prints help.

Some commands accept `--json` to print their result as JSON: [`config get`](commands/config.md), [`config list`](commands/config.md), [`list`](commands/list.md), and [`versions`](commands/versions.md).

[`install`](commands/install.md), [`uninstall`](commands/uninstall.md) and [`update`](commands/update.md) accept `--rid <RID>` to set the RID of the workspace [environment](../concepts/package_manifest.md#environment). lip manages server workspaces only.

## Commands

-   [`lip cache clean`](commands/cache_clean.md): Clean the local cache.
-   [`lip config`](commands/config.md): Get, set, list, and delete configuration values.
-   [`lip init`](commands/init.md): Create a `manifest.json` for a new project.
-   [`lip install`](commands/install.md): Install packages.
-   [`lip list`](commands/list.md): List installed packages.
-   [`lip migrate`](commands/migrate.md): Migrate the package repository in the current directory from `tooth.json` to `manifest.json`.
-   [`lip uninstall`](commands/uninstall.md): Uninstall packages.
-   [`lip update`](commands/update.md): Update packages.
-   [`lip versions`](commands/versions.md): Show available versions for a package.
-   [`lip view`](commands/view.md): Show the manifest of a package.
