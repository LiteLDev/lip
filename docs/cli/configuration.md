# Configuration

lip stores its configuration in `config.toml`.

## File Location

| Platform | Path |
| --- | --- |
| Windows | `%APPDATA%\lip\config.toml` |
| Linux / macOS | `$XDG_CONFIG_HOME/lip/config.toml` (typically `~/.config/lip/config.toml`) |

The file is a [TOML 1.0](https://toml.io/en/v1.0.0) file of `format = 4` and the keys below with their values, described by the JSON Schema [`config.schema.toml`](https://github.com/LiteLDev/lip/blob/main/protocol/config.schema.toml), written as TOML; a key that is not in the file has its default value. lip creates the file when you first set a value with [`lip config set`](commands/config.md):

```toml
format = 4
github_proxy = "https://proxy.example.com"
```

A `liprc.json` of lip v0.x in the same directory, when there is no `config.toml`, is converted to `config.toml` automatically, and then deleted.

## Keys

| Key | Type | Default | Description |
| --- | --- | --- | --- |
| `github_proxy` | `string?` | `null` | Proxy URL for GitHub requests and downloads (git requests and release assets). If a proxied request fails, lip retries it against `github.com` directly. Must be a URI, such as `https://proxy.example.com/`: `lip config set` rejects any other value, and a `config.toml` holding another value is invalid, so every key takes its default. |

Packages are resolved and downloaded directly from their git repositories, so no package registry or module proxy needs to be configured.

## Management

Use [`lip config`](commands/config.md) to manage configuration values.
