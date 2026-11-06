# Configuration

lip stores its configuration in `config.yaml`.

## File Location

| Platform | Path |
| --- | --- |
| Windows | `%APPDATA%\lip\config.yaml` |
| Linux / macOS | `$XDG_CONFIG_HOME/lip/config.yaml` (typically `~/.config/lip/config.yaml`) |

The file is a YAML mapping of `format: 4` and the keys below to their values, described by the JSON Schema [`config.schema.yaml`](https://github.com/LiteLDev/lip/blob/main/protocol/config.schema.yaml); a key that is not in the file has its default value. lip creates the file when you first set a value with [`lip config set`](commands/config.md):

```yaml
format: 4
github_proxy: https://proxy.example.com
```

A `liprc.json` of lip v0.x in the same directory, when there is no `config.yaml`, is converted to `config.yaml` automatically, and then deleted.

## Keys

| Key | Type | Default | Description |
| --- | --- | --- | --- |
| `github_proxy` | `string?` | `null` | Proxy URL for GitHub requests and downloads (git requests and release assets). If a proxied request fails, lip retries it against `github.com` directly. |

Packages are resolved and downloaded directly from their git repositories, so no package registry or module proxy needs to be configured.

## Management

Use [`lip config`](commands/config.md) to manage configuration values.
