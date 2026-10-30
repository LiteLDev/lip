# lipd run

Starts the lip daemon JSON-RPC server.

## Synopsis

```bash
lipd run
```

## Description

The `run` command starts the `lipd` process in server mode. It listens for JSON-RPC 2.0 messages on standard input (stdin) and writes responses to standard output (stdout). When stdin is closed, it exits with code 0.

This command is intended to be used by automated tools, IDE plugins, and scripts that need programmatic access to lip's package management capabilities.

## Options

This command takes no options.

## Examples

Start the daemon:

```bash
lipd run
```
