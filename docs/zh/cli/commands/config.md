# lip config

管理配置项。

配置有两个文件，各有自己的配置键：

- **用户配置**：`config.toml`，Windows 为 `%APPDATA%\lip\config.toml`，Linux/macOS 为 `$XDG_CONFIG_HOME/lip/config.toml`。命令默认使用它。
- **工作区配置**：当前目录中的 `.lip/config.toml`。命令加上 `--workspace` 时使用它。

可用键请参见[配置参考](../configuration.md)。不属于所选文件的键会报错。

## lip config get

```shell
lip config get <KEY> [--workspace] [--json]
```

获取并显示指定配置键的值。使用 `--json` 时以 JSON 字符串输出该值，而不是表格；该键没有值（不在文件中且没有默认值）时输出 `null`。

## lip config set

```shell
lip config set <KEY> <VALUE> [--workspace]
```

将配置键设置为给定值。值可以为空（`""`），例如 `lip config set github_proxy ""`。该键不接受的值会报错，且不做任何更改。

## lip config list

```shell
lip config list [--workspace] [--json]
```

列出所有配置键及其值。使用 `--json` 时以 JSON 对象输出，没有值的键为 `null`。

## lip config delete

```shell
lip config delete <KEY> [--workspace]
```

从配置文件中移除该键，使其取默认值；没有默认值时，它就没有值。

## 选项

| 选项 | 说明 |
| --- | --- |
| `--workspace` | 使用工作区配置（当前目录中的 `.lip/config.toml`），而不是用户配置。 |
| `--json` | 以 JSON 输出结果（`get` 与 `list`）。 |
