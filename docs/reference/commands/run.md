# lip run

Run the server of the workspace.

## Synopsis

```
lip run [--managed [--detach]] [-h | --help]
```

## Description

`lip run` starts the server at the root of the workspace, without arguments, with the workspace as its working directory. There are two ways to run it:

| | `lip run` | `lip run --managed` |
| --- | --- | --- |
| The server runs | For this command: in the **foreground**. | In the [daemon](daemon.md), as a **managed server**. |
| When the terminal is closed | lip stops the server. | The server keeps running. |
| The command ends | When the server has exited. | When you detach, or when the server has exited. With `--detach`, at once. |
| Ctrl+C | Stops the server. | Stops the server. |

A workspace has at most one running server, whichever way it was started.

### The server

The server is a file at the workspace root, chosen by the game and the loader that [`lip setup`](setup.md) installed:

| The workspace has | Server | Run |
| --- | --- | --- |
| A loader (LeviLamina) | `bedrock_server_mod.exe` | By lip on Windows; through Wine on Linux and macOS. |
| No loader, and the build `windows` | `bedrock_server.exe` | The same. |
| No loader, and the build `linux` | `bedrock_server` | By lip itself, only on Linux on x64. |

For the Linux server, lip sets the environment variable `LD_LIBRARY_PATH` to `.` when it is unset or empty, so that the server finds the libraries beside it.

Through Wine, lip runs the `wine` that it installed in its [data directory](../files.md#data-directory), never another one, with the absolute path of the server file as its only argument, and sets:

| Variable | Value |
| --- | --- |
| `WINEPREFIX` | When it is unset or empty: the absolute path of `.lip/wine` in the workspace. Wine creates the prefix when it is missing. |
| `WINEDEBUG` | When it is unset: `-all`, which silences the debug output of Wine. |

See [Linux and macOS](../../guides/linux_macos.md).

### Starting

Without `--managed`, `lip run` does, in order:

1. It takes the **server lock**, `.lip/server.lock`. When another process holds it, a server of the workspace runs already, and the command fails and says so.
2. It takes the **workspace lock**. When another process holds it, another command is changing the workspace, and the command fails.
3. When an operation was interrupted, it completes or undoes it. When that fails, the command fails.
4. It releases the workspace lock.
5. It fails when the workspace has no game (the message names `lip setup`), when the server is `bedrock_server` and lip does not run on Linux on x64, or when the server file does not exist.
6. For the Windows servers, it installs what the server needs of the system when that is missing: Wine on Linux and macOS, and the Microsoft Visual C++ Redistributable. When that fails, the command fails.
7. It runs the server and waits until it exits.
8. When the server is [restarted](#restarting), it goes back to step 2.
9. It releases the server lock.

The workspace lock is not held while the server runs, so `lip install`, `lip uninstall` and `lip update` work in the same workspace from another terminal. Their changes take effect when the server starts again. The interface does not carry them out while the server runs; it asks you to stop the server first.

### Input and output

The server writes to the standard output and standard error of `lip`, and reads what `lip` reads from its standard input: type server commands as usual. The input of the server does not end when that of `lip` does. A restarted server reads what comes after the server before it exited.

### Stopping

lip is **asked to stop** by Ctrl+C, by the signals `SIGINT`, `SIGTERM` and `SIGHUP`, and on Windows by any console control signal, such as closing the console window.

The first time lip is asked to stop, it sends the line `stop` to the server, which saves the world and exits. When lip is asked before the server has started, the server gets the line once it has. lip never exits before the server does, however often it is asked. When the server hangs, end the server process yourself.

### Restarting

The server is restarted when all of these hold:

- it exited with an exit code other than 0;
- lip was not asked to stop; and
- [`restart`](../configuration.md#restart) of the workspace configuration is `on-failure` at the moment the server exits.

lip then writes a message, waits 5 seconds, and starts the server again from step 2. Asking lip to stop while it waits cancels the restart.

The server is restarted at most 5 times within 10 minutes. When it exits again after that, lip writes that it does not restart it any more, and exits.

### Managed servers

A **managed server** is a server that the [daemon](daemon.md) runs for no particular client. It runs as above, with the working directory and the environment variables of the `lip` that started it, with these differences:

- It keeps running whatever happens to the client that started it.
- Its input is what the clients attached to it, and the interface, send it. Its output goes to all of them.
- Only an attached client or the interface can ask it to stop.

The daemon runs at most one managed server per workspace. A server started on the [Server](../../interface/server.md) page of the interface is a managed server too, and you can attach to it from a terminal.

With `--managed`, `lip run` does, in order:

1. When lip carries out the command itself, without the daemon, the command fails: only the daemon runs managed servers.
2. When no managed server of the workspace runs, it starts one and waits until its server has started. When that fails, the command fails in the same way.
3. With `--detach`, the command ends. Otherwise the client **attaches**.

### Attaching

While you are attached to a managed server:

- What you type goes to the server. The end of your standard input does not detach you.
- What the server writes, and the messages of lip about it, go to your terminal. When the server was already running, you may first see some of what it wrote before.
- Ctrl+C (`SIGINT`; on Windows also Ctrl+Break) asks the server to stop. You stay attached until it has exited.
- Closing the terminal (`SIGHUP`), `SIGTERM`, the other console control signals on Windows, and ending the `lip` process **detach** you: the command ends, and the server keeps running.

When the server stops running while you are attached, the command ends.

To leave a managed server running, close the terminal or the terminal tab. Run `lip run --managed` in the workspace again to attach again.

## Options

| Option | Description |
| --- | --- |
| `--managed` | Run the server as a managed server, or attach to the one that runs. |
| `--detach` | With `--managed`: do not attach. Without `--managed`, it is a usage error. |
| `-h`, `--help` | Write the help and exit. |

## Output

`lip run` has no result of its own. Standard output and standard error carry what the server writes there. The messages of lip, such as the one before a restart, go to standard error.

## Exit status

Without `--managed`:

| Case | Exit code |
| --- | --- |
| The server was started | The exit code of the server that ran last; of `wine` when it ran through Wine. On Linux and macOS, 128 + n when signal n ended it, such as 137 for `SIGKILL`. lip writes no message of its own. |
| The server was not started | Not 0, with a message that says why. |

With `--managed`:

| Case | Exit code |
| --- | --- |
| `--detach`, or you detached | `0` |
| The server stopped running while you were attached | What the `lip run` of the managed server exits with, as in the table above. |
| The managed server could not be started | Not 0, with a message that says why. |

## Examples

Run the server in the foreground. Press Ctrl+C to stop it:

```shell
lip run
```

Start the server in the background and return at once:

```shell
lip run --managed --detach
```

Attach to it later, to see its output and type commands:

```shell
lip run --managed
```

Restart the server automatically when it crashes:

```shell
lip config set restart on-failure --workspace
lip run --managed --detach
```

## See also

- [Running a server from commands](../../guides/server_from_commands.md)
- [Server](../../interface/server.md): starting, stopping and watching the server in the interface.
- [`lip setup`](setup.md), [`lip daemon`](daemon.md), [`lip config`](config.md)
- [Linux and macOS](../../guides/linux_macos.md)
