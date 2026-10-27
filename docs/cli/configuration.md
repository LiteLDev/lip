# Configuration

lip stores its configuration in `config.yaml`.

## File Location

| Platform | Path |
| --- | --- |
| Windows | `%APPDATA%\lip\config.yaml` |
| Linux / macOS | `$XDG_CONFIG_HOME/lip/config.yaml` (typically `~/.config/lip/config.yaml`) |

The file is a YAML mapping of the keys below to their values; a key that is not in the file has its default value. lip creates the file when you first set a value with [`lip config set`](commands/config.md):

```yaml
github_proxy: https://proxy.example.com
```

Earlier versions stored the configuration in `liprc.json` in the same directory. If there is no `config.yaml` but there is a `liprc.json`, lip converts it to `config.yaml` and deletes `liprc.json` the next time it reads the configuration.

## Keys

| Key | Type | Default | Description |
| --- | --- | --- | --- |
| `github_proxy` | `string?` | `null` | Proxy URL for GitHub requests and downloads (git requests and release assets). If a proxied request fails, lip retries it against `github.com` directly. |

Packages are resolved and downloaded directly from their git repositories, so no package registry or module proxy needs to be configured.

## Management

Use [`lip config`](commands/config.md) to manage configuration values.
