# lip-run(1)

## NAME

lip-run - run the server of the workspace

## SYNOPSIS

```
lip run [-h | --help]
```

## DESCRIPTION

The **server** is a file at the workspace root, chosen by the game and the loader of the workspace ([lip-setup(1), "Terms"](lip-setup.md#terms)) as the state file records them, and run without arguments:

| The workspace has | Server | Run |
|---|---|---|
| a loader | `bedrock_server_mod.exe` | as a program ([lip-workspace(7), "Running programs"](lip-workspace.md#running-programs)), for the environment of [lip(1), "Environment"](lip.md#environment) |
| no loader, and a game of the build `windows` | `bedrock_server.exe` | the same |
| no loader, and a game of the build `linux` | `bedrock_server` | by `lip` itself, as a program of a platform that is not a Windows platform; only when `lip` runs on Linux on x64 |

When `lip` runs the Linux server, `bedrock_server`, it sets the environment variable `LD_LIBRARY_PATH` to `.` when that is unset or empty for `lip`.

`lip run` does, in order:

1. Take the workspace lock ([lip-workspace(7), "Workspace lock"](lip-workspace.md#workspace-lock)). When another process holds it, the command fails.
2. When `.lip/journal` exists, recover ([journal(5), "Recovery"](journal.md#recovery)). When that fails, the command fails.
3. Release the workspace lock.
4. When the workspace has no game, the command fails, and its message should name `lip setup` ([lip-setup(1)](lip-setup.md)). When the server is `bedrock_server` and `lip` does not run on Linux on x64, the command fails. When the server is not a file, the command fails.
5. When the server is not `bedrock_server`, prepare the system ([lip-workspace(7), "System dependencies"](lip-workspace.md#system-dependencies)). When that fails, the command fails.
6. Run the server, and wait until it exits.

The workspace lock is not held while the server runs.

### Standard streams

The standard output and the standard error of the server are those of `lip`. The server reads from its standard input the bytes that `lip` reads from its own, in their order. The standard input of the server does not end when that of `lip` does.

### Stopping

`lip` is **asked to stop** by the signal `SIGINT`, `SIGTERM` or `SIGHUP` ([signal(7)]), and on Windows by a console control signal ([HandlerRoutine]).

The first time `lip` is asked to stop while the server runs, the server reads next from its standard input the line `stop`, ended by LF (U+000A). `lip` does not exit before the server does, however often it is asked to stop.

## EXIT STATUS

When step 6 started the server, `lip run` exits with the exit code of the server, as [lip-workspace(7), "Running programs"](lip-workspace.md#running-programs) defines it, and need not write a message.

## SEE ALSO

[lip(1)](lip.md), [lip-install(1)](lip-install.md), [lip-setup(1)](lip-setup.md), [lip-tui(1)](lip-tui.md), [journal(5)](journal.md), [lip-workspace(7)](lip-workspace.md)

[signal(7)]: https://man7.org/linux/man-pages/man7/signal.7.html
[HandlerRoutine]: https://learn.microsoft.com/windows/console/handlerroutine
