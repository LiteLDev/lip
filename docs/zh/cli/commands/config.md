# lip config

管理配置项。

配置保存在 `config.toml`：Windows 为 `%APPDATA%\lip\config.toml`，Linux/macOS 为 `$XDG_CONFIG_HOME/lip/config.toml`。可用键请参见[配置参考](../configuration.md)。

## lip config get

```shell
lip config get <KEY> [--json]
```

获取并显示指定配置键的值。使用 `--json` 时以 JSON 字符串输出该值，而不是表格。

## lip config set

```shell
lip config set <KEY> <VALUE>
```

将配置键设置为给定值。

## lip config list

```shell
lip config list [--json]
```

列出所有配置键及其值。使用 `--json` 时以 JSON 对象输出。

## lip config delete

```shell
lip config delete <KEY>
```

通过从配置文件中移除该键，将其重置为默认值。
