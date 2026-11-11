# CLI Overview

The `lip` CLI is the primary interface for managing packages. It provides commands for installing, updating, and managing dependencies.

## Global Options

| Option | Description |
| :--- | :--- |
| `-h, --help` | Show help for `lip` or for a command (e.g. `lip install --help`). |
| `-v, --version` | Print the version of `lip`. |

`lip` without arguments prints help.

Some commands accept `--json` to print their result as JSON: [`config get`](commands/config.md), [`config list`](commands/config.md) and [`list`](commands/list.md). [`pkg view`](commands/pkg_view.md) always prints JSON.

[`install`](commands/install.md), [`uninstall`](commands/uninstall.md) and [`update`](commands/update.md) accept `--platform <RID>` to set the platform of the workspace [environment](../concepts/lip_toml.md#environment). Without it, the platform is the one the state file records, or else `win-x64`, whatever system lip runs on; Linux-native packages need `--platform linux-x64`. lip manages server workspaces only.

## Commands

Commands for running a server:

-   [`lip cache clean`](commands/cache_clean.md): Clean the local cache.
-   [`lip config`](commands/config.md): Get, set, list, and delete configuration values.
-   [`lip install`](commands/install.md): Install packages.
-   [`lip list`](commands/list.md): List installed packages.
-   [`lip run`](commands/run.md): Run the server of the workspace.
-   [`lip uninstall`](commands/uninstall.md): Uninstall packages.
-   [`lip update`](commands/update.md): Update packages.

Commands for developing packages, in the command group `pkg`:

-   [`lip pkg init`](commands/pkg_init.md): Create a `lip.toml` for a new package.
-   [`lip pkg migrate`](commands/pkg_migrate.md): Migrate the `tooth.json` of lip v0.x in the current directory to `lip.toml`.
-   [`lip pkg pack`](commands/pkg_pack.md): Pack a directory into a ZIP archive.
-   [`lip pkg view`](commands/pkg_view.md): Show the package file and the versions of a package.

`lip pkg --help`, `lip cache --help` and `lip config --help` show the commands of a group. Earlier versions of lip had `lip init`, `lip pack`, `lip view`, `lip versions` and `lip migrate`; they are now `lip pkg init`, `lip pkg pack`, `lip pkg view` (which also lists the versions) and `lip pkg migrate`.
