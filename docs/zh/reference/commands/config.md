# lip config

读写配置。

## 概要

```
lip config get <KEY> [--workspace] [--json] [-h | --help]
lip config set <KEY> <VALUE> [--workspace] [-h | --help]
lip config list [--workspace] [--json] [-h | --help]
lip config delete <KEY> [--workspace] [-h | --help]
```

## 说明

`config` 组的命令读写某一个作用域的配置：

| 作用域 | 文件 | 键 |
| --- | --- | --- |
| 用户（默认） | 配置目录中的 `config.toml` | `github_proxy`、`language` |
| 工作区（带 `--workspace`） | 当前目录中的 `.lip/config.toml` | `restart` |

[配置](../configuration.md)说明了每个键及其取值。

`<KEY>` 必须是所选作用域的键。不是的话，命令失败。

| 命令 | 作用 |
| --- | --- |
| `lip config get <KEY>` | 输出 `<KEY>` 的值，或说明它没有值。不做任何更改。 |
| `lip config set <KEY> <VALUE>` | 把 `<KEY>` 设为 `<VALUE>`。`<VALUE>` 不是该键接受的值时，命令失败，不做任何更改。 |
| `lip config list` | 输出该作用域的每个键及其值。不做任何更改。 |
| `lip config delete <KEY>` | 从文件中移除 `<KEY>`，使它取默认值；没有默认值时，它就没有值。 |

`<VALUE>` 可以为空，在 shell 中写作 `""`：`lip config set github_proxy ""`。这个空值是作为操作数给出的；选项的值则永远不能为空。

`set` 和 `delete` 会重写该作用域的整个文件：`format = 4`，加上值不同于默认值的键。带 `--workspace` 时，`.lip` 不存在就创建它。它们不取得工作区锁，所以服务器运行时也能使用。

## 选项

| 选项 | 说明 |
| --- | --- |
| `--workspace` | 使用工作区配置，即当前目录中的 `.lip/config.toml`，而不是用户配置。 |
| `--json` | 用于 `get` 和 `list`：以 JSON 输出结果。 |
| `-h`、`--help` | 输出帮助并退出。 |

## 输出

`get` 和 `list` 把结果写到标准输出。`set` 和 `delete` 没有结果；它们的最后一行是写到标准错误的消息。

不带 `--json` 时，结果是一张给人看的表。没有值的键显示 `(not set)`，空值显示 `(empty)`：

```
┌──────────────┬───────────┐
│ Key          │ Value     │
├──────────────┼───────────┤
│ github_proxy │ (not set) │
│ language     │ zh-Hans   │
└──────────────┴───────────┘
```

带 `--json` 时：

| 命令 | 结果 |
| --- | --- |
| `lip config get <KEY> --json` | 值，作为 JSON 字符串；键没有值时为 `null`。 |
| `lip config list --json` | 一个 JSON 对象，以同样的方式把该作用域的每个键映射到它的值。 |

```shell
lip config get restart --workspace --json
```

```json
"never"
```

```shell
lip config list --json
```

```json
{
  "github_proxy": null,
  "language": "zh-Hans"
}
```

## 退出状态

| 退出码 | 含义 |
| --- | --- |
| `0` | 命令成功。对没有值的键执行 `get` 也算成功。 |
| 非 `0` | 用法错误、键不属于该作用域、值不被该键接受，或文件无法写入。 |

## 示例

通过代理发送 GitHub 请求，之后再恢复为询问：

```shell
lip config set github_proxy https://proxy.example.com/
lip config delete github_proxy
```

直接连接 GitHub，并让界面不再询问：

```shell
lip config set github_proxy ""
```

设置界面的语言：

```shell
lip config set language zh-Hans
```

让这个工作区的服务器在崩溃时重启：

```shell
lip config set restart on-failure --workspace
```

## 另请参阅

- [配置](../configuration.md)：每一个键。
- [配置 lip](../../guides/configuration.md)
- [设置](../../interface/settings.md)：界面中同样的设置。
