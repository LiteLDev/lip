# lip run

Run the server of the workspace.

## Synopsis

```shell
lip run
```

## Description

Runs `bedrock_server_mod.exe` at the root of the workspace (the current directory), without arguments, with the workspace as its working directory, and waits until it exits. Install the server first, e.g. with `lip install github.com/LiteLDev/LeviLamina`; see [Running a Server](../../guides/running_server.md).

Before it starts the server, `lip run`:

1. fails at once if another lip command, such as `lip install`, is changing the workspace;
2. completes or undoes an operation that was interrupted, as `lip install` would (see [Internal Mechanism](../../guides/internal_mechanism.md#workspace-state)); and
3. fails if `bedrock_server_mod.exe` is not a file.

The workspace is not locked while the server runs: you can run `lip install`, `lip uninstall` and `lip update` in the same workspace from another terminal. Their changes take effect when the server is started again.

### Wine

When the platform of the workspace [environment](../../concepts/lip_toml.md#environment) starts with `win` (such as the default `win-x64`) and lip does not run on Windows, the server is run through [Wine](https://www.winehq.org/): lip runs `wine`, found through `PATH`, with the absolute path of `bedrock_server_mod.exe` as its only argument and these environment variables:

| Variable | Value |
| --- | --- |
| `WINEPREFIX` | When it is unset or empty: the absolute path of `.lip/wine` in the workspace. Wine creates the prefix when it is missing. |
| `WINEDEBUG` | When it is unset: `-all`, which silences the debug output of Wine. |

Otherwise, lip runs the server itself.

### Input and Output

The server writes to the terminal of lip, and reads what you type: every byte lip reads from its standard input is passed on to the server, so you can type server commands as usual.

### Stopping

Press Ctrl+C (or send lip `SIGINT`, `SIGTERM` or `SIGHUP`) to stop the server: the first time, lip types the line `stop` into the server, which saves the world and exits. lip exits only after the server has exited, however often you press Ctrl+C; if the server hangs, end its process.

## Exit Status

When the server was started, `lip run` exits with the exit code of the server (of `wine`, when it is run through Wine), or with 128 + n when signal n ends it on a system other than Windows (e.g. 137 for `SIGKILL`). Otherwise it exits with a non-zero code and prints why.

## Examples

```shell
lip install github.com/LiteLDev/LeviLamina
lip run
```
