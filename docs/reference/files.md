# Files

This page lists every file and directory that lip reads or writes, so that you know what you may delete, back up or edit, and what you lose when you do.

lip writes to four places only: the workspace it operates on, its per-user directories, paths that you give as arguments, and its installation directory when it updates itself. It never writes to the system temporary directory. The programs it runs (the server, hook commands, installers and Wine) write where they like.

## The workspace

A **workspace** is the directory of one server. It is the current directory of a command, or the workspace selected in the interface.

| Path | Is | Written by |
| --- | --- | --- |
| `.lip/` | The records of lip about the workspace. See [The .lip directory](#the-lip-directory). | lip |
| `mods/` | The directory that LeviLamina loads mods from. Each mod is in `mods/<mod name>/`. | lip, and the mods themselves |
| `bedrock_server_mod.exe`, `bedrock_server.exe`, `bedrock_server` | The server that [`lip run`](commands/run.md) starts. See [The server files](#the-server-files). | lip |
| Everything else | The files of the game and of file packages, and what the server creates when it runs, such as `worlds/`. | lip and the server |
| `tooth_lock.json` | The state of a workspace of lip v0.x. See [State file](../concepts/state_file.md#workspaces-of-lip-v0-x). | lip v0.x |

lip creates `mods/` before an operation changes the workspace. `mods` must be a real directory: when it is a link, the operation fails.

A workspace is tied to nothing outside itself. You can move, rename or copy the directory while no lip command and no server uses it, and it keeps its installed packages.

### The .lip directory

On Windows `.lip` has the hidden attribute.

| Entry | Is | If you delete it |
| --- | --- | --- |
| `state.json` | The [state file](../concepts/state_file.md): the installed packages and the files each one owns. | lip no longer knows what is installed. The files stay, but lip cannot uninstall or update them. Do not delete it. |
| `state.json.tmp` | The state file while it is being written. | Do not touch it while a command runs. |
| `journal` | The record of the operation in progress, from which lip completes or undoes an interrupted operation. It exists only during an operation, or after one was interrupted. | lip can no longer undo the interrupted operation. Do not delete it; run `install`, `uninstall`, `update`, `setup` or `run` to recover. |
| `lock` | The file of the **workspace lock**. A process holds it while it carries out an operation, so that two operations never change the workspace at once. Its content means nothing. | Harmless while no command runs. Never delete, rename or replace it while one does. |
| `server.lock` | The file of the **server lock**. The process that runs the server holds it, so that a workspace has at most one running server. | Harmless while no server runs. Never delete, rename or replace it while one does. |
| `config.toml` | The [workspace configuration](configuration.md#workspace-configuration). | `restart` goes back to `never`. |
| `logs/` | The [failure reports](#failure-reports). | You lose the reports. Safe at any time. |
| `wine/` | On Linux and macOS: the Wine prefix that the Windows server and the hook commands run in. | Wine creates it again, and lip installs the Visual C++ runtime into it again. Stop the server first. |

lip may keep other entries in `.lip`. Do not rely on them.

`.lip` is reserved: a package cannot install a file into it, and a `purge` glob never matches it.

The two locks are independent. An operation does not take the server lock, and the server does not hold the workspace lock while it runs, so `lip install` works while a `lip run` runs. The interface is stricter and asks you to stop the server first.

### The server files

The game is the package `github.com/LiteLDev/bds`. Installing it writes the whole content of the Bedrock Dedicated Server archive, downloaded from `www.minecraft.net`, to the workspace root.

| The workspace has | Server file |
| --- | --- |
| A loader (LeviLamina) | `bedrock_server_mod.exe` |
| No loader, and the build `windows` | `bedrock_server.exe` |
| No loader, and the build `linux` | `bedrock_server` |

On Linux and macOS, lip gives `bedrock_server` the mode `0755` when it writes it.

When the game is installed, replaced or uninstalled, two lists decide what happens to its files:

| List | Paths | Effect |
| --- | --- | --- |
| Preserved | `server.properties`, `allowlist.json`, `permissions.json` | An existing file is kept when the game is installed, and is not deleted when the game is uninstalled. |
| Purged | `bedrock_server.exe.bak` (build `windows` only), `packet-statistics.txt`, `valid_known_packs.json`, `behavior_packs/`, `config/`, `definitions/`, `development_behavior_packs/`, `development_resource_packs/`, `development_skin_packs/`, `resource_packs/`, `world_templates/` | Deleted when the game is uninstalled, with everything below them, also files that the game did not install. Paths that another installed package owns are kept. |

### What survives a game change

Changing the game version with [`lip setup`](commands/setup.md) uninstalls the old game and installs the new one:

| Kept | Replaced or deleted |
| --- | --- |
| `server.properties`, `allowlist.json`, `permissions.json` | Every other file that the old game installed |
| `worlds/` and every other file that the server or you created, outside the purged paths | The purged paths above, with what you put into them |
| `mods/`, and the files of the packages that stay installed | The files of the packages that the setup uninstalls |
| `.lip/` | |

::: warning
Packs that you copied into `behavior_packs/`, `resource_packs/` or a `development_*` directory are deleted with those directories. Keep a copy outside the workspace before you change the game.
:::

### File names

lip refuses to write a file to a workspace when a segment of its path contains `:` or ends in `.` or a space, because Windows cannot hold such a name. The operation fails.

On Linux and macOS, lip treats the paths of the workspace as case-insensitive, as Wine does: `Mods/a.dll` and `mods/A.DLL` are the same file. A file is written in place of an existing file whose name differs only in case. When a path names more than one existing file that way, the operation fails.

## Per-user directories

| Directory | Windows | Linux and macOS | Default on Linux and macOS |
| --- | --- | --- | --- |
| Config | `%APPDATA%\lip` | `$XDG_CONFIG_HOME/lip` | `~/.config/lip` |
| Cache | `%LOCALAPPDATA%\lip\Cache` | `$XDG_CACHE_HOME/lip` | `~/.cache/lip` |
| Data | `%LOCALAPPDATA%\lip\Data` | `$XDG_DATA_HOME/lip` | `~/.local/share/lip` |
| State | `%LOCALAPPDATA%\lip\State` | `$XDG_STATE_HOME/lip` | `~/.local/state/lip` |

An `XDG_*` variable that is unset, empty or not an absolute path takes its default.

### Config directory

| Entry | Is |
| --- | --- |
| `config.toml` | The [user configuration](configuration.md#user-configuration). |
| `liprc.json` | The configuration of lip v0.x. lip [converts it](configuration.md#liprc-json) to `config.toml` and deletes it. |

Deleting the config directory resets `github_proxy` and `language`.

### Cache directory

The cache holds what lip downloaded and can download again. [`lip cache clean`](commands/cache_clean.md) deletes the whole directory.

| Entry | Is |
| --- | --- |
| `http-<H>` | A file: the body of a successful HTTPS GET of one URL. `<H>` is the lowercase hexadecimal SHA-256 of the URL, without its fragment, as the package wrote it, whatever proxy was used. |
| `<name>.tmp` | A file that is being written. |
| `self-update/` | What [`lip self-update`](commands/self_update.md) downloads and extracts, only while it runs. |

lip may keep other entries there. Do not rely on them.

Other programs may read and write the cache if they follow the same rules:

- **Writing.** Write an entry to a `.tmp` file under a name that no other process uses, then rename it. Never change an entry in place.
- **Reading.** An entry that exists is complete. A reader that knows what an `http-<H>` entry must hold checks it, and may delete it when it differs.
- **Deleting.** An `http-<H>` entry may be deleted at any time; a `.tmp` file when no process is writing it; `self-update` when no `lip self-update` runs.

When lip needs a ZIP archive and the cached entry of its URL is not a ZIP archive, lip deletes the entry and downloads the URL again, once.

Deleting the cache loses nothing: lip downloads again what it needs. A lip command that writes to the cache while you delete it may fail.

### Data directory

The data directory holds the Wine that lip installs on Linux and macOS, shared by every workspace, and what lip downloads to install the system dependencies.

Deleting it while no lip process runs loses nothing: lip installs the system dependencies again when it needs them. On Linux and macOS that means downloading Wine again.

### State directory

| Entry | Is |
| --- | --- |
| `workspaces.json` | The [workspace list](#workspaces-json) of the interface. |
| `daemon.sock` and other files of the daemon | On Linux and macOS, only when `XDG_RUNTIME_DIR` is not set to an absolute path. See [The daemon endpoint](#the-daemon-endpoint). |

Deleting it while no lip process runs loses nothing but the workspace list. The workspaces themselves are untouched; add them again in the interface.

### workspaces.json

The list of workspaces that the interface shows on its [Workspaces](../interface/workspaces.md) page: a JSON array of absolute paths, in the order they were added.

```json
["D:\\servers\\survival", "D:\\servers\\creative"]
```

- A list that is missing or is not a JSON array is read as empty.
- An element that is not an absolute path, or that names a directory already in the list, is left out.
- lip writes each path in its canonical form: `.` and `..` resolved, `\` as the separator on Windows, and no separator at the end unless the path is a root. Two paths are the same directory when their canonical forms are equal, ignoring case on Windows.

The interface adds its working directory to the list when it starts. Removing a workspace from the list changes no file in it. Commands do not use the list.

## The daemon endpoint

The [daemon](commands/daemon.md) listens where only your user can reach it:

| System | Endpoint |
| --- | --- |
| Windows | The named pipe `\\.\pipe\lip-<SID>`, `<SID>` being the security identifier of your user. |
| Linux and macOS | The Unix socket `daemon.sock` in the **daemon directory**: `$XDG_RUNTIME_DIR/lip` when `XDG_RUNTIME_DIR` is an absolute path, otherwise the state directory. |

The daemon directory has the mode `0700` and the socket the mode `0600`. The daemon deletes `daemon.sock` when it exits. The directory also holds other files of the daemon; do not rely on them.

## The installation directory

The installation directory is the one that holds the `lip` executable.

| Entry | Is |
| --- | --- |
| `install.json` | `{"kind": "msi"}`. The Windows installer writes it; it tells [`lip self-update`](commands/self_update.md) to update with the installer. Without it, lip counts as portable. lip never writes it. |
| `<file>.old` | A file that a portable update replaced and could not delete yet. lip deletes `<executable>.old` the next time it starts. |

## Failure reports

A command that fails, other than on a usage error, may write a **failure report** to `.lip/logs/` in its workspace, and names the file in a message:

```
Full report: .lip\logs\20260115T093012Z-4812.log
```

The name is the UTC time and a process ID. In the current version a report is a text file with the command line, the versions of lip and of the system, the directories lip uses, the error with its hints and causes, the entries of `.lip`, and a trace of what lip did. User names, passwords and query values in URLs are replaced by `***`. lip keeps the 20 newest reports and deletes older ones.

Attach the report when you ask for help. Its layout is not fixed and may change.

A [dry run](cli.md#dry-run) that fails may write one too; it is the only file a dry run writes.

## See also

- [How lip works](../concepts/how_lip_works.md): operations, the journal and the locks explained.
- [State file](../concepts/state_file.md)
- [Troubleshooting](../guides/troubleshooting.md)
- [Workspaces](../interface/workspaces.md) in the interface.
