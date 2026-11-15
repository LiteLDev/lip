# Interface

This page is the compact reference of the interactive interface of lip: its keys, its pages and the commands behind them, and the rules that hold on every page. To learn the interface step by step, start with [A tour of the interface](../interface/tour.md).

The normative definition is the [lip-tui(1)](https://github.com/LiteLDev/lip/blob/main/protocol/lip-tui.md) page of the protocol.

## Starting

```shell
lip
```

`lip` without arguments opens the interface when its standard input and its standard output are both terminals; otherwise it writes its help (see [Command line](cli.md)).

- The interface works on one workspace at a time, the **selected workspace**: at first the folder that `lip` was started in.
- The [daemon](../concepts/how_lip_works.md) of lip carries the interface out. Several interfaces can be open at once.
- The terminal must have at least 60 columns and 16 rows. In a smaller one lip asks you to enlarge it, and the only key that works is `Ctrl+C`, which quits without asking.
- In a terminal narrower than 80 columns, the sidebar shows one symbol for each page while a page has the focus.

## Keys

The same keys do the same on every page. The bottom line of the screen lists the ones that work where the focus is.

| Key | Does |
| --- | --- |
| `↑` `↓` | Moves in a list or a menu. In the sidebar, shows the page before or after. |
| `←` `→` | Moves between the sidebar and the page, and between panes or buttons side by side; changes an option that has several values. |
| `Enter` | Opens or carries out what is selected. On a row of a list, opens its actions. |
| `Space` | Checks a row where several can be chosen; switches an option on or off. |
| `Tab`, `Shift+Tab` | Goes to the next or the previous area, round the sidebar and the areas of the page. |
| `Esc` | Goes back one level: from a dialog to the page, from an inner area of a page to the one before it, from the page to the sidebar. In the sidebar, asks whether to quit. |
| `PgUp` `PgDn` `Home` `End` | Pages through a list or a text. In the sidebar, `Home` and `End` show the first and the last page. |
| `Ctrl+C` | On the Server page while the server runs, stops the server. While a change is carried out, cancels it. Anywhere else, asks whether to quit. |
| Letters and digits | Are typed where a text field has the focus. They do nothing anywhere else: there are no letter shortcuts. |

## Pages

| Page | Does | Commands |
| --- | --- | --- |
| [Home](../interface/home.md) | Shows the state of the selected workspace and the next step to take. | |
| [Server](../interface/server.md) | Starts and stops the server, shows what it writes, and sends it what you type. | [`lip run --managed --detach`](commands/run.md), [`lip config set restart … --workspace`](commands/config.md) |
| [Mods](../interface/mods.md) | Lists the installed packages; installs, updates and uninstalls them. | [`lip list`](commands/list.md), [`lip install`](commands/install.md), [`lip update`](commands/update.md), [`lip uninstall`](commands/uninstall.md) |
| [Get mods](../interface/get_mods.md) | Searches the mods of levimc.org, and installs a version of one. | [`lip install`](commands/install.md) |
| [Game config](../interface/game_config.md) | Edits `server.properties`, `allowlist.json` and `permissions.json`. | None |
| [Mod config](../interface/mod_config.md) | Edits the configuration files of the installed mods. | None |
| [Workspaces](../interface/workspaces.md) | Lists the workspaces; selects, adds and removes them; sets up a new server; changes the game or the loader of one. | [`lip setup`](commands/setup.md) |
| [Tools](../interface/tools.md) | Creates a `lip.toml`, migrates a `tooth.json`, packs a ZIP, views a package. | [`lip pkg init`](commands/pkg_init.md), [`lip pkg migrate`](commands/pkg_migrate.md), [`lip pkg pack`](commands/pkg_pack.md), [`lip pkg view`](commands/pkg_view.md) |
| [Settings](../interface/settings.md) | Language, GitHub proxy, auto restart, cache, updates of lip, the daemon, the version. | [`lip config`](commands/config.md), [`lip cache clean`](commands/cache_clean.md), [`lip self-update`](commands/self_update.md), [`lip daemon status`](commands/daemon.md), `lip --version` |

A command that the interface carries out behaves as its own page defines, with the selected workspace as its working directory, and with these differences:

- What it says is shown in the interface, in English.
- What it asks, it asks in a dialog.
- When it fails, the interface shows why and goes on.

## Preview and Apply

Every change of the packages of a workspace goes the same way: installing, updating, uninstalling, and setting up the game or the loader.

| Step | What happens |
| --- | --- |
| 1. Preview | lip carries the command out as a [dry run](cli.md) and shows what it would do. Nothing has changed. |
| 2. **Apply** or **Cancel** | **Apply** goes on. **Cancel** changes nothing. When the preview finds nothing to change, the only button is **Close**. |
| 3. License | Only when the change installs the game: lip shows the Minecraft EULA and the Microsoft Privacy Policy. **Accept** goes on; **Decline** changes nothing. |
| 4. Progress | `Esc` or `Ctrl+C` cancels; what was done so far is undone. |
| 5. Result | What the command said, and **✓ Done.** |

- Applying does not start over: what the preview read and downloaded is used as it is.
- When the preview of a setup names installed mods that have no version for the new game or loader, **Apply** uninstalls them.
- A failure shows the cause, hints when lip has any, and the path of the failure report below `.lip/logs` of the workspace.

## While the server runs

The server of a workspace runs while lip runs it, whoever started it: an interface, `lip run --managed`, or a plain `lip run` in a terminal. Meanwhile the interface does this for that workspace:

| What | While its server runs |
| --- | --- |
| Installing, updating and uninstalling packages ([Mods](../interface/mods.md), [Get mods](../interface/get_mods.md)) | Off. |
| Setting up a new server in its folder, changing its game or loader ([Workspaces](../interface/workspaces.md)) | Off. |
| `server.properties`, `allowlist.json`, `permissions.json` ([Game config](../interface/game_config.md)) | Shown, not changed or saved. Unsaved changes are kept until the server has stopped. |
| The configuration files of the mods ([Mod config](../interface/mod_config.md)) | Changed and saved as usual. The page notes that a change may need a restart. |
| Everything else: browsing mods, switching workspaces, [Tools](../interface/tools.md), [Settings](../interface/settings.md) | As usual. |

What is off stays on the screen, dimmed, with the reason **Stop the server first.**

This holds for the interface only. The commands `lip install`, `lip update`, `lip uninstall` and `lip setup` are not refused while the server runs; see [Running a server from the commands](../guides/server_from_commands.md).

Quitting the interface does not stop a server that the daemon runs. When lip carries out the interface itself, because it cannot use its daemon, quitting stops the servers that the interface started.

## Configuration files

| File | Read | Written |
| --- | --- | --- |
| `server.properties` | When the Game config page is shown. | On **Save**: the values of the properties that changed, and no other character. Not created when it does not exist. |
| `allowlist.json`, `permissions.json` | The same. A file that does not exist is read as an empty list. | On **Save**: the whole file, without comments. Created when it does not exist. |
| A `.json` file of a mod | When the file is selected on the Mod config page. | On **Save**: the whole file, without comments, each name where it was. Not created when it no longer exists. |
| Any other file of a mod | The same. | Never. |

- A file that lip cannot read is shown, and never written.
- A file that changed on disk since lip read it is overwritten only when you say so.
- Leaving a page with unsaved changes asks whether to save them.
- A saved change takes effect when the server is next started.

The files of a mod are those in `mods/<mod>/config/`; when there is none, the `.json`, `.ini`, `.toml`, `.yml` and `.yaml` files directly in `mods/<mod>/` other than `manifest.json`, and `mods/<mod>/data/config.json`.

## Languages

| Value of `language` | Interface |
| --- | --- |
| `en` | English |
| `zh-Hans` | Simplified Chinese |
| `zh-Hant` | Traditional Chinese |
| not set | The language of the system: English, or Chinese as its locale says. |

The language is chosen on the [Settings](../interface/settings.md#language) page, or with [`lip config`](commands/config.md); see [Configuration](configuration.md). It changes at once. What a command says is in English in every language.

## GitHub check

When the interface starts and `github_proxy` is not set, lip checks in the background whether it reaches GitHub, once, waiting up to 10 seconds. When it does not, the interface asks how to reach it:

| Answer | Does |
| --- | --- |
| A proxy | Asks for its URL, tests it, and sets `github_proxy` to it. |
| Connect directly | Sets `github_proxy` to an empty value: lip neither checks nor asks again. |
| Later | Changes nothing; lip checks again the next time the interface starts. |

See [Settings](../interface/settings.md#github-proxy).

## Exit status

`lip` exits with code 0 when you quit the interface, whatever the commands carried out in it did, and whatever code the servers exited with.

## Files

| File | The interface |
| --- | --- |
| `workspaces.json` in the state directory | Reads and writes the list of workspaces. |
| `config.toml` in the config directory | Reads `language` and `github_proxy`; writes them from the Settings page and from the GitHub check. |
| `.lip/config.toml` in a workspace | Reads and writes `restart` (Auto restart). |
| `server.properties`, `allowlist.json`, `permissions.json` in a workspace | Reads them; writes them on **Save**. |
| The configuration files of the mods, below `mods/` | Reads them; writes the `.json` ones on **Save**. |
| `.lip/logs/` in a workspace | Writes a failure report when a command fails. |
| `https://levimc.org/api/v4/mods/` | Reads the mod catalogue. Nothing of it is written to disk, and `github_proxy` is not used for it. |

The commands that the interface carries out read and write what their own pages say. [Files](files.md) lists every file and directory of lip.

## See also

- [A tour of the interface](../interface/tour.md)
- [Your first server](../intro/first_server.md)
- [Command line](cli.md)
- [Configuration](configuration.md)
