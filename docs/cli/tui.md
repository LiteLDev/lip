# Interactive Interface

Run `lip` without arguments in a terminal to open the interactive interface: a full-screen program that sets up a server, installs the mods of [levimc.org/mods](https://levimc.org/mods/), runs the server and edits its configuration, with the arrow keys and `Enter`.

```shell
lip
```

The interface works on one workspace at a time, the current directory at first (see [Workspaces](#workspaces)). When the standard input or the standard output of lip is not a terminal (in a script or a pipe), `lip` without arguments writes its help instead.

Like the commands, the interface runs in the [daemon](../guides/internal_mechanism.md#daemon) of lip; the `lip` you start shows it and passes on your keys. You can open it in several terminals at once.

## Layout

```
 lip 1.2.3 · D:\servers\survival                           ● Running
┌────────────────┐┌─ Home ──────────────────────────────────────────┐
│ Home           ││                                                 │
│ Server       ● ││                                                 │
│ Mods           ││                                                 │
│ Get mods       ││                    the page                     │
│ Game config    ││                                                 │
│ Mod config     ││                                                 │
│ Workspaces     ││                                                 │
│ Tools          ││                                                 │
│ Settings       ││                                                 │
└────────────────┘└─────────────────────────────────────────────────┘
 ↑↓ Move  Enter Open  Esc Quit
```

| Part | Shows |
| --- | --- |
| Top line | The version of lip, the selected workspace, and whether its server runs. |
| Sidebar | The nine [pages](#pages). The Server entry is marked while the server runs. |
| Page | The page selected in the sidebar. Questions, menus and progress open as dialogs above it. |
| Bottom line | The keys that work right now, each with a word or two. It changes as you move. |

The focus is either in the sidebar or in the page. In the sidebar, `↑` and `↓` show one page after the other, and `Enter` moves into the page shown; `Esc` moves back to the sidebar.

The interface needs a terminal of at least 60 columns and 16 rows; in a smaller one it asks you to enlarge the terminal, and the only key that works is `Ctrl+C`, which quits without asking. In a terminal narrower than 80 columns, the sidebar shrinks to one symbol per page while you are in a page, and shows the names again when you move back to it.

## Keys

The same keys do the same on every page:

| Key | Does |
| --- | --- |
| `↑`, `↓` | Move in a list or a menu. |
| `←`, `→` | Move between the sidebar and the page, between panes or buttons side by side; change the value of an option. |
| `Enter` | Opens or carries out what is selected. On a row of a list, opens the menu of what can be done with it. |
| `Space` | Checks a row where several can be chosen; switches an option on or off. |
| `Esc` | Goes back one level: from a dialog to the page, from the page to the sidebar. In the sidebar, asks whether to quit. |
| `Tab`, `Shift+Tab` | Move to the next or the previous area. |
| `PgUp`, `PgDn`, `Home`, `End` | Page through a list or a text. |
| `Ctrl+C` | On the [Server](#server) page while the server runs, stops the server. On any other page and in the sidebar, asks whether to quit. While a change is applied, cancels it. |

There are no letter shortcuts. Everything you can do is a row or a button on the screen that the arrow keys reach, and letters are only typed where a text field has the focus, such as a search box or the input line of the server.

To quit, press `Esc` in the sidebar, or `Ctrl+C` where it does not stop the server, and confirm. A running server keeps running (see [Server](#server)).

## Your first server in three steps

The Home page lists what a new server needs, in order. Select a step with `↑` and `↓` and press `Enter`; a finished step is checked off.

1. **Create the server.** lip asks for the directory of the server, the game version, the build and the loader, one after the other (see [Workspaces](#workspaces)). It then shows what it will install; choose **Apply**, and accept the Minecraft EULA when asked.
2. **Install mods.** This opens the [Get mods](#get-mods) page: move to a mod, press `Enter`, and choose to install it. Again lip shows what it will do before anything changes.
3. **Start the server.** This opens the [Server](#server) page and starts the server. Type server commands in its input line; `Ctrl+C` stops the server.

If GitHub cannot be reached from your network, lip asks how to reach it before you get far: see [GitHub proxy](#github-proxy).

## Pages

| Page | Does | Commands |
| --- | --- | --- |
| [Home](#home) | Shows the state of the workspace and the next step to take. | |
| [Server](#server) | Starts and stops the server, shows what it writes, and sends it what you type. | [`lip run`](./commands/run.md), [`lip config`](./commands/config.md) |
| [Mods](#mods) | Lists the installed packages; installs, updates and uninstalls them. | [`lip list`](./commands/list.md), [`lip install`](./commands/install.md), [`lip update`](./commands/update.md), [`lip uninstall`](./commands/uninstall.md) |
| [Get mods](#get-mods) | Searches, filters and sorts the mods of levimc.org, and installs them. | [`lip install`](./commands/install.md) |
| [Game config](#game-config) | Edits `server.properties`, `allowlist.json` and `permissions.json`. | |
| [Mod config](#mod-config) | Edits the configuration files of the installed mods. | |
| [Workspaces](#workspaces) | Switches between workspaces, sets up a new server, changes the game or the loader of one. | [`lip setup`](./commands/setup.md) |
| [Tools](#tools) | Creates a `lip.toml`, migrates a `tooth.json`, packs a ZIP archive, views a package. | [`lip pkg init`](./commands/pkg_init.md), [`lip pkg migrate`](./commands/pkg_migrate.md), [`lip pkg pack`](./commands/pkg_pack.md), [`lip pkg view`](./commands/pkg_view.md) |
| [Settings](#settings) | Language, GitHub proxy, cache, updates of lip, state of the daemon. | [`lip config`](./commands/config.md), [`lip cache clean`](./commands/cache_clean.md), [`lip self-update`](./commands/self_update.md), [`lip daemon status`](./commands/daemon.md) |

Every page works on the selected workspace, which the top line shows.

### Home

The Home page shows the state of the selected workspace and the three steps of a new server: create the server, install mods, start the server. Steps that are done are checked off; `Enter` on a step goes straight to it (see [Your first server in three steps](#your-first-server-in-three-steps)).

### Server

The Server page shows whether the server of the selected workspace runs, waits to be restarted, or with which code it exited; a button that starts or stops it; what the server writes; and an input line.

- Starting the server starts it as a managed server, as [`lip run --managed --detach`](./commands/run.md#managed-servers) does: the daemon of lip runs it, and it keeps running after you quit lip or close its terminal. Open lip again, or run `lip run --managed` in the workspace, to see it again.
- While the server runs, what you type in the input line is sent to the server with `Enter`.
- Stopping the server, with the button or with `Ctrl+C`, sends it the line `stop`.
- The restart option says whether a server that crashes is started again; it is the `restart` key of the workspace, as `lip config set restart on-failure --workspace` sets it (see [`lip run`](./commands/run.md#restarting)).

Each workspace has its own server, so servers of several workspaces can run at once; the [Workspaces](#workspaces) page marks the workspaces whose server runs. Interfaces open in several terminals, and `lip run --managed`, share the servers: each shows the server of its selected workspace, whoever started it.

When lip cannot use its daemon and runs the interface itself, the servers it starts are not managed: quitting lip asks them to stop, and lip waits until they have exited.

The other pages stay usable while the server runs. As with `lip run`, the workspace is not locked meanwhile, and changes to packages take effect when the server is started again.

### Mods

The Mods page lists the packages installed in the workspace, as [`lip list`](./commands/list.md) does.

- Above the list are two actions: installing from a source, and updating everything. For the first, write the packages as on the command line (see [`lip install`](./commands/install.md)): a package id, a URL, or a ZIP file or a directory on your disk.
- `Enter` on a package opens its menu: update it, update it to a version you name (`lip update <package>@<version>`), or uninstall it.
- `Space` checks packages; the menu then acts on all the checked ones at once.

A package that is installed only because another one depends on it cannot be uninstalled by itself: uninstall what depends on it.

### Get mods

The Get mods page reads the mod catalogue of levimc.org (`https://levimc.org/api/v4/mods/`); nothing of it is kept on disk, and `github_proxy` is not used for it. lip manages servers only, so the list holds the mods that have a build for a server.

The page has a search box, a sort order, a pane of filters, the list of mods, and the details of the selected one. `Enter` on a mod opens its menu: install the latest version, choose a version, read the README, or open its page in the browser.

### Game config

The Game config page edits the files of the server at the root of the workspace, in three sections:

| File | Holds |
| --- | --- |
| `server.properties` | The properties of the server. |
| `allowlist.json` | The players allowed on the server. |
| `permissions.json` | The permission of each player. |

`Enter` edits the selected entry, and `Space` switches a `true`/`false` value. Adding and removing a player are rows of the list.

Nothing is written until you save; leaving the page with unsaved changes asks whether to save them. lip changes only what you changed: in `server.properties`, the lines of the properties you changed, keeping every comment; in a JSON file, the values you changed, keeping the order of its members (comments in a JSON file are not kept).

The server creates `server.properties` when it first starts, so a new workspace may not have it yet; `allowlist.json` and `permissions.json` are created when you save them. Changes take effect when the server is started again.

### Mod config

The Mod config page lists the configuration files of the installed mods. A `.json` file opens as a tree whose values you edit; any other file is shown as it is, to be edited with a text editor. Saving works as on the [Game config](#game-config) page.

The configuration files of a mod are the files in `mods/<mod>/config/`; for a mod without one, the `.json`, `.ini`, `.toml`, `.yml` and `.yaml` files directly in `mods/<mod>/` (but `manifest.json`), and `mods/<mod>/data/config.json`. Most mods create them when the server first starts.

### Workspaces

lip remembers the workspaces you used in a list, `workspaces.json` in its state directory (`%LOCALAPPDATA%\lip\State` on Windows, `$XDG_STATE_HOME/lip` elsewhere). When the interface opens, the current directory is added to the list, if it is not there yet, and selected. The list is kept between runs of lip, and shared by the interfaces open at the same time.

The Workspaces page shows the list: which workspace is selected, which no longer exist, and which have a running server.

- Above the list are two actions: setting up a new server, and adding an existing directory, absolute or relative to the selected workspace.
- `Enter` on a workspace opens its menu: switch to it, change its game or loader version, or remove it from the list. Removing changes no file in the workspace; the selected workspace cannot be removed.

A new server is set up in four steps:

1. **The directory of the server**, absolute or relative to the selected workspace. A directory that does not exist is created.
2. **The game version**, read from the builds list of the server: the release versions, newest first, then the others.
3. **The build**: `windows`, the server that LeviLamina and its mods run on (through Wine off Windows), or `linux`, the Linux server without a loader. Only the builds that the version has are offered.
4. **The loader**, for the `windows` build: the versions of LeviLamina that support the game, newest first, or no loader. lip finds them as [`lip setup`](./commands/setup.md#the-loader) does, which may take a moment.

lip then creates the directory, adds it to the list, selects it, and carries out [`lip setup`](./commands/setup.md) with your choices, e.g. `lip setup --game 1.21.50 --loader levilamina@1.1.0`. `Esc` before the last choice stops, creating nothing.

Changing the game or the loader of a workspace carries out `lip setup` in it again, with the same choices but the directory.

### Tools

The Tools page is for those who make packages. Each of its rows asks for what its command needs and shows the result:

| Row | Command |
| --- | --- |
| Create a `lip.toml` | [`lip pkg init`](./commands/pkg_init.md) |
| Migrate a `tooth.json` | [`lip pkg migrate`](./commands/pkg_migrate.md) |
| Pack a ZIP archive | [`lip pkg pack`](./commands/pkg_pack.md) |
| View a package | [`lip pkg view`](./commands/pkg_view.md) |

Paths are absolute or relative to the selected workspace.

### Settings

| Row | Does | Command |
| --- | --- | --- |
| Language | Chooses the language of the interface, or follows the system (see [Languages](#languages)). | `lip config set language <value>`, `lip config delete language` |
| GitHub proxy | Sets, tests or removes the proxy through which lip reaches GitHub (see [GitHub proxy](#github-proxy)). | `lip config set github_proxy <URI>` |
| Cache | Cleans the cache. | [`lip cache clean`](./commands/cache_clean.md) |
| Updates | Checks whether a newer lip is released, and installs it. | [`lip self-update --check`](./commands/self_update.md), [`lip self-update`](./commands/self_update.md) |
| Daemon | Shows the state of the daemon of lip. | [`lip daemon status`](./commands/daemon.md) |
| Version | Shows the version of lip. | `lip --version` |

The interface cannot stop the daemon, because the daemon runs the interface: quit the interface and run [`lip daemon stop`](./commands/daemon.md).

## Preview and Apply

Every change to the workspace (installing, updating, uninstalling, setting up) goes the same way:

1. **Preview.** lip carries the command out as a dry run (`--dry-run`) and shows what it would do.
2. **Apply or Cancel.** Nothing changes until you choose **Apply**. **Cancel** leaves the workspace as it is, so it is also how you only look at what a command would do.
3. **The license**, when the change sets up the game, e.g. a new server, or installing `LeviLamina` in a workspace without a game (see [`lip setup`](./commands/setup.md)): lip shows the links to the Minecraft EULA and the Microsoft Privacy Policy and asks whether you accept them; nothing changes unless you accept.
4. **Progress.** `Esc` or `Ctrl+C` cancels the change and undoes what was done so far.
5. **Result.** What the command said.

When the preview of a setup lists mods that would be uninstalled because they do not work with the new game, choosing **Apply** uninstalls them.

When a command fails, the interface shows the cause, what you can do about it, and the path of the failure report below `.lip/logs` of the workspace, and goes on.

## GitHub proxy

lip downloads LeviLamina and most mods from GitHub. When the interface opens and `github_proxy` is not set (not even to an empty value, see [Configuration](./configuration.md)), lip checks in the background whether it reaches GitHub, while you use the interface: it requests `https://github.com/LiteLDev/LeviLamina.git/info/refs?service=git-upload-pack` and waits up to 10 seconds for an answer.

When GitHub does not answer, the interface asks how to reach it:

| Choice | Does |
| --- | --- |
| A proxy | Asks for the URL prefix of a GitHub proxy, tests it with the same request through it, and saves it as `github_proxy` when it works. When it does not, you can still keep it. |
| Direct | Sets `github_proxy` to an empty value: lip connects to GitHub directly, and does not check or ask again. |
| Later | Changes nothing; lip checks again when the interface next opens. |

lip does not suggest any proxy itself. You can change `github_proxy` at any time on the [Settings](#settings) page or with [`lip config`](./commands/config.md); once it is set, the check does not happen.

## Languages

The interface is in English, Simplified Chinese or Traditional Chinese. Choose one on the [Settings](#settings) page; the screen changes at once. The choice is the `language` key of the user configuration (see [Configuration](./configuration.md)):

| Value | Language |
| --- | --- |
| `en` | English |
| `zh-Hans` | Simplified Chinese |
| `zh-Hant` | Traditional Chinese |

```shell
lip config set language zh-Hans
lip config delete language
```

When `language` is not set, the interface follows the system: the display language of your user on Windows; elsewhere the first of the environment variables `LC_ALL`, `LC_MESSAGES` and `LANG` that names a language. A Chinese locale of Taiwan, Hong Kong or Macao, or one marked `Hant`, gives Traditional Chinese, unless it is marked `Hans`; any other Chinese locale gives Simplified Chinese. With any other language, the interface is in English.

Only the text of the interface itself is translated. What a command says, such as the lines of a preview, its messages and its errors, is shown in English, as on the command line.
