# lip rpc

Serve the JSON-RPC interface over stdio.

## Synopsis

```shell
lip rpc
```

## Description

Reads JSON-RPC 2.0 messages from standard input and writes responses to standard output, with each message framed by a `Content-Length` header as in the Language Server Protocol. It serves one client and exits with code 0 when standard input is closed. Every method operates on the workspace that is the current working directory.

This command is intended for IDE plugins, GUIs, and scripts that need programmatic access to lip and spawn `lip rpc` as a subprocess. See the [JSON-RPC Overview](../../rpc/overview.md) for the protocol and the [JSON-RPC Specification](../../rpc/json_rpc_spec.md) for the methods.

This command takes no options.
