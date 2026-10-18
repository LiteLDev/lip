# JSON-RPC Specification

`lipd` exposes the core functionality of lip through a JSON-RPC 2.0 interface.

## OpenRPC Specification

For a complete and rigorous definition of all available methods, parameters, and return types, please refer to our OpenRPC schema:

[**lipd.v3.openrpc.json**](https://github.com/LiteLDev/lip/blob/main/schemas/lipd.v3.openrpc.json)

This schema defines:
-   All available RPC methods (e.g., `Install`, `List`, `Versions`).
-   Input parameters and their types.
-   Return values.
-   Notifications sent by the server.
