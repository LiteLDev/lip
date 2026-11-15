# lip-daemon(1)

## NAME

lip-daemon - the process of the user that carries out the commands of lip

## SYNOPSIS

```
lip daemon status [--json] [-h | --help]
lip daemon stop [-h | --help]
```

## DESCRIPTION

### Terms

- **daemon**: A `lip` process started with the single argument `--daemon` ([lip(1), OPTIONS](lip.md#options)). It carries out the commands of other `lip` processes of the same user.
- **client**: A `lip` process started with a command ([lip(1), "Terms"](lip.md#terms)) other than a command of the command group `daemon`.
- **endpoint**: Where the daemon of a user is reached:
  - on Windows, the named pipe `\\.\pipe\lip-<SID>`, where `<SID>` is the security identifier of the user in its string form, such as `S-1-5-21-1004336348-1177238915-682003330-1001`;
  - elsewhere, the Unix domain socket `daemon.sock` in the **daemon directory**: `$XDG_RUNTIME_DIR/lip` when `XDG_RUNTIME_DIR` is set to an absolute path, otherwise the state directory ([lip-userspace(7)](lip-userspace.md)).
- **version of a daemon**: The version that `lip -v` writes for the executable file the daemon runs ([lip(1), OPTIONS](lip.md#options)).

The variables named here are those of the process that reads them.

### Endpoint

A user has at most one daemon per endpoint; a daemon that cannot create the endpoint because another daemon has it exits.

The endpoint is accessible only to its user: the named pipe grants access to that user alone; the daemon directory has the mode `0700` and the socket the mode `0600`. A client connects only to a named pipe whose owner is its user.

The daemon directory holds `daemon.sock` and files whose names and content are not defined here. A process other than `lip` must not read meaning into them.

### Carrying out a command

A client does, in order:

1. Connect to the endpoint. When nothing listens on it, start a daemon, with the arguments, the environment variables and the user of the client, in a new session without a terminal and without the standard streams of the client, and connect once it listens.
2. When the daemon has another version or runs another executable file than the client, ask it to exit. A daemon that carries out no command exits, and the client goes back to step 1; a daemon that carries out a command keeps running, and the client goes on at "Fallback".
3. Have the daemon carry out the command.

The daemon carries out the command as lip(1) and the page of the command define, with the arguments, the working directory, the environment variables and the process ID of the client, as `lip` itself would in the client process. Everything those pages define of `lip` holds as observed from the client:

- What `lip` reads from its standard input, the daemon reads from the standard input of the client, only when it needs the bytes, and in their order.
- What `lip` writes to its standard output and to its standard error, the client writes there, in the order the daemon writes it. The output of the programs that the command runs ([lip-workspace(7), "Running programs"](lip-workspace.md#running-programs)) is written so too, the standard output of a program to standard output and its standard error to standard error.
- Whether a standard stream of `lip` is a terminal, and the size of the terminal, are those of the client.
- The signals and the console control signals that the client receives are received by `lip`: they interrupt it as lip(1), "Workspace operations" defines, and ask it to stop as lip-run(1), "Stopping" defines. When they would end `lip` at once, the client ends at once, as that signal ends a process, and the command is **abandoned**.
- The client exits with the exit code of the command.

When the client ends before the command does, `lip` is asked to stop ([lip-run(1), "Stopping"](lip-run.md#stopping)) while the server of `lip run` runs; otherwise the command is abandoned.

Commands that a daemon carries out for different clients are, to each other, carried out by different processes: for example, each finds the workspace lock that the other takes held by another process ([lip-workspace(7), "Workspace lock"](lip-workspace.md#workspace-lock)).

An abandoned command writes nothing more to the client, and ends as soon as it can: a workspace operation that changes the workspace is cancelled and undone ([lip-workspace(7), "Atomicity"](lip-workspace.md#atomicity), rule 2); any other command changes nothing more.

The programs that a daemon runs are not in the session of the client: a signal or a console control signal reaches them only as "Stopping" of [lip-run(1)](lip-run.md) defines. Their standard output and standard error are pipes, not terminals, whatever those of the client are; the bytes they write reach the client as above.

A daemon that exits refuses commands from the moment it stops listening; a client whose command it refuses goes back to step 1.

### Fallback

When a client cannot start a daemon or connect to the endpoint, or step 2 of "Carrying out a command" goes on here, the client writes a message and carries out the command itself, as lip(1) and the page of the command define.

### Exiting

A daemon exits:

- after it has had no connection for a time that is not defined here;
- when a client asks it to (step 2 of "Carrying out a command"), or `lip daemon stop` does, and it carries out no command; and
- once it carries out no command, after it has carried out `lip self-update` that updated `lip` or started an installer ([lip-self-update(1)](lip-self-update.md)).

Before it exits, it stops listening on the endpoint and, elsewhere than on Windows, deletes `daemon.sock`.

### Commands

The commands of the command group `daemon` are carried out by the process that they start, never by a daemon, and never start one.

- `lip daemon status`: The result is whether a daemon listens on the endpoint and, when one does, its version and its process ID.
- `lip daemon stop`: When a daemon listens on the endpoint, asks it to exit and waits until it has. When it carries out a command, the command fails, and the daemon keeps running. When no daemon listens, the command succeeds and changes nothing.

## OPTIONS

- `--json`: The result is written as JSON ([lip(1), "Output"](lip.md#output)): `{"running": false}` when no daemon listens; otherwise

  ```json
  { "running": true, "version": "<version>", "pid": <process ID> }
  ```

  where `<version>` is the version of the daemon, as a JSON string, and `<process ID>` is a JSON number.

## FILES

- The endpoint, and the daemon directory elsewhere than on Windows.

## SEE ALSO

[lip(1)](lip.md), [lip-run(1)](lip-run.md), [lip-self-update(1)](lip-self-update.md), [lip-userspace(7)](lip-userspace.md)
