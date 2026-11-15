# CLI 概览

`lip` CLI 是管理包的主要接口，提供安装、更新和依赖管理等命令。

## 全局选项

| 选项 | 说明 |
| :--- | :--- |
| `-h, --help` | 显示 `lip` 或某个命令的帮助（例如 `lip install --help`）。 |
| `-v, --version` | 输出 `lip` 的版本号。 |

在终端里不带参数运行 `lip` 会打开[交互界面](tui.md)；否则（在脚本或管道中）输出帮助。

部分命令支持 `--json`，以 JSON 格式输出结果：[`config get`](commands/config.md)、[`config list`](commands/config.md)、[`daemon status`](commands/daemon.md) 和 [`list`](commands/list.md)。[`pkg view`](commands/pkg_view.md) 始终输出 JSON。

[`setup`](commands/setup.md)、[`install`](commands/install.md)、[`uninstall`](commands/uninstall.md) 和 [`update`](commands/update.md) 向工作区安装平台 `win-x64` 的包，无论 lip 运行在什么系统上（参见[环境](../concepts/lip_toml.md#环境)）；Linux 版服务器是变体 `github.com/LiteLDev/bds#linux`。`setup` 选择工作区的游戏（服务器）和加载器（LeviLamina）；`install` 和 `update` 从不改变它们。lip 只管理服务端工作区。

## 命令

用于运行服务器的命令：

- [`lip cache clean`](commands/cache_clean.md)：清理本地缓存。
- [`lip config`](commands/config.md)：获取、设置、列出和删除配置项。
- [`lip install`](commands/install.md)：安装包。
- [`lip list`](commands/list.md)：列出已安装包。
- [`lip run`](commands/run.md)：运行工作区的服务器。
- [`lip self-update`](commands/self_update.md)：把 lip 自身更新到最新发布的版本。
- [`lip setup`](commands/setup.md)：选择工作区的游戏和加载器。
- [`lip uninstall`](commands/uninstall.md)：卸载包。
- [`lip update`](commands/update.md)：更新包。

除 `lip daemon` 外，每个命令都由 lip 的守护进程执行：它是你的用户的一个后台进程，由第一个命令启动；命令的行为与你启动的 `lip` 自己执行时完全相同。命令组 `daemon` 用于查看和停止它：

- [`lip daemon status`](commands/daemon.md)：显示守护进程是否在运行。
- [`lip daemon stop`](commands/daemon.md)：停止守护进程。

用于开发包的命令，位于命令组 `pkg` 中：

- [`lip pkg init`](commands/pkg_init.md)：为新包创建 `lip.toml`。
- [`lip pkg migrate`](commands/pkg_migrate.md)：把当前目录中 lip v0.x 的 `tooth.json` 迁移为 `lip.toml`。
- [`lip pkg pack`](commands/pkg_pack.md)：把一个目录打包为 ZIP 归档。
- [`lip pkg view`](commands/pkg_view.md)：显示包的包文件和版本。

`lip pkg --help`、`lip cache --help`、`lip config --help` 和 `lip daemon --help` 显示命令组中的命令。lip 早期版本的 `lip init`、`lip pack`、`lip view`、`lip versions` 和 `lip migrate` 现在分别是 `lip pkg init`、`lip pkg pack`、`lip pkg view`（它同时列出版本）和 `lip pkg migrate`。
