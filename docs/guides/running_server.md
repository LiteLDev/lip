# Running a Server

This guide sets up a LeviLamina server with lip and runs it with [`lip run`](../cli/commands/run.md), on Windows, and on Linux and macOS through [Wine](https://www.winehq.org/). Besides lip, there is nothing to download or install: lip installs what the server needs of the system itself.

## Setting Up the Server

Create an empty directory for the server, the workspace, and run lip in it:

```shell
mkdir my-server
cd my-server
lip setup --game 1.21.0
lip run
```

[`lip setup`](../cli/commands/setup.md) chooses the server of the workspace:

- **The game**: the Bedrock Dedicated Server, `github.com/LiteLDev/bds`, of the version `--game`.
- **The build**: `--build windows`, the default, for the Windows server, or `--build linux` for the Linux server (see [The Linux Server](#the-linux-server)).
- **The loader**: LeviLamina, by default for the Windows server, or no loader with `--no-loader`. `--loader levilamina@<version>` takes that version of LeviLamina; without a version, lip takes the newest LeviLamina that supports the game version.

`lip setup --game 1.21.0` installs the game 1.21.0 and the newest LeviLamina that supports it, with the packages LeviLamina depends on, into the workspace, and sets up `bedrock_server_mod.exe`. `lip run` then runs `bedrock_server_mod.exe` in the workspace.

You can also let LeviLamina choose the game: `lip install LeviLamina` is carried out as `lip setup`, with the newest LeviLamina and the newest game version it supports, and `lip install LeviLamina@<version>` with that version of LeviLamina:

```shell
lip install LeviLamina
```

Then install mods with [`lip install`](../cli/commands/install.md). In a workspace that has no game yet, installing a mod fails and asks you to run `lip setup` first.

### The Minecraft EULA

Before lip downloads the game, it shows the links to the [Minecraft End User License Agreement](https://www.minecraft.net/eula) and the [Microsoft Privacy Policy](https://go.microsoft.com/fwlink/?LinkId=521839), and asks whether you accept them. Type `yes` to go on; any other answer stops the command, and nothing changes.

lip does not remember the answer: it asks every time a setup installs or changes the game. In a script, a service or a container, where the standard input or the standard error of lip is not a terminal, lip cannot ask, and the setup fails unless you accept with `--accept-eula`:

```shell
lip setup --game 1.21.0 --accept-eula
```

In the [interactive interface](../cli/tui.md#preview-and-apply), lip asks in a dialog when a change sets up the game.

### Changing the Game Version

[`lip install`](../cli/commands/install.md) and [`lip update`](../cli/commands/update.md) never change the game or the loader. When a new version of the game comes out, or a mod needs another one, run `lip setup` again:

```shell
lip setup --game 1.21.50
```

lip resolves the mods of the workspace again for the new game and loader: a mod keeps its version when that version works with them, and changes version otherwise. Add `--dry-run` to see what would change first. When a mod has no version that works with the new game, lip lists such mods and asks whether to uninstall them; when lip cannot ask, the command fails, naming them, and you can uninstall them yourself with [`lip uninstall`](../cli/commands/uninstall.md) before you run `lip setup` again. Running `lip setup` with the game and the loader that the workspace already has changes nothing of them, and does not ask about the EULA.

### The Windows Server Without a Loader

To run the Windows server without LeviLamina, set it up without a loader:

```shell
lip setup --game 1.21.0 --no-loader
lip run
```

`lip run` then runs `bedrock_server.exe` instead of `bedrock_server_mod.exe`, in the same way: by lip on Windows, through Wine on Linux and macOS. Mods that need LeviLamina cannot be installed in such a workspace.

## On Windows

The Windows server needs the Microsoft Visual C++ Redistributable (x64). When it is not installed, lip downloads its installer from Microsoft and runs it while `lip setup` sets up the Windows server, and before it runs a hook command or `lip run` runs the server; Windows asks you to allow it to make changes, as an administrator. See [System Dependencies](internal_mechanism.md#system-dependencies).

## On Linux and macOS, through Wine

lip installs the packages of the platform `win-x64` by default, whatever system it runs on (see [Environment](../concepts/lip_toml.md#environment)). On Linux and macOS, it runs the hook commands of those packages and the Windows server through Wine, so the same Windows server runs there.

### Installing and Running

The commands are the same as on Windows:

```shell
mkdir my-server
cd my-server
lip setup --game 1.21.0
lip run
```

The first time lip needs Wine, which is when `lip setup` sets up the Windows server, it downloads a build of Wine, checks it against the SHA-256 written into lip, and extracts it into its data directory (`~/.local/share/lip`, see [FAQ](../faq.md#where-does-lip-store-installed-packages)), where every workspace shares it. It then installs the Microsoft Visual C++ Redistributable into the Wine prefix of the workspace, once. Neither needs root, and neither needs a `wine` of your own: lip never runs a `wine` on `PATH`. See [System Dependencies](internal_mechanism.md#system-dependencies).

On a system other than Windows, when the platform of the workspace starts with `win`, lip runs every Windows program through its Wine: the hook commands, as `wine cmd /s /c <command>`, while it installs, updates or uninstalls packages, and the Windows server, as `wine` with the absolute path of the server file (`bedrock_server_mod.exe`, or `bedrock_server.exe` without a loader), while `lip run` runs. Wine passes each `"` of a hook command to `cmd` as `\"`, which `cmd` does not read as an escape, so a hook command that contains `"` does not run as written through Wine; packages meant for Wine should avoid `"` in their hooks. The paths of such a workspace are case-insensitive to lip, as they are to the programs Wine runs: a package that writes `Plugins/a.dll` writes into an existing `plugins/` directory, not beside it.

### Wine Prefix

lip sets two environment variables for `wine`, unless you set them yourself:

| Variable | Default |
| --- | --- |
| `WINEPREFIX` | The absolute path of `.lip/wine` in the workspace, used when `WINEPREFIX` is unset or empty. Each workspace gets a prefix of its own, which Wine creates the first time it runs there and which moves with the workspace. |
| `WINEDEBUG` | `-all`, used when `WINEDEBUG` is unset: the debug messages of Wine are not shown. Set it, e.g. to `err+all`, to see them. |

To use another prefix, set `WINEPREFIX` for every lip command of the workspace:

```shell
export WINEPREFIX="$HOME/.wine-bds"
lip setup --game 1.21.0
lip run
```

### macOS and arm64

The Windows server is a Windows program for x64. lip is tested with a real LeviLamina server on each system it is released for, set up as follows:

| System | What runs the server |
| --- | --- |
| Windows on x64 | Windows. |
| Windows on arm64 | Windows, which emulates x64. |
| Linux on x64 | The Wine of lip. |
| macOS on x64 | The Wine of lip. |
| macOS on arm64 | The same Wine for x64, run by Rosetta 2. |

On Linux on x64, the Wine of lip is a build of [Kron4ek/Wine-Builds](https://github.com/Kron4ek/Wine-Builds); on macOS, a build of [Gcenx/macOS_Wine_builds](https://github.com/Gcenx/macOS_Wine_builds).

On a Mac with Apple silicon, Wine needs Rosetta 2, and installing it needs an administrator, so lip does not install it: when it is missing, lip fails and asks you to run:

```shell
softwareupdate --install-rosetta --agree-to-license
```

lip is not released for Linux on arm64, where nothing that was tried runs a LeviLamina server reliably:

- [Hangover](https://github.com/AndreRH/hangover) runs the Bedrock Dedicated Server, but not the one LeviLamina has modified, which fails as it starts.
- Wine for x64 run by [box64](https://github.com/ptitSeb/box64) kills Windows programs as they exit, so their exit codes are lost.
- Wine for x64 run by [FEX](https://fex-emu.com/) ran a LeviLamina server in two of six attempts. In two, `PeEditor.exe`, which modifies the server while LeviLamina is installed, was killed; in two, the server started but failed as it stopped.

lip downloads and places the files of the server itself, without a Windows program.

### The Linux Server

The build `linux` sets up the Linux server of Minecraft instead of the Windows one, the variant `github.com/LiteLDev/bds#linux`. LeviLamina loads only into the Windows server, so the Linux server has no loader, and mods that need LeviLamina cannot be installed next to it:

```shell
lip setup --game 1.21.0 --build linux
lip run
```

`lip install github.com/LiteLDev/bds#linux` does the same with the newest version of the game, and `lip install github.com/LiteLDev/bds#linux@<version>` with that version.

lip places the files of the Linux server and makes `bedrock_server` executable itself, without a hook command, so nothing runs through Wine, and lip installs neither Wine nor the Microsoft Visual C++ Redistributable for it. A workspace holds only one game, so setting up the Linux server in a workspace that has the Windows server replaces it, as any change of the game does.

On Linux on x64, `lip run` runs `bedrock_server` at the workspace root itself, with the environment variable `LD_LIBRARY_PATH` set to `.` when it is unset or empty, so that the server finds the libraries beside it. On other systems, `lip run` fails for the Linux server.

A workspace that an earlier lip created with `--platform linux-x64` keeps working when it holds only the server: lip reads its server as `github.com/LiteLDev/bds#linux` (see [State file](../concepts/lockfiles.md#workspaces-of-linux-x64)).

## While the Server Runs

- **Console**: The server writes to your terminal, and everything you type is passed on to it, so you can type server commands as usual.
- **Stopping**: Press Ctrl+C. lip types `stop` into the server, which saves the world and exits, and lip exits after it. Pressing Ctrl+C again does not cut the server off: lip still waits for it to exit. If the server hangs, end its process.
- **Exit code**: `lip run` exits with the exit code of the server (of `wine`, through Wine), or 128 + n when signal n ends it on Linux or macOS (e.g. 137 for `SIGKILL`), so a script or service manager that restarts the server on failure can use it.
- **Restarting**: To have lip restart a server that crashes (exits with a non-zero code), set `lip config set restart on-failure --workspace`. lip then waits 5 seconds and starts it again, at most 5 times within 10 minutes; a server you stopped, or that exits with code 0, is not restarted. See [`lip run`](../cli/commands/run.md#restarting).
- **One server per workspace**: A second `lip run` in a workspace whose server runs fails at once, whether the server was started by `lip run`, the interface or the daemon.
- **Keeping it running**: `lip run --managed` has the daemon of lip run the server, so that it keeps running when you close the terminal; `lip run --managed` again attaches to it, and `lip run --managed --detach` starts it without attaching. The [interface](../cli/tui.md#server) runs its servers this way. See [Managed Servers](../cli/commands/run.md#managed-servers).
- **Changing packages**: lip does not lock the workspace while the server runs. You can run `lip install`, `lip update`, `lip uninstall` or `lip setup` in the same workspace from another terminal; the server sees the changes when you start it again. If a file cannot be replaced or deleted because the running server has it open, as can happen on Windows, the operation fails and is undone, leaving the workspace as it was; stop the server and run the command again. The [interactive interface](../cli/tui.md#while-the-server-runs) is stricter: while the server of a workspace runs, however it was started, it installs, updates, uninstalls and sets up nothing there, and shows the game configuration read-only.
- **Interrupted operations**: `lip run` first completes or undoes an operation that was interrupted, as any operation does, and fails at once if another lip command is changing the workspace at that moment.
