# lip config

Read and write the configuration.

## Synopsis

```
lip config get <KEY> [--workspace] [--json] [-h | --help]
lip config set <KEY> <VALUE> [--workspace] [-h | --help]
lip config list [--workspace] [--json] [-h | --help]
lip config delete <KEY> [--workspace] [-h | --help]
```

## Description

The commands of the group `config` read and write the configuration of one scope:

| Scope | File | Keys |
| --- | --- | --- |
| User (the default) | `config.toml` in the config directory | `github_proxy`, `language` |
| Workspace (with `--workspace`) | `.lip/config.toml` in the current directory | `restart` |

[Configuration](../configuration.md) describes every key and its values.

`<KEY>` must be a key of the chosen scope. When it is not, the command fails.

| Command | Does |
| --- | --- |
| `lip config get <KEY>` | Writes the value of `<KEY>`, or that it has no value. Changes nothing. |
| `lip config set <KEY> <VALUE>` | Sets `<KEY>` to `<VALUE>`. When `<VALUE>` is not a value that the key accepts, the command fails and changes nothing. |
| `lip config list` | Writes every key of the scope with its value. Changes nothing. |
| `lip config delete <KEY>` | Removes `<KEY>` from the file, so that it takes its default, or has no value when it has no default. |

`<VALUE>` may be empty, written as `""` in a shell: `lip config set github_proxy ""`. The empty value is given as an operand; an option value can never be empty.

`set` and `delete` rewrite the whole file of the scope: `format = 4` and the keys that have a value other than their default. With `--workspace`, they create `.lip` when it is missing. They take no workspace lock, so they work while the server runs.

## Options

| Option | Description |
| --- | --- |
| `--workspace` | Use the workspace configuration, `.lip/config.toml` in the current directory, instead of the user configuration. |
| `--json` | Of `get` and `list`: write the result as JSON. |
| `-h`, `--help` | Write the help and exit. |

## Output

`get` and `list` write their result to standard output. `set` and `delete` have no result; their final line is a message on standard error.

Without `--json`, the result is a table for people. A key without a value shows `(not set)`, and an empty value shows `(empty)`:

```
┌──────────────┬───────────┐
│ Key          │ Value     │
├──────────────┼───────────┤
│ github_proxy │ (not set) │
│ language     │ zh-Hans   │
└──────────────┴───────────┘
```

With `--json`:

| Command | Result |
| --- | --- |
| `lip config get <KEY> --json` | The value as a JSON string, or `null` when the key has no value. |
| `lip config list --json` | A JSON object that maps each key of the scope to its value, in the same way. |

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

## Exit status

| Code | Meaning |
| --- | --- |
| `0` | The command succeeded. `get` of a key without a value succeeds too. |
| not `0` | A usage error, a key that is not a key of the scope, a value that the key does not accept, or a file that cannot be written. |

## Examples

Send GitHub requests through a proxy, then go back to asking:

```shell
lip config set github_proxy https://proxy.example.com/
lip config delete github_proxy
```

Reach GitHub directly, and stop the interface from asking:

```shell
lip config set github_proxy ""
```

Set the language of the interface:

```shell
lip config set language zh-Hans
```

Restart the server of this workspace when it crashes:

```shell
lip config set restart on-failure --workspace
```

## See also

- [Configuration](../configuration.md): every key.
- [Configuring lip](../../guides/configuration.md)
- [Settings](../../interface/settings.md): the same settings in the interface.
