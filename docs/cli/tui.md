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
| Bottom line | The keys that work right now, each with a word or two. It changes as you move. In a narrow terminal the last ones are left out; `Esc` is always shown. |

The focus is either in the sidebar or in the page; the interface opens with it in the Home page. In the sidebar, `↑` and `↓` show one page after the other, and `Enter` moves into the page shown; `Esc` moves back to the sidebar.

The interface needs a terminal of at least 60 columns and 16 rows; in a smaller one it asks you to enlarge the terminal, and the only key that works is `Ctrl+C`, which quits without asking. In a terminal narrower than 80 columns, the sidebar shrinks to one symbol per page while you are in a page, and shows the names again when you move back to it.

## Keys

The same keys do the same on every page:

| Key | Does |
| --- | --- |
| `↑`, `↓` | Move in a list or a menu. |
| `←`, `→` | Move between the sidebar and the page, between panes or buttons side by side; change the value of an option. |
| `Enter` | Opens or carries out what is selected. On a row of a list, opens the menu of what can be done with it. |
| `Space` | Checks a row where several can be chosen; switches an option on or off. |
| `Esc` | Goes back one level: from a dialog to the page, inside a page from an inner part to the one before it (from the input line of the server to its output, from the values of a file to the list of files), from the page to the sidebar. In the sidebar, asks whether to quit. |
| `Tab`, `Shift+Tab` | Move to the next or the previous area, round the sidebar and the areas of the page. |
| `PgUp`, `PgDn`, `Home`, `End` | Page through a list or a text. |
| `Ctrl+C` | On the [Server](#server) page while the server runs, stops the server. On any other page and in the sidebar, asks whether to quit. While a change is applied, cancels it. |

There are no letter shortcuts. Everything you can do is a row or a button on the screen that the arrow keys reach, and letters are only typed where a text field has the focus, such as a search box or the input line of the server.

To quit, press `Esc` in the sidebar, or `Ctrl+C` where it does not stop the server, and confirm. A running server keeps running (see [Server](#server)).

## Your first server in three steps

The Home page lists what a new server needs, in order. Select a step with `↑` and `↓` and press `Enter`; a finished step is checked off.

1. **Create the server.** lip offers the current directory when it has no server yet, or lets you [choose another folder](#choosing-a-folder-or-a-file); then it asks for the game version and the loader, one after the other (see [Workspaces](#workspaces)). It then shows what it will install; choose **Apply**, and accept the Minecraft EULA when asked.
2. **Install mods.** This opens the [Get mods](#get-mods) page: move to a mod, press `Enter`, choose to install it, and choose a version. Again lip shows what it will do before anything changes.
3. **Start the server.** This opens the [Server](#server) page and starts the server. Type server commands in its input line; `Ctrl+C` stops the server. Without a server there is nothing to start, and lip points you back to the first step.

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

The Server page shows whether the server of the selected workspace runs, waits to be restarted, or with which code it exited; a button that starts or stops it, and the **Auto restart** option; what the server writes; and, while the server runs, an input line. In a workspace without a server, the page offers **New server…** instead, which sets one up as the [Workspaces](#workspaces) page does.

- Starting the server starts it as a managed server, as [`lip run --managed --detach`](./commands/run.md#managed-servers) does: the daemon of lip runs it, and it keeps running after you quit lip or close its terminal. Open lip again, or run `lip run --managed` in the workspace, to see it again.
- While the server runs, what you type in the input line is sent to the server with `Enter`; `↑` and `↓` bring back the lines you sent before. `Esc` leaves the input line for the output, and `Tab` goes on from the button to the output and to the input line.
- While the output has the focus, `↑`, `↓`, `PgUp`, `PgDn`, `Home` and `End` scroll it; otherwise it follows what the server writes.
- Stopping the server, with the button or with `Ctrl+C`, sends it the line `stop`.
- **Auto restart** says whether a server that crashes is started again; it is the `restart` key of the workspace, as `lip config set restart on-failure --workspace` sets it (see [`lip run`](./commands/run.md#restarting)). The [Settings](#settings) page has the same option.

Each workspace has its own server, so servers of several workspaces can run at once; the [Workspaces](#workspaces) page marks the workspaces whose server runs. Interfaces open in several terminals, and `lip run --managed`, share the servers: each shows the server of its selected workspace, whoever started it.

When lip cannot use its daemon and runs the interface itself, the servers it starts are not managed: quitting lip asks them to stop, and lip waits until they have exited.

While the server runs, the interface changes neither the packages nor the game configuration of its workspace: see [While the server runs](#while-the-server-runs).

### Mods

The Mods page lists the packages installed in the workspace, as [`lip list`](./commands/list.md) does.

- Above the list are the actions that fit the workspace: **Install from source…** always; **Update all** once you have installed mods, and **Get mods**, which opens the [Get mods](#get-mods) page, before that; **New server…** in a workspace without a game. For the first, type the packages as on the command line (see [`lip install`](./commands/install.md)), such as a package id or a URL, or [browse](#choosing-a-folder-or-a-file) for a ZIP file or a folder on your disk.
- `Enter` on a mod opens its menu: see its details, update it, update it to a version you name (`lip update <package>@<version>`), or uninstall it. The details are those that the [Get mods](#get-mods) page shows for the mod; when the catalogue of levimc.org has no such mod, or cannot be read, lip says that it has no information about it. The menu of the game and of the loader leads to the [Workspaces](#workspaces) page, where their versions are changed.
- `Space` checks the mods you installed; the menu then updates or uninstalls all the checked ones at once.

A package that is installed only because another one depends on it can be neither checked nor uninstalled by itself: uninstall what depends on it.

### Get mods

The Get mods page reads the mod catalogue of levimc.org (`https://levimc.org/api/v4/mods/`); nothing of it is kept on disk, and `github_proxy` is not used for it. lip manages servers only, so the list holds the mods that have a build for a server.

The page has a search box, a sort order, a row that opens the filters (game version, tags, license, dependencies), the list of mods, and the details of the selected one. Each row of the list shows the downloads, the stars and the date of the last update of the mod, as levimc.org does, and marks an installed mod; downloads that are not known are left out. The sort orders are those of levimc.org:

| Order | The mods come |
| --- | --- |
| Relevance | Best match of the search first. |
| Popularity | Most popular first. The popularity of a mod is computed from its downloads and its stars together, as on levimc.org, so it is neither the order of the downloads nor that of the stars. |
| Updated | Last updated first. |
| Newest | Last published first. |

`Enter` on a mod opens its menu: **Install…**, read the README, or open its page in the browser. Installing always goes through choosing a version; there is no way to install "the latest" unseen:

- When the mod has variants, they are tabs on one row at the top, all of them; `←` and `→` change the variant. When the row is too narrow for all of them, it scrolls sideways and marks the side on which more are hidden; it never takes a second row.
- Below are the versions of the chosen variant that fit the game and the LeviLamina of the workspace, newest first, and only those. When none fits, a line says which game or which LeviLamina rules them out.
- The last row, **Custom…**, takes a branch, a tag or a commit instead, e.g. `branch:main`.

In a workspace without a server there is nothing that a version could fit: lip says so and offers to create the server first (see [Workspaces](#workspaces)).

### Game config

The Game config page edits the files of the server at the root of the workspace, in three sections:

| File | Holds |
| --- | --- |
| `server.properties` | The properties of the server. |
| `allowlist.json` | The players allowed on the server. |
| `permissions.json` | The permission of each player. |

`Enter` edits the selected entry, and `Space` switches a `true`/`false` value; on a property you changed, `Enter` also offers to reset it. **Add player…** is the first row of each list of players, and `Enter` on a player opens its menu, which removes the player.

Nothing is written until you choose **Save** at the bottom of the page; **Discard** drops your changes, and leaving the page with unsaved changes asks whether to save them. When a file was changed on disk after lip read it, lip asks before it overwrites it. lip changes only what you changed: in `server.properties`, the lines of the properties you changed, keeping every comment; in a JSON file, the values you changed, keeping the order of its members (comments in a JSON file are not kept).

The server creates `server.properties` when it first starts, so a new workspace may not have it yet; `allowlist.json` and `permissions.json` are created when you save them. The server reads the three files when it starts.

While the server runs, the page is read-only: you can look through the files, but neither change nor save them (see [While the server runs](#while-the-server-runs)).

### Mod config

The Mod config page lists the configuration files of the installed mods. A `.json` file opens as a tree: `Enter` opens an object or a list and edits a value, `Space` switches a `true`/`false` value, and `Esc` goes up a level; a list has a row that adds a value, and `Enter` on one of its values offers to remove it. Any other file is shown as it is, to be edited with a text editor. As on the [Game config](#game-config) page, nothing is written until you choose **Save**, and leaving with unsaved changes asks; a file that no longer exists is not created. Unlike the game configuration, the configuration of a mod can be saved while the server runs; the mod may read it only when the server is next started (see [While the server runs](#while-the-server-runs)).

When no installed mod has a configuration file, the page says that there is no mod to configure, and offers **Get mods** instead. That does not mean that no mod is installed: a mod may have no configuration, or keep it where lip does not look.

The configuration files of a mod are the files in `mods/<mod>/config/`; for a mod without one, the `.json`, `.ini`, `.toml`, `.yml` and `.yaml` files directly in `mods/<mod>/` (but `manifest.json`), and `mods/<mod>/data/config.json`. Most mods create them when the server first starts.

### Workspaces

lip remembers the workspaces you used in a list, `workspaces.json` in its state directory (`%LOCALAPPDATA%\lip\State` on Windows, `$XDG_STATE_HOME/lip` elsewhere). When the interface opens, the current directory is added to the list, if it is not there yet, and selected. The list is kept between runs of lip, and shared by the interfaces open at the same time.

The Workspaces page shows the list: which workspace is selected, which no longer exist, and which have a running server.

- Above the list are two actions: **New server…**, and **Add existing folder…**, which lets you [choose the folder](#choosing-a-folder-or-a-file).
- `Enter` on a workspace opens its menu: switch to it, change its game or loader version, or remove it from the list. Removing changes no file in the workspace; the selected workspace cannot be removed. A workspace whose directory no longer exists can only be removed, and the versions of a workspace cannot be changed while its server runs.

A new server is set up in up to four steps:

1. **The folder of the server**, [chosen in the folder browser](#choosing-a-folder-or-a-file). A folder that does not exist yet can be chosen too, and is created. When the selected workspace has no server yet, lip first offers that workspace itself.
2. **The game version**, read from the builds list of the server: the release versions, newest first, then the others. Each version shows whether a LeviLamina supports it. Mods need LeviLamina, and not every game version has one, so choose a supported version for a server with mods. When lip cannot find out, the list shows the versions without that.
3. **The build**, only when lip runs on Linux on x64 and the version has both builds: `windows`, the server that LeviLamina and its mods run on (through Wine off Windows), or `linux`, the Linux server without a loader. Everywhere else the Linux server cannot run, so the build is `windows` and lip does not ask; nor does it ask when the version has only one build.
4. **The loader**, for the `windows` build: the versions of LeviLamina that support the game, newest first, or no loader. lip finds them as [`lip setup`](./commands/setup.md#the-loader) does, which may take a moment.

lip then creates the directory, adds it to the list, selects it, and carries out [`lip setup`](./commands/setup.md) with your choices, e.g. `lip setup --game 1.21.50 --loader levilamina@1.1.0`. `Esc` goes back one step, and in the first step stops, creating nothing. Afterwards the new workspace is the selected row of the list.

Changing the game or the loader of a workspace carries out `lip setup` in it again, with the same choices but the folder; each step starts on what the workspace has now.

### Tools

The Tools page is for those who make packages. Each of its rows asks for what its command needs and shows the result:

| Row | Command |
| --- | --- |
| Create a `lip.toml` | [`lip pkg init`](./commands/pkg_init.md) |
| Migrate a `tooth.json` | [`lip pkg migrate`](./commands/pkg_migrate.md) |
| Pack a ZIP archive | [`lip pkg pack`](./commands/pkg_pack.md) |
| View a package | [`lip pkg view`](./commands/pkg_view.md) |

Where a row needs a folder or a file, you [choose it in the folder browser](#choosing-a-folder-or-a-file): packing asks for the folder of the package and then for the file to write. Viewing a package first asks which one: a mod installed in the workspace, chosen from their list; a package that you type as on the command line; or a ZIP file or a folder on your disk.

### Settings

| Row | Does | Command |
| --- | --- | --- |
| Language | Chooses the language of the interface, or follows the system (see [Languages](#languages)). | `lip config set language <value>`, `lip config delete language` |
| GitHub proxy | Sets and tests the proxy through which lip reaches GitHub, connects directly, or removes the setting (see [GitHub proxy](#github-proxy)). | `lip config set github_proxy <URI>`, `lip config delete github_proxy` |
| Auto restart | Says whether the server of the selected workspace is started again when it crashes (see [Server](#server)). | `lip config set restart on-failure --workspace` |
| Clear cache | Deletes the downloaded files. | [`lip cache clean`](./commands/cache_clean.md) |
| Check for updates | Checks whether a newer lip is released, and offers to install it. | [`lip self-update --check`](./commands/self_update.md) |
| Install update | Installs the newer release; the row is there once a check has found one. | [`lip self-update`](./commands/self_update.md) |
| Daemon | Shows the state of the daemon of lip; `Enter` asks it again. | [`lip daemon status`](./commands/daemon.md) |
| About | Shows the version of lip. | `lip --version` |

The interface cannot stop the daemon, because the daemon runs the interface: quit the interface and run [`lip daemon stop`](./commands/daemon.md).

## Preview and Apply

Every change to the workspace (installing, updating, uninstalling, setting up) goes the same way:

1. **Preview.** lip carries the command out as a dry run (`--dry-run`) and shows what it would do.
2. **Apply or Cancel.** Nothing changes until you choose **Apply**. **Cancel** leaves the workspace as it is, so it is also how you only look at what a command would do. When the preview finds that there is nothing to change, e.g. everything is installed already, there is nothing to apply, and the only button is **Close**.
3. **The license**, when the change sets up the game, e.g. a new server, or installing `LeviLamina` in a workspace without a game (see [`lip setup`](./commands/setup.md)): lip shows the links to the Minecraft EULA and the Microsoft Privacy Policy and asks whether you accept them; nothing changes unless you accept.
4. **Progress.** `Esc` or `Ctrl+C` cancels the change and undoes what was done so far.
5. **Result.** What the command said.

Applying does not start over: what the preview read from the network and downloaded (the versions, the package files, the archives) is used as it is, so applying takes only as long as the changes to the workspace do. A new server shares it from its first step on, so the versions that its steps read are not read again either.

When the preview of a setup lists mods that would be uninstalled because they do not work with the new game, choosing **Apply** uninstalls them.

When a command fails, the interface shows the cause, what you can do about it, and the path of the failure report below `.lip/logs` of the workspace, and goes on.

## Choosing a folder or a file

Wherever lip needs a folder or a file, it opens a folder browser, so you never have to type a path: for the folder of a new server and for an existing folder to add ([Workspaces](#workspaces)), for a ZIP file or a folder to install ([Mods](#mods)), and for the folder to pack, the file to write and a package to view ([Tools](#tools)).

The browser starts in the folder of the selected workspace. It has a path bar, the list of what the folder shown holds (`..` first, then its folders, then the files that can be chosen, if any), and buttons.

| Key | Does |
| --- | --- |
| `↑`, `↓`, `PgUp`, `PgDn`, `Home`, `End` | Move in the list. |
| `Enter`, `→` | On a folder, open it. `Enter` on a file chooses it. |
| `←`, or `Enter` on `..` | Go up one level. Above the top folder of a drive is the list of the drives (on Windows; elsewhere the top is `/`). |
| `Tab`, `Shift+Tab` | Move between the path bar, the list, the file name (when a file is to be written) and the buttons. |
| `Esc` | In the path bar, goes back to the list; otherwise closes the browser, choosing nothing. |

- **Select this folder** chooses the folder shown; **Save** chooses the file name typed below the list, in the folder shown.
- **New folder…** asks for a name and makes a folder of that name in the folder shown.
- In the path bar you can still type or paste a path and press `Enter` to go there: an absolute path, or one relative to the folder shown.

Folders and files that are hidden, or whose name starts with a dot (such as `.lip`), are not listed; type their path in the path bar to reach them. A folder that lip may not read says so, and you can only go back up.

## While the server runs

While the server of the selected workspace runs, the interface changes neither its packages nor its game configuration:

| What | While the server runs |
| --- | --- |
| Installing, updating and uninstalling mods ([Mods](#mods), [Get mods](#get-mods)) | Off. |
| Setting up or changing the game or the loader ([Workspaces](#workspaces)) | Off. |
| `server.properties`, `allowlist.json`, `permissions.json` ([Game config](#game-config)) | Read-only. |
| The configuration files of the mods ([Mod config](#mod-config)) | Can be changed and saved. The server may need a restart before a change takes effect, as the page says. |

What is off stays on the screen, shown as disabled, with the reason: stop the server first. Everything else works as usual, such as browsing the mods of levimc.org, the Tools and the Settings.

This holds whoever started the server: the interface, `lip run --managed`, or a plain [`lip run`](./commands/run.md) in another terminal. It holds for the interface only: the commands `lip install`, `lip update`, `lip uninstall` and `lip setup` are not refused while the server runs (see [Running a Server](../guides/running_server.md#while-the-server-runs)).

## GitHub proxy

lip downloads LeviLamina and most mods from GitHub. When the interface opens and `github_proxy` is not set (not even to an empty value, see [Configuration](./configuration.md)), lip checks in the background whether it reaches GitHub, while you use the interface: it requests `https://github.com/LiteLDev/LeviLamina.git/info/refs?service=git-upload-pack` and waits up to 10 seconds for an answer.

When GitHub does not answer, the interface asks how to reach it:

| Choice | Does |
| --- | --- |
| Use a proxy | Asks, on the [Settings](#settings) page, for the URL prefix of a GitHub proxy, tests it with the same request through it, and saves it as `github_proxy` when it works. When it does not, you can enter another one, or still save it. |
| Connect directly | Sets `github_proxy` to an empty value: lip connects to GitHub directly, and does not check or ask again. |
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
