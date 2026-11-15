# lip run

运行工作区的服务器。

## 概要

```shell
lip run [--managed [--detach]]
```

## 说明

不带参数地运行工作区（当前目录）根部的服务器，以工作区为工作目录，并等待它退出。请先用 [`lip setup`](setup.md) 设置服务器，例如 `lip setup --game 1.21.0`；参见[运行服务器](../../guides/running_server.md)。

服务器是工作区的游戏和加载器（参见 [`lip setup`](setup.md#游戏与加载器)）所对应的文件：

| 工作区装有 | 服务器 | 运行方式 |
| --- | --- | --- |
| LeviLamina | `bedrock_server_mod.exe` | 在 Windows 上由 lip 运行，在其他系统上通过 Wine 运行 |
| 无加载器，Windows 版服务器 | `bedrock_server.exe` | 在 Windows 上由 lip 运行，在其他系统上通过 Wine 运行 |
| 无加载器，Linux 版服务器 | `bedrock_server` | 由 lip 运行，仅限 x64 的 Linux |

lip 运行 Linux 版服务器时，若环境变量 `LD_LIBRARY_PATH` 未设置或为空，则将其设为 `.`，使服务器能找到其旁边的库。

启动服务器之前，`lip run` 会：

1. 若该工作区的服务器已在运行（由另一个 `lip run`、交互界面或 lip 的守护进程运行），立即失败（每个工作区至多运行一个服务器，它在运行期间持有 `.lip/server.lock`）；
2. 若另一个 lip 命令（如 `lip install`）正在修改该工作区，立即失败；
3. 像 `lip install` 一样，完成或撤销被中断的操作（参见[内部机制](../../guides/internal_mechanism.md#工作区状态)）；
4. 若工作区没有游戏（此时提示使用 `lip setup`）、服务器是 Linux 版服务器而 lip 不在 x64 的 Linux 上运行，或服务器不是文件，则失败；
5. 对 Windows 版服务器 `bedrock_server_mod.exe` 和 `bedrock_server.exe`，安装服务器需要而系统缺少的组件：Linux 和 macOS 上的 Wine，以及 Microsoft Visual C++ 可再发行组件包（参见[系统依赖](../../guides/internal_mechanism.md#系统依赖)）。Linux 版服务器两者都不需要。

服务器运行期间工作区不会被锁定：你可以在另一个终端中对同一工作区运行 `lip install`、`lip uninstall` 和 `lip update`。它们的改动在服务器下次启动时生效。[交互界面](../tui.md#服务器运行期间)在此期间不会执行它们：它也能看到由 `lip run` 启动的服务器，并会请你先停止它。

### Wine

当 lip 不在 Windows 上运行时，由于工作区[环境](../../concepts/lip_toml.md#环境)的平台是 `win-x64`，Windows 版服务器通过 [Wine](https://www.winehq.org/) 运行：lip 运行它安装在数据目录中的 Wine 的 `wine`，从不运行在 `PATH` 中找到的，以服务器文件的绝对路径为其唯一参数，并设置以下环境变量：

| 变量 | 值 |
| --- | --- |
| `WINEPREFIX` | 未设置或为空时：工作区中 `.lip/wine` 的绝对路径。前缀不存在时由 Wine 创建。 |
| `WINEDEBUG` | 未设置时：`-all`，即关闭 Wine 的调试输出。 |

其他情况下，以及对 Linux 版服务器，lip 直接运行服务器。

### 输入与输出

服务器输出到 lip 的终端，并读取你输入的内容：lip 从其标准输入读到的每个字节都会转交给服务器，因此你可以像平常一样输入服务器命令。

### 停止

按 Ctrl+C（或向 lip 发送 `SIGINT`、`SIGTERM` 或 `SIGHUP`）即可停止服务器：第一次时，lip 向服务器输入一行 `stop`，服务器随即保存世界并退出。无论按多少次 Ctrl+C，lip 都要等服务器退出后才会退出；若服务器卡住，请结束其进程。

### 重启

当工作区配置中 `restart = "on-failure"`（`lip config set restart on-failure --workspace`，参见 [`lip config`](config.md)）时，服务器以非零退出码退出且你没有要求它停止的话，lip 会重启它：输出一条消息，等待 5 秒，然后重新执行上述步骤并启动服务器。10 分钟内至多重启 5 次；超过后 lip 输出不再重启的消息并退出。lip 在每次服务器退出时读取 `restart`，因此你可以在服务器运行期间修改它。以退出码 0 退出的服务器，或你要求停止的服务器，不会被重启。

### 托管服务器

加上 `--managed` 时，服务器是**托管服务器**：由 [lip 的守护进程](daemon.md)运行，启动它的终端关闭后它仍继续运行，直到它退出或你停止它。守护进程为每个工作区至多运行一个托管服务器：

- `lip run --managed` 启动该工作区的托管服务器（或找到正在运行的那个），并连接（attach）到它：你能看到它的输出，你输入的内容会发给它。若服务器已在运行，你会先看到它最近输出的一部分。
- `lip run --managed --detach` 启动它（或找到正在运行的那个）后立即以 0 退出。
- 连接期间，Ctrl+C（`SIGINT`，或 Windows 上的 Ctrl+Break）如上所述要求服务器停止，你会保持连接直到它退出。关闭终端（`SIGHUP`，或在 Windows 上关闭控制台窗口）、`SIGTERM` 或结束 `lip` 进程则会断开连接（detach）：服务器继续运行，再次运行 `lip run --managed` 即可重新连接。
- [交互界面](../tui.md)也显示托管服务器并连接到它们：在其[服务器](../tui.md#服务器)页面中启动的服务器就是托管服务器。

托管服务器使用启动它的 `lip` 的环境变量运行，其重启规则同上。lip 不经守护进程自己执行命令时（例如守护进程无法启动），`lip run --managed` 会失败，因为只有守护进程运行托管服务器。不带 `--managed` 使用 `--detach` 是用法错误。

## 退出状态

服务器已启动时，`lip run` 以最后运行的服务器的退出码退出（通过 Wine 运行时为 `wine` 的退出码）；在 Windows 以外的系统上服务器被信号 n 结束时，以 128 + n 退出（例如 `SIGKILL` 为 137）。否则它以非零退出码退出，并输出原因。

`lip run --managed` 在你断开连接或给出 `--detach` 时以 0 退出；在连接期间托管服务器停止运行时，以其 `lip run` 的退出码退出，规则同上。

## 示例

```shell
lip setup --game 1.21.0
lip run
```

在后台运行服务器，失败时自动重启，稍后再连接到它：

```shell
lip config set restart on-failure --workspace
lip run --managed --detach
lip run --managed
```
