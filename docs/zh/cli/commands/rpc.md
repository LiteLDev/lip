# lip rpc

通过 stdio 提供 JSON-RPC 接口。

## 概要

```shell
lip rpc
```

## 说明

从标准输入读取 JSON-RPC 2.0 消息，并将响应写入标准输出；每条消息都带有 `Content-Length` 头作为消息帧，与 Language Server Protocol 相同。它只服务一个客户端，标准输入关闭时以退出码 0 退出。每个方法都作用于当前工作目录所在的工作区。

该命令面向需要以编程方式访问 lip 的 IDE 插件、GUI 和脚本，由它们以子进程方式启动 `lip rpc`。协议说明参见 [JSON-RPC 概览](../../rpc/overview.md)，方法定义参见 [JSON-RPC 规范](../../rpc/json_rpc_spec.md)。

该命令不接受任何选项。
