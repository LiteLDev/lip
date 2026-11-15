# Running a Server

This guide sets up a LeviLamina server with lip and runs it with [`lip run`](../cli/commands/run.md), on Windows, and on Linux and macOS through [Wine](https://www.winehq.org/). Besides lip, there is nothing to download or install: lip installs what the server needs of the system itself.

## On Windows

Create an empty directory for the server, the workspace, and run lip in it:

```shell
mkdir my-server
cd my-server
lip install github.com/LiteLDev/LeviLamina
lip run
```

`lip install github.com/LiteLDev/LeviLamina` installs LeviLamina and the packages it depends on, including the Bedrock Dedicated Server, into the workspace, and sets up `bedrock_server_mod.exe`. `lip run` then runs `bedrock_server_mod.exe` in the workspace.

The server needs the Microsoft Visual C++ Redistributable (x64). When it is not installed, lip downloads its installer from Microsoft and runs it before it runs the first hook command or the server; Windows asks you to allow it to make changes, as an administrator. See [System Dependencies](internal_mechanism.md#system-dependencies).

## On Linux and macOS, through Wine

lip installs the packages of the platform `win-x64` by default, whatever system it runs on (see [Environment](../concepts/lip_toml.md#environment)). On Linux and macOS, it runs the hook commands of those packages and the server through Wine, so the same Windows server runs there.

### Installing and Running

The commands are the same as on Windows:

```shell
mkdir my-server
cd my-server
lip install github.com/LiteLDev/LeviLamina
lip run
```

The first time lip needs Wine, it downloads a build of Wine, checks it against the SHA-256 written into lip, and extracts it into its data directory (`~/.local/share/lip`, see [FAQ](../faq.md#where-does-lip-store-installed-packages)), where every workspace shares it. It then installs the Microsoft Visual C++ Redistributable into the Wine prefix of the workspace, once. Neither needs root, and neither needs a `wine` of your own: lip never runs a `wine` on `PATH`. See [System Dependencies](internal_mechanism.md#system-dependencies).

On a system other than Windows, when the platform of the workspace starts with `win`, lip runs every program through its Wine: the hook commands, as `wine cmd /s /c <command>`, while it installs, updates or uninstalls packages, and the server, as `wine` with the absolute path of `bedrock_server_mod.exe`, while `lip run` runs. Wine passes each `"` of a hook command to `cmd` as `\"`, which `cmd` does not read as an escape, so a hook command that contains `"` does not run as written through Wine; packages meant for Wine should avoid `"` in their hooks. The paths of such a workspace are case-insensitive to lip, as they are to the programs Wine runs: a package that writes `Plugins/a.dll` writes into an existing `plugins/` directory, not beside it.

### Wine Prefix

lip sets two environment variables for `wine`, unless you set them yourself:

| Variable | Default |
| --- | --- |
| `WINEPREFIX` | The absolute path of `.lip/wine` in the workspace, used when `WINEPREFIX` is unset or empty. Each workspace gets a prefix of its own, which Wine creates the first time it runs there and which moves with the workspace. |
| `WINEDEBUG` | `-all`, used when `WINEDEBUG` is unset: the debug messages of Wine are not shown. Set it, e.g. to `err+all`, to see them. |

To use another prefix, set `WINEPREFIX` for every lip command of the workspace:

```shell
export WINEPREFIX="$HOME/.wine-bds"
lip install github.com/LiteLDev/LeviLamina
lip run
```

### macOS and arm64

The server is a Windows program for x64. lip is tested with a real LeviLamina server on each system it is released for, set up as follows:

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

lip downloads the server itself, without a Windows program, so installing `github.com/LiteLDev/bds` needs no Wine.

### The Linux Server

The variant `linux` of `github.com/LiteLDev/bds` installs the Linux server of Minecraft instead of the Windows one:

```shell
lip install github.com/LiteLDev/bds#linux
```

lip places the files of the Linux server and makes `bedrock_server` executable itself, without a hook command, so nothing runs through Wine. A workspace holds only one variant of the server, so `github.com/LiteLDev/bds#linux` cannot be installed next to `github.com/LiteLDev/bds` or `github.com/LiteLDev/bds#windows`.

`lip run` still runs only `bedrock_server_mod.exe` at the workspace root, which the Linux server does not have, and LeviLamina loads only into the Windows server. Start the Linux server yourself, e.g. with `./bedrock_server`.

A workspace that an earlier lip created with `--platform linux-x64` keeps working when it holds only the server: lip reads its server as `github.com/LiteLDev/bds#linux` (see [State file](../concepts/lockfiles.md#workspaces-of-linux-x64)).

## While the Server Runs

- **Console**: The server writes to your terminal, and everything you type is passed on to it, so you can type server commands as usual.
- **Stopping**: Press Ctrl+C. lip types `stop` into the server, which saves the world and exits, and lip exits after it. Pressing Ctrl+C again does not cut the server off: lip still waits for it to exit. If the server hangs, end its process.
- **Exit code**: `lip run` exits with the exit code of the server (of `wine`, through Wine), or 128 + n when signal n ends it on Linux or macOS (e.g. 137 for `SIGKILL`), so a script or service manager that restarts the server on failure can use it.
- **Changing packages**: lip does not lock the workspace while the server runs. You can run `lip install`, `lip update` or `lip uninstall` in the same workspace from another terminal; the server sees the changes when you start it again. If a file cannot be replaced or deleted because the running server has it open, as can happen on Windows, the operation fails and is undone, leaving the workspace as it was; stop the server and run the command again.
- **Interrupted operations**: `lip run` first completes or undoes an operation that was interrupted, as any operation does, and fails at once if another lip command is changing the workspace at that moment.
