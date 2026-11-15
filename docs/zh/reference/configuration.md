# 配置

本页列出 lip 的全部设置：两个配置文件、它们的格式、每个键的取值、默认值和作用，以及修改它们的方法。

## 两个文件

| 作用域 | 文件 | 适用于 | 修改方式 |
| --- | --- | --- | --- |
| 用户 | [配置目录](files.md#配置目录)中的 `config.toml` | lip 为你做的所有事情。 | `lip config ...` |
| 工作区 | 工作区中的 `.lip/config.toml` | 该工作区。 | 在工作区中运行 `lip config ... --workspace` |

配置目录在 Windows 上是 `%APPDATA%\lip`，在其他系统上是 `$XDG_CONFIG_HOME/lip`，默认即 `~/.config/lip`。

每个作用域有自己的键。一个作用域的键不是另一个作用域的键：不带 `--workspace` 的 `lip config get restart` 会失败。

## 格式

每个文件都是 [TOML 1.0](https://toml.io/en/v1.0.0) 文件，包含 `format = 4` 和所属作用域的键：

::: code-group

```toml [config.toml]
format = 4
github_proxy = "https://proxy.example.com/"
language = "zh-Hans"
```

```toml [.lip/config.toml]
format = 4
restart = "on-failure"
```

:::

- 文件中没有的键取默认值。没有默认值的键此时**没有值**。
- 不属于该作用域的键会被忽略。
- 文件不存在、无法读取或无效时，视为空文件：该作用域的所有键都不存在。只要有一个键的值不被接受，整个文件就无效。
- 只有在你修改某个值时，lip 才会写文件。它写入 `format` 和你设置过的键，省略处于默认值的键和没有值的键。
- 写 `.lip/config.toml` 时，如果 `.lip` 不存在就创建它。这不需要任何锁，所以服务器运行时也可以修改。

这两个文件由以 TOML 书写的 JSON Schema [`config.schema.toml`](https://github.com/LiteLDev/lip/blob/main/protocol/config.schema.toml) 和 [`workspace-config.schema.toml`](https://github.com/LiteLDev/lip/blob/main/protocol/workspace-config.schema.toml) 描述。

## 用户配置

| 键 | 取值 | 默认值 |
| --- | --- | --- |
| `github_proxy` | 一个 URI，或空字符串。 | 无 |
| `language` | `en`、`zh-Hans` 或 `zh-Hant`。 | 无 |

### github_proxy

lip 怎样向 GitHub 发送网络请求：git 仓库和下载。

| 值 | 作用 |
| --- | --- |
| 一个 URI，例如 `https://proxy.example.com/` | lip 通过这个 URI 前缀发送请求。通过它失败的请求会直接发往 GitHub。 |
| 空字符串（`lip config set github_proxy ""`） | lip 直接向 GitHub 发送请求。 |
| 没有值（`lip config delete github_proxy`） | lip 直接向 GitHub 发送请求，并且 `github_proxy` **未配置**。 |

后两种只对界面有区别：`github_proxy` 未配置时，界面在启动时检查能否连上 GitHub，连不上就询问你怎样连接。只要它有值（包括空字符串），界面就不再询问。

无论 `github_proxy` 是什么，[`lip self-update`](commands/self_update.md) 都直接从 GitHub 读取发布列表和校验和，只有更新文件本身通过它下载。

### language

[界面](interface.md)的语言：`en` 为英文，`zh-Hans` 为简体中文，`zh-Hant` 为繁体中文。

没有值时，界面跟随系统语言：在 Windows 上是显示语言，在其他系统上是 `LC_ALL`、`LC_MESSAGES` 和 `LANG` 中第一个指明了语言的变量。如果它既不是英文也不是中文，界面使用英文。

`language` 只影响界面。命令的消息和结果始终是英文。

## 工作区配置

| 键 | 取值 | 默认值 |
| --- | --- | --- |
| `restart` | `never` 或 `on-failure`。 | `never` |

### restart

工作区的服务器以非 0 退出码退出时，是否重启它。

| 值 | 作用 |
| --- | --- |
| `never` | 不重启服务器。 |
| `on-failure` | lip 写一条消息，等待 5 秒，然后再次启动服务器；10 分钟内最多 5 次。你要求停止的服务器不会被重启。 |

lip 在服务器每次退出时读取 `restart`，所以可以在服务器运行时修改它。参见 [`lip run`](commands/run.md#重启)。

## 修改配置

用命令修改，参见 [`lip config`](commands/config.md)：

```shell
lip config list
lip config set github_proxy https://proxy.example.com/
lip config delete github_proxy
lip config set restart on-failure --workspace
```

值不被该键接受时，`lip config set` 失败，不做任何更改。

在界面中，[设置](../interface/settings.md)页修改的是同样的文件：

| 设置页中的项 | 键 | 等同于 |
| --- | --- | --- |
| 语言 | `language` | `lip config set language <value>`；**跟随系统**即 `lip config delete language`。 |
| GitHub 代理 | `github_proxy` | `lip config set github_proxy <URI>`；**直连**设置为空字符串；**重置**即 `lip config delete github_proxy`。 |
| 自动重启 | 当前工作区的 `restart` | 打开时为 `lip config set restart on-failure --workspace`，关闭时为 `never`。 |

当你回答界面关于如何连接 GitHub 的询问时，界面也会写入 `github_proxy`：填写代理则设置为该 URI，**直接连接**设置为空字符串，**以后再说**不做任何更改。

你也可以手动编辑这些文件。lip 在命令需要时读取它们。

## liprc.json

lip v0.x 把配置保存在配置目录的 `liprc.json` 中。如果配置目录中没有 `config.toml` 而有 `liprc.json`，lip 会从中读取 `github_proxy`，写成 `config.toml`，然后删除 `liprc.json`。这只发生一次，在第一条命令读取配置之前。

[试运行](cli.md#试运行)以同样的方式读取 `liprc.json`，但不写 `config.toml`，也保留 `liprc.json`。

## 另请参阅

- [`lip config`](commands/config.md)
- [配置 lip](../guides/configuration.md)：同样的内容，以指南的形式。
- 界面中的[设置](../interface/settings.md)页。
- [文件](files.md)：各系统上这些文件的位置。
