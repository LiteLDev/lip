# lipd 概览

`lipd` 通过标准输入/输出（stdio）通信，因此很容易与任意可启动子进程并重定向流的语言或工具集成。

## 快速开始

启动守护进程：

```bash
lipd run
```

这会启动 JSON-RPC 服务器，从 `stdin` 读取请求并将响应写入 `stdout`，直到 `stdin` 关闭。每个方法都作用于 `lipd` 当前工作目录所在的工作区。

## 连接方式

### 协议概览

- **协议**：[JSON-RPC 2.0](https://www.jsonrpc.org/specification)
- **传输**：标准输入/输出（stdio）
- **消息帧**：每条消息是 UTF-8 编码的 JSON 正文，前面带有 `Content-Length: <字节数>` 头和一个空行（`\r\n\r\n`），与 Language Server Protocol 相同

::: tip
建议使用你所选语言的 JSON-RPC 库，以可靠处理消息封装与解析。
:::

## 客户端约定

方法执行期间，`lipd` 会把 `lip` 本应输出的消息（例如进度更新或日志消息）以 JSON-RPC **请求**（而非通知）的形式发给客户端。客户端必须以结果 `null` 应答每个请求：在应答到达之前，`lipd` 不会继续执行发出 `Print*` 请求的方法；对 `ReportProgress` 则不等待应答。

这些请求包括：

- `PrintInfo`
- `PrintSuccess`
- `PrintWarning`
- `PrintError`
- `ReportProgress`

这些方法的定义请参见 [JSON-RPC 规范](json_rpc_spec.md)。
