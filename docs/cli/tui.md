# Interactive Interface

Run `lip` without arguments in a terminal to open the interactive interface: every command of lip, the mods of [levimc.org/mods](https://levimc.org/mods/), and the server of the workspace, in one full-screen program.

```shell
lip
```

The interface works on one workspace at a time, the current directory at first (see [Workspaces](#workspaces)). When the standard input or the standard output of lip is not a terminal (in a script or a pipe), `lip` without arguments writes its help instead.

Like the commands, the interface runs in the [daemon](../guides/internal_mechanism.md#daemon) of lip; the `lip` you start shows it and passes on your keys. You can open it in several terminals at once.

## Workspaces

The top line shows the selected workspace. Every tab works on it: the packages, the server, the configuration files and the commands of the Develop tab are those of the selected workspace.

lip remembers the workspaces you used in a list, `workspaces.json` in its state directory (`%LOCALAPPDATA%\lip\State` on Windows, `$XDG_STATE_HOME/lip` elsewhere). When the interface opens, the current directory is added to the list, if it is not there yet, and selected. Press `w` to open the list:

| Key | Does |
| --- | --- |
| `↑`, `↓`, `Enter` | Select a workspace. |
| `a` | Adds a directory, absolute or relative to the selected workspace, and selects it. The directory must exist. |
| `n` | Sets up a new server (see [New server](#new-server)). |
| `d`, `Del` | Removes the selected workspace from the list. No file in it changes. The workspace that is selected cannot be removed. |
| `Esc` | Closes the list. |

The list is kept between runs of lip, and shared by the interfaces open at the same time. A workspace that no longer exists is marked as missing.

## New server

You can set up a server from nothing without leaving the interface. In a workspace without a game, the Packages tab says so: press `n` there, or in the list of workspaces (`w`). lip then asks, one dialog after the other:

1. **The directory of the server**, absolute or relative to the selected workspace. In a workspace without a game, that workspace is offered. A directory that does not exist is created.
2. **The game version**, read from the builds list of the server: the release versions, newest first, then the versions of a single build (`1.21.50-patch.2`).
3. **The build**: `windows`, the server that LeviLamina and its mods run on (through Wine off Windows), or `linux`, the Linux server without a loader. Only the builds that the version has are offered.
4. **The loader**, for the `windows` build: the versions of LeviLamina that support the game, newest first, or no loader. lip finds them as [`lip setup`](./commands/setup.md#the-loader) does, which may take a moment.

Once you have chosen, lip creates the directory, adds it to the workspace list, selects it, and carries out [`lip setup`](./commands/setup.md) with your choices, e.g. `lip setup --game 1.21.50 --loader levilamina@1.1.0`: it shows the plan, and, once you choose **Apply**, asks whether you accept the Minecraft EULA. `Esc` in any of the dialogs before the last choice stops, creating nothing.

When the server is set up, start it in the **3 Server** tab, and install mods from **2 Browse**.

## GitHub reachability

lip downloads LeviLamina and most mods from GitHub. When the interface opens and `github_proxy` is not set (not even to an empty value, see [Configuration](./configuration.md)), lip checks in the background whether it reaches GitHub, while you use the interface: it requests `https://github.com/LiteLDev/LeviLamina.git/info/refs?service=git-upload-pack` and waits up to 10 seconds for an answer.

When GitHub does not answer, the interface asks how to reach it:

| Choice | Does |
| --- | --- |
| **Set a proxy** | Asks for the URL prefix of a GitHub proxy, tests it with the same request through it, and saves it as `github_proxy` when it works. When it does not, you can enter another one or save it anyway. |
| **Connect directly** | Sets `github_proxy` to an empty value: lip connects to GitHub directly, and does not check or ask again. |
| **Later** | Changes nothing; lip checks again when the interface next opens. |

lip does not suggest any proxy itself. You can change `github_proxy` at any time in the **5 Settings** tab or with [`lip config`](./commands/config.md); once it is set, the check does not happen.

## Tabs

Switch tabs with `Tab` and `Shift+Tab`, or with the number of the tab. The bottom line of the screen always lists the keys of what is shown.

| Tab | Does | Commands |
| --- | --- | --- |
| **1 Packages** | Lists the installed packages; installs, updates and uninstalls them. | [`lip list`](./commands/list.md), [`lip install`](./commands/install.md), [`lip update`](./commands/update.md), [`lip uninstall`](./commands/uninstall.md) |
| **2 Browse** | Searches, filters and sorts the mods of levimc.org, shows their details, versions and README, and installs them. | [`lip install`](./commands/install.md) |
| **3 Server** | Runs the server of the workspace in a window, shows what it writes, and sends it the lines you type. | [`lip run`](./commands/run.md) |
| **4 Config** | Shows and edits `server.properties`, `allowlist.json`, `permissions.json` and the configuration files of the installed mods. | |
| **5 Settings** | Shows and changes the user configuration and the configuration of the workspace; cleans the cache. | [`lip config`](./commands/config.md), [`lip cache clean`](./commands/cache_clean.md) |
| **6 Develop** | Creates a `lip.toml`, migrates a `tooth.json`, packs a ZIP archive, views a package. | [`lip pkg init`](./commands/pkg_init.md), [`lip pkg migrate`](./commands/pkg_migrate.md), [`lip pkg pack`](./commands/pkg_pack.md), [`lip pkg view`](./commands/pkg_view.md) |

Quit with `q` or `Ctrl+Q`. `w` opens the list of workspaces.

## Installing, updating and uninstalling

Every change to the workspace is planned first: lip carries it out as a dry run (`--dry-run`) and shows what it would do. Nothing changes until you choose **Apply**.

While the change is applied, `Ctrl+C` cancels it and undoes what was done so far. When a command fails, the interface shows the cause, what you can do about it, and the path of the failure report below `.lip/logs`, and goes on.

In the install dialog of the Packages tab (`i`), the packages are written as on the command line (see [`lip install`](./commands/install.md)), separated by spaces.

When an installation sets up the game, e.g. installing `LeviLamina` in a new workspace (see [`lip setup`](./commands/setup.md)), lip asks in a dialog, once you choose **Apply**, whether you accept the Minecraft EULA and the Microsoft Privacy Policy, and shows their links; nothing changes unless you accept. When the plan of such an installation lists mods that would be uninstalled because they do not work with the new game, choosing **Apply** uninstalls them.

## Browsing mods

The Browse tab reads the mod catalogue of levimc.org (`https://levimc.org/api/v4/mods/`) when it is first opened; nothing of it is kept on disk, and `github_proxy` is not used for it. lip manages servers only, so the list holds the mods that have a build for a server.

| Key | Does |
| --- | --- |
| `/` | Searches by name, id, author, tag and description. `↑` and `↓` go on to the mods found; `Esc` clears the search. |
| `s` | Changes the order: relevance, stars, last update, newest. |
| `←` | Moves to the Filters pane; `→` or `Esc` moves back. |
| `Enter` | Installs the selected mod: asks which variant and version. |
| `r` | Shows its README. |
| `o` | Opens its page in the browser. |
| `R` | Reads the catalogue again. |

The Filters pane has a section for each kind of filter: game version, tags, other tags, license and dependencies. A section shows its heading only until you press `Enter` on it; a heading says how many of its rows are chosen.

| Key | Does |
| --- | --- |
| `Enter`, `Space` | On a heading: shows or hides the rows of the section. On a row: includes it. |
| `←` | On a row: goes to the heading of its section and hides its rows. |
| `x` | Excludes the selected row, when it is a tag or a dependency. |
| `c` | Clears every filter. |
| `→`, `Esc` | Moves back to the mods. |

The list then holds the mods that run on one of the included game versions, carry every included tag and none of the excluded ones, are under one of the included license families, and depend on every included package and on no excluded one. The tags and the license families are those of levimc.org/mods.

`Enter` on a mod opens one dialog for the variant and the version to install. `←` and `→` change the variant, among those with a build for a server. Below it are the latest version and each version of the catalogue that has the variant. `c` asks for a custom version instead: what follows the `@` of a package, that is a version, `branch:<name>`, `tag:<name>` or `rev:<commit>`. The plan of the installation is shown next, as for every change.

## Keys

The same keys do the same everywhere:

| Key | Does |
| --- | --- |
| `↑`, `↓` | Select a row. |
| `←`, `→` | Move between the panes of a tab, the buttons of a dialog, or the variants of a mod. |
| `Enter`, `Space` | Act on what is selected: open the actions of a package, install a mod, edit a setting, run an action, choose a button. |
| `Esc` | Goes back: closes a dialog, leaves a pane, clears a search. |
| `Del` | Uninstalls the selected package, resets the selected setting, removes the selected player, or undoes the change of the selected property. |

In the Packages tab, `Enter` on a package lists what can be done with it. In the Settings tab, `Enter` on `github_proxy` with an empty value makes lip connect to GitHub directly, while `Del` removes the key; `→` moves to the cache, where `Enter` cleans it. In the Server tab, `↑` and `↓` scroll while the server does not run, and `Esc` erases the line being typed while it does.

Every list goes around: `↑` on its first row moves to the last, and `↓` on the last to the first.

## Configuring the server and the mods

The Config tab lists the configuration files of the workspace on the left, and shows the entries of the selected one on the right. `Enter` or `→` moves to the entries; `Esc` or `←` moves back.

| File | Is edited as |
| --- | --- |
| `server.properties` | Its properties, with the comment of the selected one below. `Enter` toggles a `true`/`false` property, lists the allowed values of a property that has a few, and asks for any other value; an integer is checked. `Del` undoes the change. |
| `allowlist.json` | The players allowed on the server. `a` adds one by name and, optionally, xuid; `Enter` sets whether the player may join a full server; `Del` removes the player. |
| `permissions.json` | The permission of each player, by xuid. `a` adds a player; `Enter` chooses `operator`, `member` or `visitor`; `Del` removes the player. |
| A `.json` file of a mod | A tree: `Enter` opens an object or an array and edits a value (a `true`/`false` value toggles), `Esc` goes back up. In an array, `a` adds a value and `Del` removes one. The top-level `version` of a mod's file is not edited: the mod keeps it up to date. |
| Any other file of a mod | Shown as it is; edit it with a text editor. |

The configuration files of a mod are the files in `mods/<mod>/config/`; for a mod without one, the `.json`, `.ini`, `.toml`, `.yml` and `.yaml` files directly in `mods/<mod>/` (but `manifest.json`), and `mods/<mod>/data/config.json`.

Nothing is written until you press `s`, which saves the selected file. lip changes only what you changed: in `server.properties`, the lines of the properties you changed, keeping every comment; in a JSON file, the values you changed, keeping the order of its members (comments in a JSON file are not kept). Leaving the file, the tab or lip with unsaved changes asks whether to save them. `r` reads the files again.

The server and most mods create their configuration files when the server first starts, so a new workspace may not have them yet; `allowlist.json` and `permissions.json` are created when you save them. Changes take effect when the server is started again: when it runs, restart it in the Server tab.

## Running the server

In the Server tab, `Enter` starts the server of the selected workspace as [`lip run`](./commands/run.md) does. What the server writes is shown in the window; `PgUp` and `PgDn` scroll back. While the server runs, what you type is a line for the server, sent with `Enter`; `↑` and `↓` recall earlier lines.

`Ctrl+C` asks the server to stop, by sending it the line `stop`. Quitting lip while servers you started run does the same for each of them, and lip waits until they have exited. Closing the terminal does too.

Each workspace has its own server, so servers of several workspaces can run at once: select another workspace to start or see its server. Interfaces open in several terminals at once share the servers: each shows the server of its selected workspace, and can send it lines or stop it.

The other tabs stay usable while the server runs. As with `lip run`, the workspace is not locked meanwhile, and changes to packages take effect when the server is started again.
