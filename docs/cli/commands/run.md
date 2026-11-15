# lip run

Run the server of the workspace.

## Synopsis

```shell
lip run
```

## Description

Runs the server at the root of the workspace (the current directory), without arguments, with the workspace as its working directory, and waits until it exits. Set up the server first with [`lip setup`](setup.md), e.g. `lip setup --game 1.21.0`; see [Running a Server](../../guides/running_server.md).

The server is the file that the game and the loader of the workspace (see [`lip setup`](setup.md#the-game-and-the-loader)) call for:

| The workspace has | Server | Run |
| --- | --- | --- |
| LeviLamina | `bedrock_server_mod.exe` | by lip on Windows, through Wine elsewhere |
| No loader, and the Windows server | `bedrock_server.exe` | by lip on Windows, through Wine elsewhere |
| No loader, and the Linux server | `bedrock_server` | by lip, only on Linux on x64 |

lip runs the Linux server with the environment variable `LD_LIBRARY_PATH` set to `.` when it is unset or empty, so that the server finds the libraries beside it.

Before it starts the server, `lip run`:

1. fails at once if another lip command, such as `lip install`, is changing the workspace;
2. completes or undoes an operation that was interrupted, as `lip install` would (see [Internal Mechanism](../../guides/internal_mechanism.md#workspace-state));
3. fails if the workspace has no game (pointing to `lip setup`), if the server is the Linux server and lip does not run on Linux on x64, or if the server is not a file; and
4. for the Windows servers, `bedrock_server_mod.exe` and `bedrock_server.exe`, installs what the server needs of the system when it is missing: Wine on Linux and macOS, and the Microsoft Visual C++ Redistributable (see [System Dependencies](../../guides/internal_mechanism.md#system-dependencies)). The Linux server needs neither.

The workspace is not locked while the server runs: you can run `lip install`, `lip uninstall` and `lip update` in the same workspace from another terminal. Their changes take effect when the server is started again.

### Wine

When lip does not run on Windows, as the platform of the workspace [environment](../../concepts/lip_toml.md#environment) is `win-x64`, a Windows server is run through [Wine](https://www.winehq.org/): lip runs the `wine` of the Wine it installed in its data directory, never one found through `PATH`, with the absolute path of the server file as its only argument and these environment variables:

| Variable | Value |
| --- | --- |
| `WINEPREFIX` | When it is unset or empty: the absolute path of `.lip/wine` in the workspace. Wine creates the prefix when it is missing. |
| `WINEDEBUG` | When it is unset: `-all`, which silences the debug output of Wine. |

Otherwise, and for the Linux server, lip runs the server itself.

### Input and Output

The server writes to the terminal of lip, and reads what you type: every byte lip reads from its standard input is passed on to the server, so you can type server commands as usual.

### Stopping

Press Ctrl+C (or send lip `SIGINT`, `SIGTERM` or `SIGHUP`) to stop the server: the first time, lip types the line `stop` into the server, which saves the world and exits. lip exits only after the server has exited, however often you press Ctrl+C; if the server hangs, end its process.

## Exit Status

When the server was started, `lip run` exits with the exit code of the server (of `wine`, when it is run through Wine), or with 128 + n when signal n ends it on a system other than Windows (e.g. 137 for `SIGKILL`). Otherwise it exits with a non-zero code and prints why.

## Examples

```shell
lip setup --game 1.21.0
lip run
```
