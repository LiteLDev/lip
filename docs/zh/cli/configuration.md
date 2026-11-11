# 配置

lip 将配置存储在 `config.toml` 中。

## 文件位置

| 平台 | 路径 |
| --- | --- |
| Windows | `%APPDATA%\lip\config.toml` |
| Linux / macOS | `$XDG_CONFIG_HOME/lip/config.toml`（通常为 `~/.config/lip/config.toml`） |

该文件是一个 [TOML 1.0](https://toml.io/cn/v1.0.0) 文件，包含 `format = 4` 和下列配置键的值，由以 TOML 编写的 JSON Schema [`config.schema.toml`](https://github.com/LiteLDev/lip/blob/main/protocol/config.schema.toml) 描述；文件中没有的键取默认值。首次使用 [`lip config set`](commands/config.md) 设置值时，lip 会创建该文件：

```toml
format = 4
github_proxy = "https://proxy.example.com"
```

没有 `config.toml` 时，同一目录中 lip v0.x 的 `liprc.json` 会被自动转换为 `config.toml`，随后被删除。

## 配置键

| 键 | 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `github_proxy` | `string?` | `null` | GitHub 请求与下载（git 请求与发布资源）的代理 URL。代理请求失败时，lip 会直接向 `github.com` 重试。必须是 URI，例如 `https://proxy.example.com/`：`lip config set` 会拒绝其他值，含有其他值的 `config.toml` 无效，此时所有键取默认值。 |

包直接从其 git 仓库解析和下载，无需配置任何包注册表或模块代理。

## 管理方式

使用 [`lip config`](commands/config.md) 管理配置值。
