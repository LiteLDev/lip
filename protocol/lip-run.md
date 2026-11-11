# lip-run(1)

## NAME

lip-run - run the server of the workspace

## SYNOPSIS

```
lip run [-h | --help]
```

## DESCRIPTION

The **server** is the file `bedrock_server_mod.exe` at the workspace root, run as a program ([lip-workspace(7), "Running programs"](lip-workspace.md#running-programs)) without arguments, for the environment of [lip(1), "Environment"](lip.md#environment).

`lip run` does, in order:

1. Take the workspace lock ([lip-workspace(7), "Workspace lock"](lip-workspace.md#workspace-lock)). When another process holds it, the command fails.
2. When `.lip/journal` exists, recover ([journal(5), "Recovery"](journal.md#recovery)). When that fails, the command fails.
3. Release the workspace lock.
4. When `bedrock_server_mod.exe` is not a file, the command fails.
5. Run the server, and wait until it exits.

The workspace lock is not held while the server runs.

### Standard streams

The standard output and the standard error of the server are those of `lip`. The server reads from its standard input the bytes that `lip` reads from its own, in their order. The standard input of the server does not end when that of `lip` does.

### Stopping

`lip` is **asked to stop** by the signal `SIGINT`, `SIGTERM` or `SIGHUP` ([signal(7)]), and on Windows by a console control signal ([HandlerRoutine]).

The first time `lip` is asked to stop while the server runs, the server reads next from its standard input the line `stop`, ended by LF (U+000A). `lip` does not exit before the server does, however often it is asked to stop.

## EXIT STATUS

When step 5 started the server, `lip run` exits with the exit code of the server, as [lip-workspace(7), "Running programs"](lip-workspace.md#running-programs) defines it, and need not write a message.

## SEE ALSO

[lip(1)](lip.md), [lip-install(1)](lip-install.md), [journal(5)](journal.md), [lip-workspace(7)](lip-workspace.md)

[signal(7)]: https://man7.org/linux/man-pages/man7/signal.7.html
[HandlerRoutine]: https://learn.microsoft.com/windows/console/handlerroutine
