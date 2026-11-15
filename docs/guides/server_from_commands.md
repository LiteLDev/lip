# A server from the commands

After this page you can set up a Minecraft Bedrock server, add mods to it, start it, keep it running after you close the terminal, and stop it, all with `lip` commands. Everything here can also be done in the interface: see [Your first server](../intro/first_server.md).

## Before you start

- Install lip: see [Installation](../intro/installation.md). You need nothing else. lip downloads the game, the loader and what they need of your system.
- Pick a folder for the server. lip calls it the **workspace**. Every command works on the folder you are in, so `cd` into it first.

::: code-group

```powershell [Windows]
mkdir D:\servers\survival
cd D:\servers\survival
```

```sh [Linux / macOS]
mkdir -p ~/servers/survival
cd ~/servers/survival
```

:::

## Set up the server

Run [`lip setup`](../reference/commands/setup.md) with the version of the game you want:

```sh
lip setup --game 1.26.50
```

Replace `1.26.50` with the version you want. This installs three things into the folder:

- **The game**: the Bedrock Dedicated Server of Minecraft, in that version.
- **The loader**: LeviLamina, which loads mods into the game. lip takes the newest LeviLamina that supports the game version.
- **Dependencies**: the packages that LeviLamina needs.

When it is done, lip writes:

```text
✓ The server is set up. Run it with 'lip run', or install mods with 'lip install'.
```

### Choose the build and the loader

Three options change what is set up. The full list is on the [`lip setup`](../reference/commands/setup.md) page.

| You want | Run |
| --- | --- |
| The Windows server with the newest LeviLamina for it (the default) | `lip setup --game 1.26.50` |
| A particular LeviLamina version | `lip setup --game 1.26.50 --loader levilamina@<version>` |
| The Windows server without a loader, so without mods | `lip setup --game 1.26.50 --no-loader` |
| The Linux server, which has no loader | `lip setup --game 1.26.50 --build linux` |

The Windows server also runs on Linux and macOS, through Wine. See [Linux and macOS](linux_macos.md) for what runs where.

If you do not care which game version you get, let LeviLamina choose. This sets up the newest LeviLamina and the newest game it supports:

```sh
lip install LeviLamina
```

In a folder that already has a server, this is a setup too, and can change the game. See [Change the game later](#change-the-game-later).

### Accept the Minecraft EULA

Before lip downloads the game, it names the two documents the game is under and asks you to accept them:

```text
The Minecraft server is available under the Minecraft End User License Agreement (https://www.minecraft.net/eula) and the Microsoft Privacy Policy (https://go.microsoft.com/fwlink/?LinkId=521839). Read them before you go on.
Enter yes to accept the Minecraft EULA and the Privacy Policy:
```

Type `yes` (or `y`) and press Enter. Any other answer stops the command, and nothing changes.

lip does not remember the answer. It asks every time a setup installs a game.

In a script, a service or a container, lip cannot ask, and the setup fails. If you accept the documents, say so with `--accept-eula`:

```sh
lip setup --game 1.26.50 --accept-eula
```

### See what a setup would do

Add `--dry-run` (or `-n`) to see the plan without changing anything:

```sh
lip setup --game 1.26.50 --dry-run
```

lip names every package it would install or uninstall, says that it would ask about the EULA, and ends with `Dry run: nothing was changed.` See [Dry run](managing_packages.md#dry-run).

## Install mods

Install a mod with [`lip install`](../reference/commands/install.md) and its name:

```sh
lip install LegacyScriptEngine#lua
```

lip installs the mod and everything it depends on. The mod goes to `mods/<name>/` in the workspace.

A mod needs a server. In a folder that has no game yet, `lip install` of a mod fails and tells you to run `lip setup` first.

[Managing packages](managing_packages.md) covers the ways to name a package, and how to update, list and uninstall.

## Run the server

Start the server with [`lip run`](../reference/commands/run.md):

```sh
lip run
```

lip starts the server file that the workspace was set up for:

| The workspace has | lip runs |
| --- | --- |
| a loader | `bedrock_server_mod.exe` |
| no loader, and the Windows server | `bedrock_server.exe` |
| no loader, and the Linux server | `bedrock_server` (only on Linux on x64) |

Before it starts a Windows server, lip installs what the server needs of your system when it is missing: the Microsoft Visual C++ Redistributable, and on Linux and macOS a Wine of its own. On Windows, you may be asked to allow the installer. See [Linux and macOS](linux_macos.md).

### Use the console

The server writes to your terminal. What you type goes to the server, so you type server commands as usual:

```text
list
```

### Stop the server

Press Ctrl+C. lip types `stop` into the server, the server saves the world and exits, and then lip exits.

Pressing Ctrl+C again does not cut the server off. lip always waits for the server to exit. If the server hangs, end its process with the tools of your system.

Closing the terminal window also asks the server to stop. To keep it running without a terminal, use a [managed server](#keep-the-server-running).

### Exit status

`lip run` exits with the exit code of the server. On Linux and macOS, when a signal ends the server, the code is 128 plus the number of the signal, for example 137 for `SIGKILL`. Through Wine, it is the code that `wine` exits with.

When the server ran, lip writes no error of its own and no failure report, whatever the code. A script can test the code:

::: code-group

```powershell [Windows]
lip run
if ($LASTEXITCODE -ne 0) { echo "The server failed with code $LASTEXITCODE" }
```

```sh [Linux / macOS]
lip run || echo "The server failed with code $?"
```

:::

When lip cannot start the server at all, it says why and exits with a non-zero code. See [The server does not start](troubleshooting.md#the-server-does-not-start).

## Keep the server running

A plain `lip run` belongs to its terminal: close the terminal, and the server stops. A **managed server** belongs to the background process of lip, the [daemon](../concepts/how_lip_works.md#the-daemon-and-its-clients), instead. It keeps running whatever happens to your terminal.

### Start it

```sh
lip run --managed
```

This starts the server in the daemon and **attaches** your terminal to it: you see its output and type its commands, as with a plain `lip run`.

To start it without attaching, add `--detach`. The command returns as soon as the server has started:

```sh
lip run --managed --detach
```

### Come back to it

Run the same command again, in the same folder, from any terminal:

```sh
lip run --managed
```

A managed server of this workspace runs already, so lip starts nothing and attaches you to it. You first see part of what the server wrote before you came. Several terminals can be attached at once.

### Leave without stopping it

Close the terminal window or tab. Your terminal detaches, and the server keeps running.

::: warning
Ctrl+C does not detach. While you are attached, Ctrl+C asks the server to stop, as it does for a plain `lip run`.
:::

### Stop it

A managed server stops only when someone asks:

- **From an attached terminal**: press Ctrl+C, or type `stop`. Your terminal stays attached until the server has exited.
- **From the interface**: open the [Server page](../interface/server.md) of the workspace and choose Stop.

Nothing else stops it. [`lip daemon stop`](../reference/commands/daemon.md) refuses while a managed server runs, and the daemon does not exit by itself while it runs one.

### What you should know

- **The interface uses managed servers.** A server you start on the Server page is the same managed server that `lip run --managed` attaches to. You can start it in one and watch it in the other.
- **One per workspace.** The daemon runs at most one managed server for a folder.
- **It keeps the environment it was started with.** The server runs in the folder and with the environment variables of the terminal that started it, not of the terminals that attach later.
- **It needs the daemon.** When lip cannot use its daemon, `lip run --managed` fails and tells you to run the server without `--managed`. See [lip carries out the command itself](troubleshooting.md#lip-carries-out-the-command-itself).
- **It does not survive a restart of the machine.** The daemon is an ordinary process of your user. lip does not start servers when the system starts. For that, have the service manager of your system run a plain `lip run` in the workspace.

With `--managed`, the exit status is 0 when the command ends with `--detach` or because your terminal detached. When the server stops while you are attached, it is the exit code of the server, as for a plain `lip run`.

## Restart the server when it crashes

By default, a server that exits stays stopped. To have lip start it again after a crash, turn on `restart` for the workspace:

```sh
lip config set restart on-failure --workspace
```

From then on, when the server exits with a code other than 0, lip waits 5 seconds and starts it again:

```text
⚠ The server exited with code 1, and restart is on-failure: lip restarts it in 5 seconds (restart 1, at most 5 times within 10 minutes).
```

- A server that exits with code 0, or that you asked to stop, is not restarted.
- Ctrl+C during the 5 seconds cancels the restart.
- After 5 restarts within 10 minutes, lip gives up and says so. Read what the server wrote to find out why it keeps failing.
- lip reads the setting when the server exits. You can change it while the server runs.

To turn it off again:

```sh
lip config set restart never --workspace
```

The setting works for plain and managed servers, and is the "Auto restart" switch of the interface. See [Configuration](configuration.md#restart).

## One server per workspace

A workspace runs one server at a time. While a server of the folder runs, a second `lip run` fails at once:

```text
error: A server of the workspace 'D:\servers\survival' is already running: another lip run, the interface of lip or the daemon of lip runs it, and holds the server lock 'D:\servers\survival\.lip\server.lock'. Stop that server first, or attach to it with 'lip run --managed' when it is a managed server.
```

To run two servers, give each its own folder, and its own ports in `server.properties`.

## Change the game later

`lip update` never changes the game or the loader. `lip install` does only when you name LeviLamina or the game itself, which makes it a setup. When a new game version comes out, or a mod needs another one, run `lip setup` again with the version you want. Look at the plan first:

```sh
lip setup --game 1.26.60 --dry-run
lip setup --game 1.26.60
```

Stop the server before you do this.

lip then works out the mods again for the new game and loader:

- A mod keeps its version when that version works with them. Otherwise it changes to a version that does.
- When a mod has no version that works, lip names such mods and asks whether to uninstall them. Answer `yes` to go on, or anything else to stop with nothing changed. When lip cannot ask, as in a script, the command fails and names them; uninstall them yourself with `lip uninstall`, then set up again.
- Running `lip setup` with the game and the loader the workspace already has changes nothing, and does not ask about the EULA.

### What is kept and what is deleted

Changing the game removes the files of the old game and writes those of the new one. Your own files are treated like this:

| Files | What happens |
| --- | --- |
| `server.properties`, `allowlist.json`, `permissions.json` | **Kept.** The new game does not overwrite them. |
| `worlds/` and everything else that neither lip nor the list below names | **Kept.** lip does not touch files it did not install. |
| The folders of your mods, `mods/<name>/`, with their configuration | **Kept**, unless a mod is uninstalled or changes version. |
| `behavior_packs/`, `resource_packs/`, `definitions/`, `config/`, `world_templates/`, `development_behavior_packs/`, `development_resource_packs/`, `development_skin_packs/` | **Deleted with everything in them**, then written again by the new game. |
| `valid_known_packs.json`, `packet-statistics.txt`, and `bedrock_server.exe.bak` of the Windows server | **Deleted.** |

::: warning
Packs that you copied into `behavior_packs/` or `resource_packs/` at the root of the server are deleted with those folders. Keep a copy elsewhere, or keep your packs inside the world folder, below `worlds/`.
:::

The same rules apply when you uninstall the game. Make a backup of the folder before a big change: lip undoes its own changes when a setup fails, but it cannot bring back what a successful setup deleted.

## Change packages while the server runs

The commands do not refuse to work while the server runs. You can run `lip install`, `lip update`, `lip uninstall` and `lip setup` in the same folder from another terminal. The server sees the changes the next time it starts.

On Windows, a running server keeps some of its files open. When lip has to replace or delete such a file, the command fails, lip undoes what it did, and the workspace is as it was. Stop the server and run the command again. See [A file is in use](troubleshooting.md#a-file-is-in-use).

One more thing can happen with `restart` set to `on-failure`. If the server crashes at the very moment a command changes the folder, the restart finds the folder busy, and `lip run` ends with the error of [Another lip is changing the workspace](troubleshooting.md#another-lip-is-changing-the-workspace). Start the server again.

The interface is stricter: while the server of a workspace runs, it changes no packages there. See [The interface and the commands](../concepts/how_lip_works.md#the-interface-and-the-commands).

## Next

- [Managing packages](managing_packages.md): install, update, list and uninstall.
- [Configuration](configuration.md): the settings of lip, and what to do when GitHub is slow or blocked.
- [Troubleshooting](troubleshooting.md): when a command fails.
- The reference pages of [`lip setup`](../reference/commands/setup.md) and [`lip run`](../reference/commands/run.md).
