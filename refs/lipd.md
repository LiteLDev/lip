# lipd JSON-RPC Specification

This specification defines the observable behavior of `lipd`, which offers the functions of the `lip` command over JSON-RPC. The behavior of each method is that of the corresponding `lip` command, defined by the [CLI specification](cli.md) (cited as "CLI §N"), which in turn relies on the [package specification](package_format.md). "must / must not / may / should" correspond to MUST / MUST NOT / MAY / SHOULD of RFC 2119.

## 1. Invocation

```
lipd [-h | --help] [--version]
lipd run
```

`lipd run` serves one JSON-RPC connection on its standard input and standard output until standard input is closed, then exits with code 0. It takes no options. `--help` and `--version` behave as in CLI §1.

The workspace (CLI §1.1) of every method is the current working directory of the `lipd` process.

## 2. Protocol

- **Protocol**: JSON-RPC 2.0.
- **Transport**: requests are read from standard input; responses and calls to the client are written to standard output.
- **Framing**: each message is a UTF-8 JSON body preceded by a `Content-Length: <bytes>` header and an empty line (`\r\n\r\n`), as in the Language Server Protocol.
- **Parameters**: may be given by position, in the order listed in §3, or by name. A parameter marked optional may be omitted.
- **Results**: a method that succeeds responds with its result; a method listed with result `null` responds with `null`.
- **Errors**: a method that fails responds with a JSON-RPC error whose `message` describes the cause. A failure has the same effect on the workspace as the failure of the corresponding command (CLI §4).
- **Cancellation**: the client may cancel a pending request with the notification `$/cancelRequest` `{ "id": <request id> }`. A cancelled workspace operation behaves as an interrupted one (CLI §4, "Cancellation"): its changes are undone, and it responds with an error.
- **Concurrency**: the client may send further requests before earlier ones are answered. A workspace operation that finds another workspace operation running on the same workspace fails at once (CLI §4, "Concurrency").

## 3. Methods

| Method | Parameters | Result | Corresponds to |
|---|---|---|---|
| `Install` | `packages: string[]`, `dryRun: boolean`, `ignoreScripts: boolean`, `noDependencies: boolean`, `rid?: string \| null` | `null` | `lip install` (CLI §5.1) |
| `Uninstall` | `packages: string[]`, `dryRun: boolean`, `ignoreScripts: boolean`, `noDependencies: boolean`, `rid?: string \| null` | `null` | `lip uninstall` (CLI §5.2) |
| `Update` | `packages: string[]`, `dryRun: boolean`, `ignoreScripts: boolean`, `rid?: string \| null` | `null` | `lip update` (CLI §5.3); an empty `packages` is `lip update` without arguments |
| `List` | — | `[string[], string[]]`: the explicitly installed packages, then the dependencies, each as `<identity>@<version>` | `lip list` (CLI §5.4) |
| `Versions` | `package: string` | `string[]` | `lip versions` (CLI §5.5) |
| `View` | `package: string` | `string`: the document as JSON text | `lip view` (CLI §5.6) |
| `Init` | — | `null` | `lip init` (CLI §5.7) |
| `Migrate` | — | `null` | `lip migrate` (CLI §5.8) |
| `CacheClean` | — | `null` | `lip cache clean` (CLI §5.9) |
| `ConfigGet` | `key: string` | `string` | `lip config get` (CLI §5.10) |
| `ConfigSet` | `key: string`, `value: string` | `null` | `lip config set` |
| `ConfigList` | — | `{ [key: string]: string }` | `lip config list` |
| `ConfigDelete` | `key: string` | `null` | `lip config delete` |

The boolean parameters correspond to the options of the same name (`dryRun` to `--dry-run`, `ignoreScripts` to `--ignore-scripts`, `noDependencies` to `--no-dependencies`). `rid` corresponds to `--rid`; `null`, an empty string or an omitted `rid` means the option is not given.

## 4. Calls to the Client

What `lip` writes to standard error as messages (CLI §1.2), `lipd` sends to the client as the following requests. The client must answer each with the result `null`; lipd does not proceed with the method that sent a `Print*` request until the answer arrives.

| Method | Parameters | Meaning |
|---|---|---|
| `PrintInfo` | `message: string` | An informational message. |
| `PrintSuccess` | `message: string` | A success message. |
| `PrintWarning` | `message: string` | A warning. |
| `PrintError` | `message: string` | An error message. |
| `ReportProgress` | `id: string`, `message: string`, `percentage: number` | Progress of a running task. All reports of one task carry the same `id` and `message`; `percentage` is the progress reached so far. lipd does not wait for the answer. |

The final success line of a CLI command is not sent: the response of the method takes its place.

The OpenRPC document `schemas/lipd.v4.openrpc.json` must describe the same methods, parameters and results as this specification.
