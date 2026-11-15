# 配置

lip 有两个配置文件：用户配置，作用于 lip 为你做的一切；工作区配置，作用于一个工作区。

## 用户配置

### 文件位置

| 平台 | 路径 |
| --- | --- |
| Windows | `%APPDATA%\lip\config.toml` |
| Linux / macOS | `$XDG_CONFIG_HOME/lip/config.toml`（通常为 `~/.config/lip/config.toml`） |

该文件是一个 [TOML 1.0](https://toml.io/cn/v1.0.0) 文件，包含 `format = 4` 和下列配置键的值，由以 TOML 编写的 JSON Schema [`config.schema.toml`](https://github.com/LiteLDev/lip/blob/main/protocol/config.schema.toml) 描述；文件中没有的键取默认值，没有默认值时则没有值。首次使用 [`lip config set`](commands/config.md) 设置值时，lip 会创建该文件：

```toml
format = 4
github_proxy = "https://proxy.example.com"
```

没有 `config.toml` 时，同一目录中 lip v0.x 的 `liprc.json` 会被自动转换为 `config.toml`，随后被删除。

### 配置键

| 键 | 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `github_proxy` | `string?` | 无 | lip 如何访问 GitHub 进行 git 请求与下载（发布资源）。URI，例如 `https://proxy.example.com/`：lip 通过该代理发送请求，代理请求失败时直接向 `github.com` 重试。空字符串（`lip config set github_proxy ""`）：lip 直接连接 GitHub。未设置（`lip config delete github_proxy`）：lip 同样直接连接 GitHub，但该键视为未配置。`lip config set` 会拒绝其他值，含有其他值的 `config.toml` 无效，此时所有键取默认值。 |

包直接从其 git 仓库解析和下载，无需配置任何包注册表或模块代理。

## 工作区配置

工作区配置是工作区中的 `.lip/config.toml`，一个包含 `format = 4` 和下列配置键的 TOML 文件，由 [`workspace-config.schema.toml`](https://github.com/LiteLDev/lip/blob/main/protocol/workspace-config.schema.toml) 描述。在工作区中使用 `lip config --workspace` 管理它：

```toml
format = 4
restart = "on-failure"
```

### 配置键

| 键 | 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `restart` | `"never"` 或 `"on-failure"` | `"never"` | 工作区的服务器以非零退出码退出时是否重启：`on-failure` 时在 5 秒后重启，10 分钟内至多 5 次（参见 [`lip run`](commands/run.md#重启)）。 |

## 管理方式

使用 [`lip config`](commands/config.md) 或[交互界面](tui.md)的 Settings 标签页管理配置值。
