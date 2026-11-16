# Linux and macOS

After this page you know which servers lip runs on your system, what lip downloads to run them and where it puts it, and which limits to expect. The commands are the same on every system: see [A server from the command line](https://levimc.org/tutorials/server/command-line/).

## What works where

Minecraft publishes two servers: one for Windows and one for Linux. LeviLamina, the loader that mods need, loads only into the Windows server. So a server with mods is always the Windows server, and lip runs it through [Wine](https://www.winehq.org/) where the system is not Windows.

| System | Windows server, with or without LeviLamina | Linux server, without a loader |
| --- | --- | --- |
| Windows on x64 | Yes | No |
| Windows on arm64 | Yes. Windows emulates x64. | No |
| Linux on x64 | Yes, through the Wine of lip | Yes |
| macOS on x64 | Yes, through the Wine of lip | No |
| macOS on arm64 (Apple silicon) | Yes, through the Wine of lip and Rosetta 2 | No |
| Linux on arm64, and any other system | No | No |

lip is released for the first five. There is no lip for Linux on arm64: see [Limits and rough edges](#limits-and-rough-edges).

Whatever system lip runs on, it installs the same packages, those made for Windows on x64. A server folder set up on one system can be moved to another.

## The Windows server through Wine

Set up and run the server exactly as on Windows:

```sh
mkdir -p ~/servers/survival
cd ~/servers/survival
lip setup --game 1.26.50
lip run
```

You do not install Wine. lip brings its own and never runs another one, such as a `wine` on your `PATH`.

### What lip downloads, and where

The first time lip needs to run a Windows program, it prepares your system. That is when `lip setup` sets up a Windows server, when a package runs a program of its own while it is installed, and before `lip run` starts a Windows server.

| What | Where it goes | Shared |
| --- | --- | --- |
| **Wine**: a build for your system, from [Kron4ek/Wine-Builds](https://github.com/Kron4ek/Wine-Builds) on Linux and from [Gcenx/macOS_Wine_builds](https://github.com/Gcenx/macOS_Wine_builds) on macOS | `wine/` in the data directory of lip: `$XDG_DATA_HOME/lip`, by default `~/.local/share/lip`, on Linux and on macOS | By every workspace. Downloaded once. |
| **The Microsoft Visual C++ Redistributable**, which the server needs | Installed into the Wine prefix of the workspace | No. Once per workspace. |

lip tells you when it installs Wine:

```text
lip runs the server with Wine <version>, which it installs once for every workspace, into <directory>.
```

Both downloads are checked. Each version of lip knows the exact files it expects, by their SHA-256. A download that differs is deleted and not used, and the command fails before it changes the workspace. The downloaded archive and installer are deleted once they are used.

Neither step needs root.

To free the space, delete the data directory while no lip runs. Nothing is lost: lip installs Wine again when it next needs it.

### The Wine prefix

Wine keeps its "Windows", a registry and a `C:` drive, in a folder called the prefix. lip gives each workspace its own: `.lip/wine` in the server folder. Wine creates it the first time it runs there, which makes that first run slower.

The prefix moves with the server folder. Deleting `.lip/wine` while the server is stopped is safe: Wine makes it again, and lip installs the Visual C++ Redistributable into it again.

To use another prefix, set `WINEPREFIX` yourself, for every lip command of that workspace:

```sh
export WINEPREFIX="$HOME/.wine-bedrock"
lip setup --game 1.26.50
lip run
```

### macOS on Apple silicon

The Wine of lip is a program for x64, which a Mac with Apple silicon runs through Rosetta 2. Installing Rosetta 2 needs an administrator, so lip does not do it. When it is missing, lip fails and tells you to run:

```sh
softwareupdate --install-rosetta --agree-to-license
```

Then run the lip command again.

## The Linux server

On Linux on x64, lip can also run the Linux server of Minecraft. It needs no Wine and nothing else of your system. It has no loader, so mods that need LeviLamina cannot be installed next to it.

```sh
lip setup --game 1.26.50 --build linux
lip run
```

- `lip setup` places the files of the server and makes `bedrock_server` executable.
- `lip run` starts `bedrock_server` in the workspace. It sets `LD_LIBRARY_PATH` to `.` when that variable is unset or empty, so that the server finds the libraries beside it.
- On any system other than Linux on x64, `lip run` fails for a workspace set up this way, and tells you to run it there or to set up the Windows server.

A workspace holds one game. Setting up the Linux server in a folder that has the Windows server replaces it, and the other way round. See [`lip setup`](../reference/commands/setup.md).

## Environment variables

lip reads these variables from the terminal you run it in.

| Variable | What lip does with it |
| --- | --- |
| `WINEPREFIX` | The Wine prefix. When unset or empty, lip uses `.lip/wine` in the workspace. |
| `WINEDEBUG` | Passed to Wine. When unset, lip sets it to `-all`, which hides the debug messages of Wine. Set it, for example to `err+all`, to see them. |
| `LD_LIBRARY_PATH` | For the Linux server. When unset or empty, lip sets it to `.`. |
| `XDG_CONFIG_HOME` | Where the user settings are: `$XDG_CONFIG_HOME/lip`. Default `~/.config`. |
| `XDG_CACHE_HOME` | Where downloads are cached: `$XDG_CACHE_HOME/lip`. Default `~/.cache`. |
| `XDG_DATA_HOME` | Where Wine is installed: `$XDG_DATA_HOME/lip`. Default `~/.local/share`. |
| `XDG_STATE_HOME` | Where the list of workspaces of the interface is: `$XDG_STATE_HOME/lip`. Default `~/.local/state`. |
| `XDG_RUNTIME_DIR` | Where the socket of the daemon is: `$XDG_RUNTIME_DIR/lip/daemon.sock`. When unset, the socket is in the state directory. |
| `HTTPS_PROXY`, `HTTP_PROXY`, `ALL_PROXY`, `NO_PROXY` | The proxy of your network. See [Your system's proxy](configuration.md#your-system-s-proxy). |
| `LC_ALL`, `LC_MESSAGES`, `LANG` | The language of the interface, unless the setting `language` gives one. |

An `XDG_` variable that is unset, empty or not an absolute path takes its default. macOS uses the same directories as Linux.

Every other variable is passed on unchanged to the server and to the programs of packages.

::: info
lip does its work in a background process, the daemon. That does not change which variables count: each command uses the variables of the terminal it was typed in. A [managed server](../reference/commands/run.md#managed-servers) keeps those of the terminal that started it.
:::

## Limits and rough edges

- **No Linux on arm64.** Nothing that was tried runs a LeviLamina server there reliably: [Hangover](https://github.com/AndreRH/hangover) runs the plain server but not the one LeviLamina has changed; Wine for x64 under [box64](https://github.com/ptitSeb/box64) loses the exit codes of Windows programs; Wine for x64 under [FEX](https://fex-emu.com/) started a LeviLamina server in only some attempts.
- **No Wine for other systems.** On a system without a Wine of lip, anything that needs a Windows program fails with `lip runs the server with Wine on systems other than Windows, but has no Wine for <system>. Run the server on Windows, on Linux on x64 or on macOS.`
- **The first start is slow.** Wine is downloaded and unpacked, the prefix is created, and the Visual C++ Redistributable is installed. Later starts skip all three.
- **File names ignore case.** In a workspace of the Windows server, lip treats `Plugins/a.dll` and `plugins/a.dll` as the same file, as the programs under Wine do. If the folder holds two names that differ only in case where lip needs one, the command fails. Rename one of them.
- **Quotes in the commands of packages.** Wine passes a `"` to the Windows command line as `\"`. A package whose install commands contain `"` may not work through Wine. Report it to the author of the package.
- **Exit codes are those of Wine.** `lip run` exits with the code that `wine` exits with, or 128 plus the number of the signal that ended it.
- **A proxy that changes downloads breaks the check.** When the SHA-256 of Wine or of the Visual C++ Redistributable is not the expected one, lip refuses the file. Try again later or on another network.
- **A broken prefix.** When the Visual C++ Redistributable fails to install in a prefix, lip names the prefix. Delete that folder and run lip again. When `wine` itself cannot be run, delete `wine/` in the data directory so that lip installs it again.
- **Folders of older versions of lip.** A workspace that an earlier lip made for the platform `linux-x64` still works when it holds nothing but the server: lip reads it as the Linux server. With other packages in it, lip stops and says that it installs packages for `win-x64` only. Set up a new folder.

The reasons behind these are in [Internals](../concepts/internals.md#system-dependencies).
