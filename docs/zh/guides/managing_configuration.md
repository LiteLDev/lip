# 管理配置

lip 有两类配置：用户配置，包含影响 lip 与远程服务交互方式的设置；工作区配置，包含一个工作区的设置。在 `lip config` 命令后加上 `--workspace`，即可使用当前目录中工作区的配置。

## 查看配置

查看当前全部配置：

```sh
lip config list
lip config list --workspace
```

查看某个键的值：

```sh
lip config get <KEY>
```

示例：
```sh
lip config get github_proxy
```

## 设置配置

设置配置值：

```sh
lip config set <KEY> <VALUE>
```

### 常见配置

- `github_proxy`（用户）：GitHub 请求使用的代理 URL，或空字符串表示直接连接 GitHub。
- `language`（用户）：`en`、`zh-Hans` 或 `zh-Hant`，[交互界面](../cli/tui.md#语言)的语言。未设置时，界面跟随系统的语言。
- `restart`（工作区）：`never` 或 `on-failure`，工作区的服务器以非零退出码退出时 [`lip run`](../cli/commands/run.md#重启) 是否重启它。

示例：
```sh
lip config set github_proxy https://mirror.ghproxy.com/
lip config set github_proxy ""
lip config set language zh-Hans
lip config set restart on-failure --workspace
```

## 删除配置

移除某项配置并恢复默认值（如果有）：

```sh
lip config delete <KEY>
```
