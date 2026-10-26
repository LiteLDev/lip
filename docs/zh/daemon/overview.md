# lipd 概览

`lipd` 通过标准输入/输出（stdio）通信，因此很容易与任意可启动子进程并重定向流的语言或工具集成。

## 快速开始

启动守护进程：

```bash
lipd run
```

这会启动 JSON-RPC 服务器，从 `stdin` 读取请求并将响应写入 `stdout`。

## 连接方式

### 协议概览

- **协议**：[JSON-RPC 2.0](https://www.jsonrpc.org/specification)
- **传输**：标准输入/输出（stdio）
- **消息帧**：每条消息是 UTF-8 编码的 JSON 正文，前面带有 `Content-Length: <字节数>` 头和一个空行（`\r\n\r\n`），与 Language Server Protocol 相同

::: tip
建议使用你所选语言的 JSON-RPC 库，以可靠处理消息封装与解析。
:::

## 客户端约定

连接 `lipd` 时，客户端应能处理守护进程发送的通知。这些通知用于反馈操作状态，例如进度更新或日志消息。

常见通知包括：

- `PrintInfo`
- `PrintSuccess`
- `PrintWarning`
- `PrintError`
- `ReportProgress`

严谨定义请参见 [OpenRPC 规范](json_rpc_spec.md)。
