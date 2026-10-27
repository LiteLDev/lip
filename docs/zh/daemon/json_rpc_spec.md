# JSON-RPC 规范

`lipd` 通过 JSON-RPC 2.0 接口暴露 lip 的核心能力。

## OpenRPC 规范

关于全部方法、参数与返回类型的完整且严格定义，请参阅我们的 OpenRPC schema：

[**lipd.v4.openrpc.json**](https://github.com/LiteLDev/lip/blob/main/schemas/lipd.v4.openrpc.json)

该 schema 定义了：
- 所有可用 RPC 方法（如 `Install`、`List`、`Versions`）。
- 输入参数及其类型。
- 返回值。
- 服务端发送的通知。

## 环境参数

`Install`、`Uninstall` 和 `Update` 末尾有两个可选参数，对应 CLI 选项 `--platform` 和 `--rid`：

| 参数 | 类型 | 说明 |
| --- | --- | --- |
| `platform` | `string \| null` | `"server"` 或 `"client"`。为 `null` 时自动检测（参见[环境](../concepts/package_manifest.md#环境)）。 |
| `rid` | `string \| null` | .NET RID，例如 `"win-x64"`。为 `null` 时使用锁中的值或 lip 自身的 RID。 |

`Update` 的 `packages` 参数可以为空，表示更新所有显式安装的包。
