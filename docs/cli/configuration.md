# Configuration

lip stores its configuration in `liprc.json`.

## File Location

| Platform | Path |
| --- | --- |
| Windows | `%APPDATA%\lip\liprc.json` |
| Linux / macOS | `$XDG_CONFIG_HOME/lip/liprc.json` (typically `~/.config/lip/liprc.json`) |

The file is created automatically with default values when lip first runs.

## Keys

| Key | Type | Default | Description |
| --- | --- | --- | --- |
| `github_proxy` | `string?` | `null` | Proxy URL for GitHub requests and downloads. |

Packages are resolved and downloaded directly from their git repositories, so no package registry or module proxy needs to be configured. The former `go_module_proxy` key has been removed; if it is still present in an existing `liprc.json`, it is ignored and dropped the next time the file is saved.

## Management

Use [`lip config`](commands/config.md) to manage configuration values.
