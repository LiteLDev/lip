# CLI 概览

`lip` CLI 是管理包的主要接口，提供安装、更新和依赖管理等命令。

## 全局选项

| 选项 | 说明 |
| :--- | :--- |
| `-h, --help` | 显示 `lip` 或某个命令的帮助（例如 `lip install --help`）。 |
| `--version` | 输出 `lip` 的版本号。 |

部分命令支持 `--json`，以 JSON 格式输出结果：[`config get`](commands/config.md)、[`config list`](commands/config.md)、[`list`](commands/list.md) 和 [`versions`](commands/versions.md)。

## 命令

- [`lip cache clean`](commands/cache_clean.md)：清理本地缓存。
- [`lip config`](commands/config.md)：获取、设置、列出和删除配置项。
- [`lip init`](commands/init.md)：初始化新项目。
- [`lip install`](commands/install.md)：安装包。
- [`lip list`](commands/list.md)：列出已安装包。
- [`lip migrate`](commands/migrate.md)：将包清单迁移到当前格式版本。
- [`lip uninstall`](commands/uninstall.md)：卸载包。
- [`lip update`](commands/update.md)：更新包。
- [`lip versions`](commands/versions.md)：显示某个包的可用版本。
- [`lip view`](commands/view.md)：显示包的清单内容。
