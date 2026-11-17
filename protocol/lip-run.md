# lip-run(1)

## NAME

lip-run - run the server of the workspace

## SYNOPSIS

```
lip run [--managed [--detach]] [-h | --help]
```

## DESCRIPTION

The **server** is a file at the workspace root, chosen by the game and the loader of the workspace ([lip-setup(1), "Terms"](lip-setup.md#terms)) as the state file records them, and run without arguments:

| The workspace has | Server | Run |
|---|---|---|
| a loader | `bedrock_server_mod.exe` | as a program ([lip-workspace(7), "Running programs"](lip-workspace.md#running-programs)), for the environment of [lip(1), "Environment"](lip.md#environment) |
| no loader, and a game of the build `windows` | `bedrock_server.exe` | the same |
| no loader, and a game of the build `linux` | `bedrock_server` | by `lip` itself, as a program of a platform that is not a Windows platform; only when `lip` runs on Linux on x64 |

When `lip` runs the Linux server, `bedrock_server`, it sets the environment variable `LD_LIBRARY_PATH` to `.` when that is unset or empty for `lip`, and the environment variable `SSL_CERT_FILE`, when that is unset or empty for `lip`, to the first of these files that exists, if one does: `/etc/ssl/certs/ca-certificates.crt`, `/etc/pki/tls/certs/ca-bundle.crt`, `/etc/ssl/ca-bundle.pem`, `/etc/pki/ca-trust/extracted/pem/tls-ca-bundle.pem`, `/etc/ssl/cert.pem`.

Without `--managed`, `lip run` does, in order:

1. Take the server lock ([lip-workspace(7), "Server lock"](lip-workspace.md#server-lock)). When another process holds it, the command fails, and its message says that a server of the workspace runs already.
2. Take the workspace lock ([lip-workspace(7), "Workspace lock"](lip-workspace.md#workspace-lock)). When another process holds it, the command fails.
3. When `.lip/journal` exists, recover ([journal(5), "Recovery"](journal.md#recovery)). When that fails, the command fails.
4. Release the workspace lock.
5. When the workspace has no game, the command fails, and its message should name `lip setup` ([lip-setup(1)](lip-setup.md)). When the server is `bedrock_server` and `lip` does not run on Linux on x64, the command fails. When the server is not a file, the command fails.
6. When the server is not `bedrock_server`, prepare the system ([lip-workspace(7), "System dependencies"](lip-workspace.md#system-dependencies)). When that fails, the command fails.
7. Run the server, and wait until it exits.
8. When the server is restarted ("Restarting"), go to step 2 once `lip` has waited.
9. Release the server lock.

The workspace lock is not held while the server runs. The server lock is held from step 1 until the command ends, also when it fails.

### Standard streams

The standard output and the standard error of the server are those of `lip`. The server reads from its standard input the bytes that `lip` reads from its own, in their order. The standard input of the server does not end when that of `lip` does. A server that is restarted ("Restarting") reads the bytes that `lip` reads after the server before it exited.

### Stopping

`lip` is **asked to stop** by the signal `SIGINT`, `SIGTERM` or `SIGHUP` ([signal(7)]), and on Windows by a console control signal ([HandlerRoutine]).

The first time `lip` is asked to stop while the server runs, the server reads next from its standard input the line `stop`, ended by LF (U+000A). When `lip` is asked to stop before the server started, the server reads that line once it has started. `lip` does not exit before the server does, however often it is asked to stop.

### Restarting

When the server exits with an exit code other than 0, `lip` has not been asked to stop, and `restart` of the workspace configuration ([config.toml(5), "Workspace configuration"](config.toml.md#workspace-configuration)), as it is when the server exits, is `on-failure`, the server is **restarted**: `lip` writes a message and waits 5 seconds (step 8). When `lip` is asked to stop while it waits, the server is not restarted.

The server is restarted at most 5 times within 10 minutes: when it exits after it was restarted 5 times within the 10 minutes before, `lip` writes a message that it does not restart it again, and does not.

### Managed servers

A **managed server** is a server that the daemon ([lip-daemon(1)](lip-daemon.md)) runs for no client. It runs as `lip run` without `--managed` defines, with the working directory and the environment variables of the client that started it, with these differences:

- Its standard input is the bytes that the clients attached to it ("Attaching") and the interfaces ([lip-tui(1), "Server"](lip-tui.md#server)) give it, those of each in their order. What it writes to its standard output and its standard error, and the messages of `lip` about it, go to them. Its standard input does not end before it exits.
- `lip` is asked to stop for it only by a client attached to it ("Attaching"), or by an interface.
- It keeps running whatever the client that started it, or any other, does.

The daemon runs at most one managed server per workspace; two workspaces are the same when the canonical forms of their paths are equal ([lip-tui(1), "Workspaces"](lip-tui.md#workspaces)). A managed server **runs** from when the daemon starts it until its `lip run` ends.

With `--managed`, `lip run` does, in order:

1. When the daemon does not carry out the command ([lip-daemon(1), "Fallback"](lip-daemon.md#fallback)), the command fails, and its message should say that a managed server needs the daemon.
2. When no managed server of the workspace runs, start one, and wait until it has started its server (step 7). When its `lip run` fails before that, the command fails with that failure.
3. With `--detach`, the command ends. Otherwise the client attaches to the managed server ("Attaching").

### Attaching

A client **attached** to a managed server is attached until it detaches or the managed server stops running. While it is attached:

- The managed server reads the bytes that `lip` reads from the standard input of the client, in their order. The standard input of the client ending does not detach it.
- What the managed server writes to its standard output and its standard error after the client attached, and the messages of `lip` about it, are written to the standard output and the standard error of the client. Before that, the client may get part of what the managed server wrote before it attached; how much is not defined here.
- `SIGINT`, and on Windows the console control signals `CTRL_C_EVENT` and `CTRL_BREAK_EVENT`, ask `lip` to stop for the managed server ("Stopping"); the client stays attached.
- `SIGHUP` and `SIGTERM`, and on Windows the other console control signals, **detach** the client: the command ends, and the managed server keeps running. A client that ends ([lip-daemon(1), "Carrying out a command"](lip-daemon.md#carrying-out-a-command)) detaches too.

When the managed server stops running while the client is attached, the command ends.

## OPTIONS

- `--managed`: Run the server of the workspace as a managed server, or attach to the one that runs ("Managed servers").
- `--detach`: With `--managed`, do not attach to the managed server. Without `--managed`, it is a usage error ([lip(1), "Terms"](lip.md#terms)).

## EXIT STATUS

Without `--managed`, when step 7 started the server, `lip run` exits with the exit code of the server that ran last, as [lip-workspace(7), "Running programs"](lip-workspace.md#running-programs) defines it, and need not write a message.

With `--managed`, `lip run` exits with 0 when it ends with `--detach` or because the client detaches. When the managed server stops running while the client is attached, `lip run` exits with the exit code that the `lip run` of the managed server exits with, and need not write a message when that started its server.

## FILES

- `.lip/server.lock` in the workspace: the server lock ([lip-workspace(7), "Server lock"](lip-workspace.md#server-lock)).

## SEE ALSO

[lip(1)](lip.md), [lip-daemon(1)](lip-daemon.md), [lip-install(1)](lip-install.md), [lip-setup(1)](lip-setup.md), [lip-tui(1)](lip-tui.md), [config.toml(5)](config.toml.md), [journal(5)](journal.md), [lip-workspace(7)](lip-workspace.md)

[signal(7)]: https://man7.org/linux/man-pages/man7/signal.7.html
[HandlerRoutine]: https://learn.microsoft.com/windows/console/handlerroutine
