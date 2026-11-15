# lip daemon

Show or stop the daemon of lip.

Every `lip` command except `lip daemon` is carried out by the **daemon**: a `lip` process of your user that runs in the background. The first command starts it, and later commands reuse it; it exits by itself after it has had nothing to do for a while. You never need to start or stop it: a command behaves exactly as if `lip` carried it out itself, in your current directory, with your environment variables, reading your input and printing to your terminal, and Ctrl+C works as usual. See [Internal Mechanism](../../guides/internal_mechanism.md#daemon).

## lip daemon status

```shell
lip daemon status [--json]
```

Shows whether the daemon runs, and if so its version and process ID. It never starts the daemon. With `--json`, prints `{"running": false}`, or `{"running": true, "version": "<version>", "pid": <process ID>}`.

## lip daemon stop

```shell
lip daemon stop
```

Asks the daemon to exit and waits until it has. If the daemon is carrying out a command, such as a `lip run` whose server is running, or runs a managed server (see [`lip run --managed`](run.md#managed-servers)), it keeps running and `lip daemon stop` fails; stop the managed servers, then run it again once the commands have ended. If no daemon runs, it does nothing.

The daemon does not exit by itself while it runs a managed server, and a newer `lip` keeps using it for `lip run --managed` until its managed servers have stopped.
