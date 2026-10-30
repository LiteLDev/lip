# JSON-RPC 规范

`lipd` 通过 JSON-RPC 2.0 接口暴露 lip 的核心能力。规范性定义见 [lipd 规范](https://github.com/LiteLDev/lip/blob/main/refs/lipd.md)。

## OpenRPC 规范

关于全部方法、参数与返回类型的完整且严格定义，请参阅我们的 OpenRPC schema：

[**lipd.v4.openrpc.json**](https://github.com/LiteLDev/lip/blob/main/refs/schemas/lipd.v4.openrpc.json)

该 schema 定义了：
- 所有可用 RPC 方法（如 `Install`、`List`、`Versions`）。
- 输入参数及其类型。
- 返回值。
- 服务端发给客户端的请求（`PrintInfo`、`PrintSuccess`、`PrintWarning`、`PrintError` 和 `ReportProgress`），客户端以 `null` 应答。

参数可以按位置（按 schema 中的顺序）或按名称传递。

## 环境参数

`Install`、`Uninstall` 和 `Update` 的最后一个参数是可选的，对应 CLI 选项 `--rid`：

| 参数 | 类型 | 说明 |
| --- | --- | --- |
| `rid` | `string \| null` | .NET RID，例如 `"win-x64"`。为 `null`、`""` 或省略时使用锁中的值或 lip 自身的 RID。 |

`Update` 的 `packages` 参数可以为空，表示更新所有显式安装的包。

## 错误与取消

方法失败时以 JSON-RPC 错误响应，其 `message` 说明原因，工作区的状态与对应 `lip` 命令失败时相同。客户端可以用通知 `$/cancelRequest` `{ "id": <请求 id> }` 取消一个尚未完成的请求：被取消的 `Install`、`Uninstall` 或 `Update` 会撤销其修改，并以错误响应。客户端可以在先前的请求得到响应之前发送新的请求，但发现同一工作区上已有另一个 `Install`、`Uninstall` 或 `Update` 在运行时，该操作会立即失败。
