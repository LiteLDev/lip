# lip run

运行工作区的服务器。

## 概要

```shell
lip run
```

## 说明

不带参数地运行工作区（当前目录）根部的 `bedrock_server_mod.exe`，以工作区为工作目录，并等待它退出。请先安装服务器，例如用 `lip install github.com/LiteLDev/LeviLamina`；参见[运行服务器](../../guides/running_server.md)。

启动服务器之前，`lip run` 会：

1. 若另一个 lip 命令（如 `lip install`）正在修改该工作区，立即失败；
2. 像 `lip install` 一样，完成或撤销被中断的操作（参见[内部机制](../../guides/internal_mechanism.md#工作区状态)）；
3. 若 `bedrock_server_mod.exe` 不是文件，则失败。

服务器运行期间工作区不会被锁定：你可以在另一个终端中对同一工作区运行 `lip install`、`lip uninstall` 和 `lip update`。它们的改动在服务器下次启动时生效。

### Wine

当工作区[环境](../../concepts/lip_toml.md#环境)的平台以 `win` 开头（例如默认的 `win-x64`），且 lip 不在 Windows 上运行时，服务器通过 [Wine](https://www.winehq.org/) 运行：lip 运行在 `PATH` 中找到的 `wine`，以 `bedrock_server_mod.exe` 的绝对路径为其唯一参数，并设置以下环境变量：

| 变量 | 值 |
| --- | --- |
| `WINEPREFIX` | 未设置或为空时：工作区中 `.lip/wine` 的绝对路径。前缀不存在时由 Wine 创建。 |
| `WINEDEBUG` | 未设置时：`-all`，即关闭 Wine 的调试输出。 |

其他情况下，lip 直接运行服务器。

### 输入与输出

服务器输出到 lip 的终端，并读取你输入的内容：lip 从其标准输入读到的每个字节都会转交给服务器，因此你可以像平常一样输入服务器命令。

### 停止

按 Ctrl+C（或向 lip 发送 `SIGINT`、`SIGTERM` 或 `SIGHUP`）即可停止服务器：第一次时，lip 向服务器输入一行 `stop`，服务器随即保存世界并退出。无论按多少次 Ctrl+C，lip 都要等服务器退出后才会退出；若服务器卡住，请结束其进程。

## 退出状态

服务器已启动时，`lip run` 以服务器的退出码退出（通过 Wine 运行时为 `wine` 的退出码）；在 Windows 以外的系统上服务器被信号 n 结束时，以 128 + n 退出（例如 `SIGKILL` 为 137）。否则它以非零退出码退出，并输出原因。

## 示例

```shell
lip install github.com/LiteLDev/LeviLamina
lip run
```
