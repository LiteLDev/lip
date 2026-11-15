# lip config

Manage configuration values.

There are two configuration files, each with its own keys:

- The **user configuration**, `config.toml` at `%APPDATA%\lip\config.toml` (Windows) or `$XDG_CONFIG_HOME/lip/config.toml` (Linux/macOS). The commands use it by default.
- The **workspace configuration**, `.lip/config.toml` in the current directory. The commands use it with `--workspace`.

See [Configuration Reference](../configuration.md) for the available keys. A key that is not a key of the chosen file is an error.

## lip config get

```shell
lip config get <KEY> [--workspace] [--json]
```

Gets and displays the value of a configuration key. With `--json`, prints the value as a JSON string instead of a table, or `null` when the key has no value (it is not in the file and has no default).

## lip config set

```shell
lip config set <KEY> <VALUE> [--workspace]
```

Sets a configuration key to the given value. The value may be empty (`""`), as in `lip config set github_proxy ""`. A value that the key does not accept is an error, and nothing is changed.

## lip config list

```shell
lip config list [--workspace] [--json]
```

Lists all configuration keys and their values. With `--json`, prints them as a JSON object, with `null` for a key that has no value.

## lip config delete

```shell
lip config delete <KEY> [--workspace]
```

Removes a configuration key from the configuration file, so that it takes its default value, or has no value when it has no default.

## Options

| Option | Description |
| --- | --- |
| `--workspace` | Use the workspace configuration, `.lip/config.toml` in the current directory, instead of the user configuration. |
| `--json` | Print the result as JSON (`get` and `list`). |
