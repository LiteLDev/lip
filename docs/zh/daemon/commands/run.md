# lipd run

启动 lip 守护进程 JSON-RPC 服务器。

## 概要

```bash
lipd run
```

## 说明

`run` 命令会以服务端模式启动 `lipd` 进程。它在标准输入（stdin）上监听 JSON-RPC 2.0 消息，并将响应写入标准输出（stdout）。stdin 关闭时，它以退出码 0 退出。

该命令面向自动化工具、IDE 插件和脚本，用于以编程方式访问 lip 的包管理能力。

## 选项

该命令不接受任何选项。

## 示例

启动守护进程：

```bash
lipd run
```
