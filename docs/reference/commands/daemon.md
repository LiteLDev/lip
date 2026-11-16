# lip daemon

Show or stop the daemon of lip.

## Synopsis

```
lip daemon status [--json] [-h | --help]
lip daemon stop [-h | --help]
```

## Description

The **daemon** is a `lip` process of your user that runs in the background and carries out the commands of every other `lip` process: all commands except those of the group `daemon`, and the interface. It is also what runs [managed servers](run.md#managed-servers).

You never start it yourself:

1. A command connects to the daemon. When none listens, the command starts one, with its own environment variables, detached from the terminal.
2. When the daemon is of another lip version, or of another `lip` executable, the command asks it to exit and starts a new one. A daemon that is busy keeps running; see [After an update](#after-an-update).
3. The daemon carries out the command exactly as the `lip` you started would: in your working directory, with your environment variables, reading your standard input, writing to your standard output and standard error, and receiving your Ctrl+C. `lip` exits with the exit code of the command.

When a command cannot start or reach the daemon, it writes a message and carries out the command itself. `lip run --managed` is the only command that then fails.

Two commands that the daemon carries out for two terminals behave as two processes do: for example, one finds the workspace lock held when the other holds it.

The daemon exits by itself:

- after it has had no connection and run no managed server for a while, 10 minutes in the current version;
- when a newer or other `lip` asks it to, and it is idle; and
- after a [`lip self-update`](self_update.md) that updated lip or started the installer, once it is idle.

The daemon is reached through an endpoint that only your user can use: a named pipe on Windows, a Unix socket elsewhere. See [The daemon endpoint](../files.md#the-daemon-endpoint).

### lip daemon status

Shows whether a daemon listens on the endpoint and, when one does, its version and its process ID.

### lip daemon stop

Asks the daemon to exit, and waits until it has.

- When no daemon listens, the command succeeds and changes nothing.
- When the daemon carries out a command or runs a managed server, **the command fails and the daemon keeps running**. Stop the managed servers first: attach with `lip run --managed` in each workspace and press Ctrl+C, or stop them on the Server page of the interface. Wait for other commands to end. Then run `lip daemon stop` again.

Both commands are carried out by the process you start, never by a daemon, and they never start one.

### After an update

A daemon of an older lip that still runs a managed server or a command is not replaced. Until it is idle:

- `lip run --managed` uses the old daemon, so you can still attach to and stop its servers;
- every other command writes a message and is carried out by the new `lip` itself.

Stop the managed servers, and the next command starts a daemon of the new version.

## Options

| Option | Description |
| --- | --- |
| `--json` | Of `status`: write the result as JSON. |
| `-h`, `--help` | Write the help and exit. |

## Output

`lip daemon status` writes its result to standard output. Without `--json` it is one line for people:

```
The daemon of lip does not run.
```

```
The daemon of lip 1.0.0 runs as process 4812, carrying out 0 commands.
```

With `--json`, when no daemon listens:

```json
{"running":false}
```

and when one does:

```json
{"running":true,"version":"1.0.0","pid":4812}
```

| Member | Value |
| --- | --- |
| `running` | A boolean: whether a daemon listens on the endpoint. |
| `version` | A string: the version of the daemon, what its `lip -v` writes. Only when `running` is `true`. |
| `pid` | A number: the process ID of the daemon. Only when `running` is `true`. |

`lip daemon stop` has no result; it writes messages to standard error.

## Exit status

| Command | `0` | not `0` |
| --- | --- | --- |
| `lip daemon status` | Always, whether a daemon runs or not. | A usage error. |
| `lip daemon stop` | The daemon has exited, or none ran. | A usage error, or the daemon is busy and keeps running. |

## Examples

See whether the daemon runs:

```shell
lip daemon status
```

Stop the daemon in a script, only when one runs:

```shell
lip daemon status --json | jq -e '.running' > /dev/null && lip daemon stop
```

## See also

- [`lip run`](run.md#managed-servers): managed servers.
- [How lip works](../../concepts/how_lip_works.md): the daemon and its clients.
- [Settings](../../interface/settings.md): the daemon in the interface.
- [A server from the command line](https://levimc.org/tutorials/server/command-line/)
