# JSON-RPC Specification

`lipd` exposes the core functionality of lip through a JSON-RPC 2.0 interface. The normative definition is the [lipd specification](https://github.com/LiteLDev/lip/blob/main/refs/lipd.md).

## OpenRPC Specification

For a complete and rigorous definition of all available methods, parameters, and return types, please refer to our OpenRPC schema:

[**lipd.v4.openrpc.json**](https://github.com/LiteLDev/lip/blob/main/schemas/lipd.v4.openrpc.json)

This schema defines:
-   All available RPC methods (e.g., `Install`, `List`, `Versions`).
-   Input parameters and their types.
-   Return values.
-   The requests the server sends to the client (`PrintInfo`, `PrintSuccess`, `PrintWarning`, `PrintError` and `ReportProgress`), which the client answers with `null`.

Parameters may be given by position, in the order of the schema, or by name.

## Environment Parameters

`Install`, `Uninstall` and `Update` take an optional last parameter that corresponds to the CLI option `--rid`:

| Parameter | Type | Description |
| --- | --- | --- |
| `rid` | `string \| null` | A .NET RID such as `"win-x64"`. `null`, `""` or an omitted `rid` uses the lock or lip's own RID. |

The `packages` parameter of `Update` may be empty to update every explicitly installed package.

## Errors and Cancellation

A method that fails responds with a JSON-RPC error whose `message` describes the cause, and leaves the workspace as the failing `lip` command would. The client may cancel a pending request with the notification `$/cancelRequest` `{ "id": <request id> }`: a cancelled `Install`, `Uninstall` or `Update` undoes its changes and responds with an error. The client may send further requests before earlier ones are answered, but an `Install`, `Uninstall` or `Update` that finds another one running on the same workspace fails at once.
