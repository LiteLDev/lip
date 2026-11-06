# 配置

lip 将配置存储在 `config.yaml` 中。

## 文件位置

| 平台 | 路径 |
| --- | --- |
| Windows | `%APPDATA%\lip\config.yaml` |
| Linux / macOS | `$XDG_CONFIG_HOME/lip/config.yaml`（通常为 `~/.config/lip/config.yaml`） |

该文件是一个 YAML 映射，包含 `format: 4` 和下列配置键的值，由 JSON Schema [`config.schema.yaml`](https://github.com/LiteLDev/lip/blob/main/protocol/config.schema.yaml) 描述；文件中没有的键取默认值。首次使用 [`lip config set`](commands/config.md) 设置值时，lip 会创建该文件：

```yaml
format: 4
github_proxy: https://proxy.example.com
```

没有 `config.yaml` 时，同一目录中 lip v0.x 的 `liprc.json` 会被自动转换为 `config.yaml`，随后被删除。

## 配置键

| 键 | 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `github_proxy` | `string?` | `null` | GitHub 请求与下载（git 请求与发布资源）的代理 URL。代理请求失败时，lip 会直接向 `github.com` 重试。 |

包直接从其 git 仓库解析和下载，无需配置任何包注册表或模块代理。

## 管理方式

使用 [`lip config`](commands/config.md) 管理配置值。
