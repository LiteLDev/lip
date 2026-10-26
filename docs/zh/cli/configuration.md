# 配置

lip 将配置存储在 `liprc.json` 中。

## 文件位置

| 平台 | 路径 |
| --- | --- |
| Windows | `%APPDATA%\\lip\\liprc.json` |
| Linux / macOS | `$XDG_CONFIG_HOME/lip/liprc.json`（通常为 `~/.config/lip/liprc.json`） |

当 lip 首次运行时，会自动创建该文件并写入默认值。

## 配置键

| 键 | 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `github_proxy` | `string?` | `null` | GitHub 请求与下载（git 请求与发布资源）的代理 URL。代理请求失败时，lip 会直接向 `github.com` 重试。 |

包直接从其 git 仓库解析和下载，无需配置任何包注册表或模块代理。原有的 `go_module_proxy` 配置键已被移除；若现有 `liprc.json` 中仍包含该键，它会被忽略，并在下次保存文件时被删除。

## 管理方式

使用 [`lip config`](commands/config.md) 管理配置值。
