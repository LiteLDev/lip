# JSON-RPC 规范

`lipd` 通过 JSON-RPC 2.0 接口暴露 lip 的核心能力。

## OpenRPC 规范

关于全部方法、参数与返回类型的完整且严格定义，请参阅我们的 OpenRPC schema：

[**lipd.v3.openrpc.json**](https://github.com/LiteLDev/lip/blob/main/schemas/lipd.v3.openrpc.json)

该 schema 定义了：
- 所有可用 RPC 方法（如 `Install`、`List`、`Versions`）。
- 输入参数及其类型。
- 返回值。
- 服务端发送的通知。
