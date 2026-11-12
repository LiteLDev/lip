# Interactive Interface

Run `lip` without arguments in a terminal to open the interactive interface: every command of lip, the mods of [levimc.org/mods](https://levimc.org/mods/), and the server of the workspace, in one full-screen program.

```shell
lip
```

The workspace is the current directory, as for every command. When the standard input or the standard output of lip is not a terminal (in a script or a pipe), `lip` without arguments writes its help instead.

## Tabs

Switch tabs with `Tab` and `Shift+Tab`, or with the number of the tab. The bottom line of the screen always lists the keys of what is shown.

| Tab | Does | Commands |
| --- | --- | --- |
| **1 Packages** | Lists the installed packages; installs, updates and uninstalls them. | [`lip list`](./commands/list.md), [`lip install`](./commands/install.md), [`lip update`](./commands/update.md), [`lip uninstall`](./commands/uninstall.md) |
| **2 Browse** | Searches, filters and sorts the mods of levimc.org, shows their details, versions and README, and installs them. | [`lip install`](./commands/install.md) |
| **3 Server** | Runs the server of the workspace in a window, shows what it writes, and sends it the lines you type. | [`lip run`](./commands/run.md) |
| **4 Settings** | Shows and changes the configuration; cleans the cache. | [`lip config`](./commands/config.md), [`lip cache clean`](./commands/cache_clean.md) |
| **5 Develop** | Creates a `lip.toml`, migrates a `tooth.json`, packs a ZIP archive, views a package. | [`lip pkg init`](./commands/pkg_init.md), [`lip pkg migrate`](./commands/pkg_migrate.md), [`lip pkg pack`](./commands/pkg_pack.md), [`lip pkg view`](./commands/pkg_view.md) |

Quit with `q` or `Ctrl+Q`.

## Installing, updating and uninstalling

Every change to the workspace is planned first: lip carries it out as a dry run (`--dry-run`) and shows what it would do. Nothing changes until you choose **Apply**.

While the change is applied, `Ctrl+C` cancels it and undoes what was done so far. When a command fails, the interface shows the cause, what you can do about it, and the path of the failure report below `.lip/logs`, and goes on.

In the install dialog, the packages are written as on the command line (see [`lip install`](./commands/install.md)), separated by spaces; the second field is `--platform`.

## Browsing mods

The Browse tab reads the mod catalogue of levimc.org (`https://levimc.org/api/v4/mods/`) when it is first opened; nothing of it is kept on disk, and `github_proxy` is not used for it. lip manages servers only, so the list starts with the mods that have a build for a server.

| Key | Does |
| --- | --- |
| `/` | Searches by name, id, author, tag and description. |
| `s` | Changes the order: relevance, stars, last update, newest. |
| `f` | Changes the side: server, client, any. |
| `t` | Changes the tag that the mods must carry. |
| `Enter` | Installs the selected mod. |
| `v` | Lists its versions; `Enter` installs the chosen one. |
| `r` | Shows its README. |
| `o` | Opens its page in the browser. |
| `R` | Reads the catalogue again. |

## Running the server

In the Server tab, `Enter` starts `bedrock_server_mod.exe` as [`lip run`](./commands/run.md) does. What the server writes is shown in the window; `PgUp` and `PgDn` scroll back. While the server runs, what you type is a line for the server, sent with `Enter`; `↑` and `↓` recall earlier lines.

`Ctrl+C` asks the server to stop, by sending it the line `stop`. Quitting lip while the server runs does the same, and lip waits until the server has exited.

The other tabs stay usable while the server runs. As with `lip run`, the workspace is not locked meanwhile, and changes to packages take effect when the server is started again.
