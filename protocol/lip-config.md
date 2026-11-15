# lip-config(1)

## NAME

lip-config - read and write the configuration

## SYNOPSIS

```
lip config get <KEY> [--workspace] [--json] [-h | --help]
lip config set <KEY> <VALUE> [--workspace] [-h | --help]
lip config list [--workspace] [--json] [-h | --help]
lip config delete <KEY> [--workspace] [-h | --help]
```

## DESCRIPTION

The commands of the command group `config` read and write the configuration of one scope ([config.toml(5)](config.toml.md)): the user configuration, or with `--workspace` the workspace configuration of the workspace ([lip(1), "Working directory"](lip.md#working-directory)).

`<KEY>` is a configuration key of that scope. When it is not one, the command fails.

- `lip config get`: The result is the value of `<KEY>`, or that it has no value. The command changes nothing.
- `lip config set`: Sets `<KEY>` to `<VALUE>`, rewriting the file of the scope as [config.toml(5)](config.toml.md) defines. `<VALUE>` may be empty. When `<VALUE>` is not a valid value of `<KEY>`, the command fails and changes nothing.
- `lip config list`: The result is every configuration key of the scope with its value. The command changes nothing.
- `lip config delete`: Removes `<KEY>` from the file of the scope, so that it takes its default or, without one, has no value, rewriting the file as [config.toml(5)](config.toml.md) defines.

## OPTIONS

- `--workspace`: The scope is the workspace configuration instead of the user configuration.
- `--json`: The result is written as JSON ([lip(1), "Output"](lip.md#output)): for `lip config get`, the value as a JSON string, or `null` when the key has no value; for `lip config list`, a JSON object that maps each configuration key of the scope to its value in the same way.

## SEE ALSO

[lip(1)](lip.md), [config.toml(5)](config.toml.md)
