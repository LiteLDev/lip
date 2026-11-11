# lip-config(1)

## NAME

lip-config - read and write the configuration

## SYNOPSIS

```
lip config get <KEY> [--json] [-h | --help]
lip config set <KEY> <VALUE> [-h | --help]
lip config list [--json] [-h | --help]
lip config delete <KEY> [-h | --help]
```

## DESCRIPTION

The commands of the command group `config` read and write the configuration ([config.toml(5)](config.toml.md)).

`<KEY>` is a configuration key. When it is not one, the command fails.

- `lip config get`: The result is the value of `<KEY>`. The command changes nothing.
- `lip config set`: Sets `<KEY>` to `<VALUE>`, rewriting the configuration as [config.toml(5)](config.toml.md) defines.
- `lip config list`: The result is every configuration key with its value. The command changes nothing.
- `lip config delete`: Resets `<KEY>` to its default value, rewriting the configuration as [config.toml(5)](config.toml.md) defines.

## OPTIONS

- `--json`: The result is written as JSON ([lip(1), "Output"](lip.md#output)): for `lip config get`, as a JSON string; for `lip config list`, as a JSON object that maps each configuration key to its value as a string.

## SEE ALSO

[lip(1)](lip.md), [config.toml(5)](config.toml.md)
