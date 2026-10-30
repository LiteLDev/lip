# lipd Overview

`lipd` uses standard input/output (stdio) for communication, making it easy to integrate with any language or tool that can spawn a subprocess and redirect streams.

## Quickstart

To start the daemon, simply run:

```bash
lipd run
```

This will start the JSON-RPC server, listening on `stdin` and writing responses to `stdout`. It serves until `stdin` is closed. Every method operates on the workspace that is the current working directory of `lipd`.

## Connecting

### Protocol Overview

-   **Protocol**: [JSON-RPC 2.0](https://www.jsonrpc.org/specification)
-   **Transport**: Standard Input/Output (stdio)
-   **Framing**: Each message is a UTF-8 JSON body preceded by a `Content-Length: <bytes>` header and a blank line (`\r\n\r\n`), as in the Language Server Protocol

::: tip
It is recommended to use a JSON-RPC library for your programming language to handle message framing and parsing reliably.
:::

## Client Contract

While a method runs, `lipd` sends your client the messages that `lip` would print, such as progress updates or log messages, as JSON-RPC **requests** (not notifications). Your client must answer each with the result `null`: `lipd` does not continue the method that sent a `Print*` request until the answer arrives. It does not wait for the answer to `ReportProgress`.

These requests are:

-   `PrintInfo`
-   `PrintSuccess`
-   `PrintWarning`
-   `PrintError`
-   `ReportProgress`

See the [JSON-RPC Specification](json_rpc_spec.md) for the definitions of these methods.
