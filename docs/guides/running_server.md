# Running a Server

This guide sets up a LeviLamina server with lip and runs it with [`lip run`](../cli/commands/run.md), on Windows, and on Linux through [Wine](https://www.winehq.org/).

## On Windows

Create an empty directory for the server, the workspace, and run lip in it:

```shell
mkdir my-server
cd my-server
lip install github.com/LiteLDev/LeviLamina
lip run
```

`lip install github.com/LiteLDev/LeviLamina` installs LeviLamina and the packages it depends on, including the Bedrock Dedicated Server, into the workspace, and sets up `bedrock_server_mod.exe`. `lip run` then runs `bedrock_server_mod.exe` in the workspace.

The server may need runtime libraries of the system, such as the Microsoft Visual C++ Redistributable. lip does not install them: install them yourself if the server reports that they are missing.

## On Linux, through Wine

lip installs the packages of the platform `win-x64` by default, whatever system it runs on (see [Environment](../concepts/lip_toml.md#environment)). On Linux, it runs the hook commands of those packages and the server through Wine, so the same Windows server runs on Linux.

### Prerequisites

lip does not install Wine, or anything inside the Wine prefix. Before you install packages:

1. Install Wine from your distribution or from [WineHQ](https://wiki.winehq.org/Download), so that the `wine` command is on `PATH`:

   ```shell
   wine --version
   ```

2. Install, into the Wine prefix of the workspace, the Windows runtime libraries the server needs, such as the Microsoft Visual C++ Redistributable, e.g. with [winetricks](https://github.com/Winetricks/winetricks):

   ```shell
   mkdir my-server
   cd my-server
   WINEPREFIX="$PWD/.lip/wine" winetricks -q vcrun2022
   ```

   `.lip/wine` is the prefix lip uses by default; see [Wine Prefix](#wine-prefix).

### Installing and Running

The commands are the same as on Windows:

```shell
lip install github.com/LiteLDev/LeviLamina
lip run
```

On a system other than Windows, when the platform of the workspace starts with `win`, lip runs every program through `wine` found on `PATH`: the hook commands, as `wine cmd /s /c <command>`, while it installs, updates or uninstalls packages, and the server, as `wine` with the absolute path of `bedrock_server_mod.exe`, while `lip run` runs. Wine passes each `"` of a hook command to `cmd` as `\"`, which `cmd` does not read as an escape, so a hook command that contains `"` does not run as written through Wine; packages meant for Wine should avoid `"` in their hooks. The paths of such a workspace are case-insensitive to lip, as they are to the programs Wine runs: a package that writes `Plugins/a.dll` writes into an existing `plugins/` directory, not beside it.

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
| Linux on x64 | Wine. |
| macOS on x64 | Wine. |
| macOS on arm64 | Wine for x64, run by Rosetta 2 (`softwareupdate --install-rosetta`). |

On macOS on x64, Wine 11.0 does not create its prefix; Wine 11.21 does. Builds of Wine for macOS are at [Gcenx/macOS_Wine_builds](https://github.com/Gcenx/macOS_Wine_builds).

lip is not released for Linux on arm64, where nothing that was tried runs a LeviLamina server reliably:

- [Hangover](https://github.com/AndreRH/hangover) runs the Bedrock Dedicated Server, but not the one LeviLamina has modified, which fails as it starts.
- Wine for x64 run by [box64](https://github.com/ptitSeb/box64) fails to run `bdsdown`, which downloads the server while packages are installed.
- Wine for x64 run by [FEX](https://fex-emu.com/) ran a LeviLamina server twice, but `bdsdown` failed in nine of ten attempts.

### Linux-Native Packages

To install the packages of another platform instead, such as the Linux-native server of `github.com/LiteLDev/bds`, give the platform on the first command of a new workspace:

```shell
lip install github.com/LiteLDev/bds --platform linux-x64
```

The workspace then keeps `linux-x64`: lip installs only entries of `linux-x64`, runs hook commands with `sh -c`, and runs programs without Wine. A package without an entry for `linux-x64`, such as a Windows-only mod, cannot be installed there.

`lip run` still runs only `bedrock_server_mod.exe` at the workspace root, which no `linux-x64` package installs: LeviLamina has no entry for `linux-x64`, and the Linux-native server of `github.com/LiteLDev/bds` is `bedrock_server`. Start such a server yourself, e.g. with `./bedrock_server`.

## While the Server Runs

- **Console**: The server writes to your terminal, and everything you type is passed on to it, so you can type server commands as usual.
- **Stopping**: Press Ctrl+C. lip types `stop` into the server, which saves the world and exits, and lip exits after it. Pressing Ctrl+C again does not cut the server off: lip still waits for it to exit. If the server hangs, end its process.
- **Exit code**: `lip run` exits with the exit code of the server (of `wine`, through Wine), or 128 + n when signal n ends it on Linux or macOS (e.g. 137 for `SIGKILL`), so a script or service manager that restarts the server on failure can use it.
- **Changing packages**: lip does not lock the workspace while the server runs. You can run `lip install`, `lip update` or `lip uninstall` in the same workspace from another terminal; the server sees the changes when you start it again. If a file cannot be replaced or deleted because the running server has it open, as can happen on Windows, the operation fails and is undone, leaving the workspace as it was; stop the server and run the command again.
- **Interrupted operations**: `lip run` first completes or undoes an operation that was interrupted, as any operation does, and fails at once if another lip command is changing the workspace at that moment.
