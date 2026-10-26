# lip config

Manage configuration values.

Configuration is stored in `liprc.json` at `%APPDATA%\lip\liprc.json` (Windows) or `$XDG_CONFIG_HOME/lip/liprc.json` (Linux/macOS). See [Configuration Reference](../configuration.md) for available keys.

## lip config get

```shell
lip config get <KEY> [--json]
```

Gets and displays the value of a configuration key. With `--json`, prints the bare value instead of a table.

## lip config set

```shell
lip config set <KEY> <VALUE>
```

Sets a configuration key to the given value.

## lip config list

```shell
lip config list [--json]
```

Lists all configuration keys and their values. With `--json`, prints them as a JSON object.

## lip config delete

```shell
lip config delete <KEY>
```

Resets a configuration key to its default value by removing it from the configuration file.
