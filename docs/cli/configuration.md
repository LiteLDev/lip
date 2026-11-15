# Configuration

lip has two configuration files: the user configuration, which applies to everything lip does for you, and the workspace configuration, which applies to one workspace.

## User Configuration

### File Location

| Platform | Path |
| --- | --- |
| Windows | `%APPDATA%\lip\config.toml` |
| Linux / macOS | `$XDG_CONFIG_HOME/lip/config.toml` (typically `~/.config/lip/config.toml`) |

The file is a [TOML 1.0](https://toml.io/en/v1.0.0) file of `format = 4` and the keys below with their values, described by the JSON Schema [`config.schema.toml`](https://github.com/LiteLDev/lip/blob/main/protocol/config.schema.toml), written as TOML; a key that is not in the file has its default value, or no value when it has no default. lip creates the file when you first set a value with [`lip config set`](commands/config.md):

```toml
format = 4
github_proxy = "https://proxy.example.com"
```

A `liprc.json` of lip v0.x in the same directory, when there is no `config.toml`, is converted to `config.toml` automatically, and then deleted.

### Keys

| Key | Type | Default | Description |
| --- | --- | --- | --- |
| `github_proxy` | `string?` | none | How lip reaches GitHub for git requests and downloads (release assets). A URI, such as `https://proxy.example.com/`: lip sends them through this proxy, and if a proxied request fails, retries it against `github.com` directly. An empty string (`lip config set github_proxy ""`): lip connects to GitHub directly. Not set (`lip config delete github_proxy`): lip connects to GitHub directly too, but the key counts as not configured. `lip config set` rejects any other value, and a `config.toml` holding another value is invalid, so every key takes its default. |
| `language` | `"en"`, `"zh-Hans"` or `"zh-Hant"` | none | The language of the [interactive interface](tui.md#languages): English, Simplified Chinese or Traditional Chinese. Not set (`lip config delete language`): the interface follows the language of your system, and is in English when that is neither English nor Chinese. Commands always write in English. |

Packages are resolved and downloaded directly from their git repositories, so no package registry or module proxy needs to be configured.

## Workspace Configuration

The workspace configuration is `.lip/config.toml` in a workspace, a TOML file of `format = 4` and the keys below, described by [`workspace-config.schema.toml`](https://github.com/LiteLDev/lip/blob/main/protocol/workspace-config.schema.toml). Manage it with `lip config --workspace` in the workspace:

```toml
format = 4
restart = "on-failure"
```

### Keys

| Key | Type | Default | Description |
| --- | --- | --- | --- |
| `restart` | `"never"` or `"on-failure"` | `"never"` | Whether the server of the workspace is restarted when it exits with a non-zero exit code: with `on-failure`, after 5 seconds, at most 5 times within 10 minutes (see [`lip run`](commands/run.md#restarting)). |

## Management

Use [`lip config`](commands/config.md) to manage configuration values, or the [interactive interface](tui.md): its [Settings](tui.md#settings) page for the user configuration, and its [Server](tui.md#server) page for `restart`.
