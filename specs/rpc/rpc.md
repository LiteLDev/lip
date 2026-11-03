# lip rpc JSON-RPC Specification

This specification defines the externally observable behavior of `lip rpc` (CLI §5.12), which offers the [commands][CLI command] of `lip` over [JSON-RPC 2.0]. It uses the terms of the [CLI specification][CLI] and of the [package specification][PF], and cites their sections as "CLI §N" and "PF §N". A reference "§N" names section N of this specification.

## Conventions

The key words "MUST", "MUST NOT", "REQUIRED", "SHOULD", "SHOULD NOT", "RECOMMENDED", "MAY" and "OPTIONAL" are to be interpreted as described in BCP 14 ([RFC 2119], [RFC 8174]) when, and only when, they appear in all capitals.

## Terms

Each term below is defined using only the terms before it and the terms of the [CLI specification][CLI] and the [package specification][PF].

- <a id="t-client"></a>**client**: the program connected to `lip rpc`.
- <a id="t-method"></a>**method**: a [JSON-RPC 2.0] method that the [client] calls on `lip rpc` (§3).
- <a id="t-client-call"></a>**client call**: a [JSON-RPC 2.0] request that `lip rpc` sends to the [client] (§4).

## 1. Invocation

```
lip rpc
```

`lip rpc` serves one [JSON-RPC 2.0] connection on its standard input and standard output until standard input is closed, then exits with code 0. It takes no options.

The [workspace][PF workspace] of every [method] is the current working directory of the `lip rpc` process.

## 2. Protocol

- **Protocol**: [JSON-RPC 2.0].
- **Transport**: `lip rpc` reads messages from standard input, and writes responses and [client calls][client call] to standard output.
- **Framing**: each message is a JSON ([RFC 8259]) body encoded in UTF-8, preceded by the header `Content-Length: <bytes>` and an empty line (`\r\n\r\n`), as in the base protocol of the [Language Server Protocol][LSP].
- **Parameters**: the parameters of a [method] MAY be given by position, in the order §3 lists them, or by name. A parameter marked `?` MAY be omitted.
- **Results**: a [method] that succeeds responds with its result; a [method] whose result §3 lists as `null` responds with `null`.
- **Errors**: a [method] that fails responds with a [JSON-RPC 2.0] error whose `message` describes the cause. The failure has the same effect on the [workspace][PF workspace] as the failure of the corresponding [command][CLI command] (CLI §4).
- **Cancellation**: the [client] MAY cancel a pending request with the notification `$/cancelRequest` `{ "id": <request id> }`. A cancelled [method] that is a [workspace operation][CLI workspace operation] behaves as an interrupted one (CLI §4 "Cancellation"): its changes are undone, and it responds with an error.
- **Concurrency**: the [client] MAY send further requests before earlier ones are answered. A [method] that is a [workspace operation][CLI workspace operation] fails at once when another [workspace operation][CLI workspace operation] is running on the same [workspace][PF workspace] (CLI §4 "Concurrency").

## 3. Methods

| [Method] | Parameters | Result | Corresponding [command][CLI command] |
|---|---|---|---|
| `Install` | `packages: string[]`, `dryRun: boolean`, `ignoreScripts: boolean`, `noDependencies: boolean`, `rid?: string \| null` | `null` | `lip install` (CLI §5.1) |
| `Uninstall` | `packages: string[]`, `dryRun: boolean`, `ignoreScripts: boolean`, `noDependencies: boolean`, `rid?: string \| null` | `null` | `lip uninstall` (CLI §5.2) |
| `Update` | `packages: string[]`, `dryRun: boolean`, `ignoreScripts: boolean`, `rid?: string \| null` | `null` | `lip update` (CLI §5.3); an empty `packages` is `lip update` without arguments |
| `List` | — | `[string[], string[]]`: the [explicitly installed][PF explicitly installed] [packages][PF package], then the [dependency packages][PF dependency package], each as `<identity>@<version>` | `lip list` (CLI §5.4) |
| `Versions` | `package: string` | `string[]` | `lip versions` (CLI §5.5) |
| `View` | `package: string` | `string`: the [root document][PF root document] as JSON text | `lip view` (CLI §5.6) |
| `Init` | — | `null` | `lip init` (CLI §5.7) |
| `Migrate` | — | `null` | `lip migrate` (CLI §5.8), except that the [method] fails wherever `lip migrate` would ask a [question][CLI question] |
| `CacheClean` | — | `null` | `lip cache clean` (CLI §5.9) |
| `ConfigGet` | `key: string` | `string` | `lip config get` (CLI §5.10) |
| `ConfigSet` | `key: string`, `value: string` | `null` | `lip config set` (CLI §5.10) |
| `ConfigList` | — | `{ [key: string]: string }` | `lip config list` (CLI §5.10) |
| `ConfigDelete` | `key: string` | `null` | `lip config delete` (CLI §5.10) |

`packages` and `package` hold [package arguments][CLI package argument] (CLI §2), or, for `Uninstall`, the arguments of CLI §5.2. `dryRun`, `ignoreScripts` and `noDependencies` correspond to the options `--dry-run`, `--ignore-scripts` and `--no-dependencies`; `true` means the option is given. `rid` corresponds to `--rid`; `null`, `""` or an omitted `rid` means the option is not given.

## 4. Calls to the Client

`lip rpc` sends each [message][CLI message] that `lip` would write to standard error (CLI §1.2) to the [client] as one of these [client calls][client call]:

| [Client call][client call] | Parameters | Meaning |
|---|---|---|
| `PrintInfo` | `message: string` | An informational [message][CLI message]. |
| `PrintSuccess` | `message: string` | A success [message][CLI message]. |
| `PrintWarning` | `message: string` | A warning. |
| `PrintError` | `message: string` | An error [message][CLI message]. |
| `ReportProgress` | `id: string`, `message: string`, `percentage: number` | The progress of a running task. All `ReportProgress` calls of one task have the same `id` and `message`; `percentage` is the progress reached so far. |

The [client] MUST answer each [client call] with the result `null`. After sending a `Print*` [client call], `lip rpc` does not continue the [method] that sent it until the answer arrives. After sending `ReportProgress`, `lip rpc` does not wait for the answer.

`lip rpc` does not send the final success line of a [command][CLI command]; the response of the [method] takes its place.

The OpenRPC document [rpc.openrpc.yaml][openrpc] MUST describe the same [methods][method], parameters and results as this specification.

## References

- [CLI]: lip Command Line Interface Specification.
- [PF]: lip Package Specification.
- [RFC 2119], [RFC 8174]: Key words for use in RFCs to Indicate Requirement Levels.
- [RFC 8259]: The JavaScript Object Notation (JSON) Data Interchange Format.
- [JSON-RPC 2.0]: JSON-RPC 2.0 Specification.
- [LSP]: Language Server Protocol, Base Protocol.

[client]: #t-client
[method]: #t-method
[client call]: #t-client-call

[CLI]: ../cli.md
[CLI command]: ../cli.md#t-command
[CLI workspace operation]: ../cli.md#t-workspace-operation
[CLI message]: ../cli.md#t-message
[CLI question]: ../cli.md#t-question
[CLI package argument]: ../cli.md#t-package-argument
[PF]: ../package_format.md
[PF workspace]: ../package_format.md#t-workspace
[PF package]: ../package_format.md#t-package
[PF root document]: ../package_format.md#t-root-document
[PF explicitly installed]: ../package_format.md#t-explicitly-installed
[PF dependency package]: ../package_format.md#t-dependency-package
[openrpc]: rpc.openrpc.yaml

[RFC 2119]: https://www.rfc-editor.org/rfc/rfc2119
[RFC 8174]: https://www.rfc-editor.org/rfc/rfc8174
[RFC 8259]: https://www.rfc-editor.org/rfc/rfc8259
[JSON-RPC 2.0]: https://www.jsonrpc.org/specification
[LSP]: https://microsoft.github.io/language-server-protocol/specifications/lsp/3.17/specification/#baseProtocol
