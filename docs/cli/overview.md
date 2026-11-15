# CLI Overview

The `lip` CLI is the primary interface for managing packages. It provides commands for installing, updating, and managing dependencies.

## Global Options

| Option | Description |
| :--- | :--- |
| `-h, --help` | Show help for `lip` or for a command (e.g. `lip install --help`). |
| `-v, --version` | Print the version of `lip`. |

`lip` without arguments opens the [interactive interface](tui.md) when it runs in a terminal, and prints help otherwise (in a script or a pipe).

Some commands accept `--json` to print their result as JSON: [`config get`](commands/config.md), [`config list`](commands/config.md), [`daemon status`](commands/daemon.md) and [`list`](commands/list.md). [`pkg view`](commands/pkg_view.md) always prints JSON.

[`setup`](commands/setup.md), [`install`](commands/install.md), [`uninstall`](commands/uninstall.md) and [`update`](commands/update.md) install the packages of the platform `win-x64` into the workspace, whatever system lip runs on (see [Environment](../concepts/lip_toml.md#environment)); the Linux server is the variant `github.com/LiteLDev/bds#linux`. `setup` chooses the game (the server) and the loader (LeviLamina) of the workspace; `install` and `update` never change them. lip manages server workspaces only.

## Commands

Commands for running a server:

-   [`lip cache clean`](commands/cache_clean.md): Clean the local cache.
-   [`lip config`](commands/config.md): Get, set, list, and delete configuration values.
-   [`lip install`](commands/install.md): Install packages.
-   [`lip list`](commands/list.md): List installed packages.
-   [`lip run`](commands/run.md): Run the server of the workspace.
-   [`lip self-update`](commands/self_update.md): Update lip itself to its latest release.
-   [`lip setup`](commands/setup.md): Choose the game and the loader of the workspace.
-   [`lip uninstall`](commands/uninstall.md): Uninstall packages.
-   [`lip update`](commands/update.md): Update packages.

Every command except `lip daemon` is carried out by the daemon of lip, a background process of your user that the first command starts; it behaves exactly as if the `lip` you started carried it out. The command group `daemon` shows and stops it:

-   [`lip daemon status`](commands/daemon.md): Show whether the daemon runs.
-   [`lip daemon stop`](commands/daemon.md): Stop the daemon.

Commands for developing packages, in the command group `pkg`:

-   [`lip pkg init`](commands/pkg_init.md): Create a `lip.toml` for a new package.
-   [`lip pkg migrate`](commands/pkg_migrate.md): Migrate the `tooth.json` of lip v0.x in the current directory to `lip.toml`.
-   [`lip pkg pack`](commands/pkg_pack.md): Pack a directory into a ZIP archive.
-   [`lip pkg view`](commands/pkg_view.md): Show the package file and the versions of a package.

`lip pkg --help`, `lip cache --help`, `lip config --help` and `lip daemon --help` show the commands of a group. Earlier versions of lip had `lip init`, `lip pack`, `lip view`, `lip versions` and `lip migrate`; they are now `lip pkg init`, `lip pkg pack`, `lip pkg view` (which also lists the versions) and `lip pkg migrate`.
