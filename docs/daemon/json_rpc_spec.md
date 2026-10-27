# JSON-RPC Specification

`lipd` exposes the core functionality of lip through a JSON-RPC 2.0 interface.

## OpenRPC Specification

For a complete and rigorous definition of all available methods, parameters, and return types, please refer to our OpenRPC schema:

[**lipd.v4.openrpc.json**](https://github.com/LiteLDev/lip/blob/main/schemas/lipd.v4.openrpc.json)

This schema defines:
-   All available RPC methods (e.g., `Install`, `List`, `Versions`).
-   Input parameters and their types.
-   Return values.
-   Notifications sent by the server.

## Environment Parameters

`Install`, `Uninstall` and `Update` take an optional trailing parameter that corresponds to the CLI option `--rid`:

| Parameter | Type | Description |
| --- | --- | --- |
| `rid` | `string \| null` | A .NET RID such as `"win-x64"`. `null` uses the lock or lip's own RID. |

The `packages` parameter of `Update` may be empty to update every explicitly installed package.
