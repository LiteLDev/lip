# Configuration

This page lists every setting of lip: the two configuration files, their format, each key with its values, default and effect, and the ways to change them.

## The two files

| Scope | File | Applies to | Changed with |
| --- | --- | --- | --- |
| User | `config.toml` in the [config directory](files.md#config-directory) | Everything lip does for you. | `lip config ...` |
| Workspace | `.lip/config.toml` in a workspace | That workspace. | `lip config ... --workspace`, run in the workspace |

The config directory is `%APPDATA%\lip` on Windows and `$XDG_CONFIG_HOME/lip` elsewhere, which is `~/.config/lip` by default.

Each scope has its own keys. A key of one scope is not a key of the other: `lip config get restart` without `--workspace` fails.

## Format

Each file is a [TOML 1.0](https://toml.io/en/v1.0.0) file with `format = 4` and the keys of its scope:

::: code-group

```toml [config.toml]
format = 4
github_proxy = "https://proxy.example.com/"
language = "zh-Hans"
```

```toml [.lip/config.toml]
format = 4
restart = "on-failure"
```

:::

- A key that is not in the file has its default. A key without a default then has **no value**.
- Keys that are not keys of the scope are ignored.
- A file that is missing, unreadable or invalid counts as empty: every key of its scope is absent. A value that the key does not accept makes the whole file invalid.
- lip writes a file only when you change a value. It writes `format` and the keys you have set, and leaves out the keys at their default and the keys without a value.
- Writing `.lip/config.toml` creates `.lip` when it is missing. It takes no lock, so it works while the server runs.

The files are described by the JSON Schemas [`config.schema.toml`](https://github.com/LiteLDev/lip/blob/main/protocol/config.schema.toml) and [`workspace-config.schema.toml`](https://github.com/LiteLDev/lip/blob/main/protocol/workspace-config.schema.toml), written as TOML.

## User configuration

| Key | Values | Default |
| --- | --- | --- |
| `github_proxy` | A URI, or the empty string. | none |
| `language` | `en`, `zh-Hans` or `zh-Hant`. | none |

### github_proxy

How lip sends its network requests to GitHub: git repositories and downloads.

| Value | Effect |
| --- | --- |
| A URI, such as `https://proxy.example.com/` | lip sends the requests through this URI prefix. A request that fails through it is sent to GitHub directly. |
| The empty string (`lip config set github_proxy ""`) | lip sends the requests to GitHub directly. |
| No value (`lip config delete github_proxy`) | lip sends the requests to GitHub directly, and `github_proxy` is **not configured**. |

The last two differ only for the interface: while `github_proxy` is not configured, the interface checks at start whether it reaches GitHub, and asks you how to reach it when it does not. With any value, the empty string included, it does not ask.

[`lip self-update`](commands/self_update.md) reads the list of releases and the checksums from GitHub directly, whatever `github_proxy` is, and downloads only the update itself through it.

### language

The language of the [interface](interface.md): English with `en`, Simplified Chinese with `zh-Hans`, Traditional Chinese with `zh-Hant`.

With no value, the interface follows the language of your system: the display language on Windows, and the first of `LC_ALL`, `LC_MESSAGES` and `LANG` that names a language elsewhere. It is in English when that names neither English nor Chinese.

`language` changes the interface only. The messages and results of commands are in English.

## Workspace configuration

| Key | Values | Default |
| --- | --- | --- |
| `restart` | `never` or `on-failure`. | `never` |

### restart

Whether the server of the workspace is restarted when it exits with an exit code other than 0.

| Value | Effect |
| --- | --- |
| `never` | The server is not restarted. |
| `on-failure` | lip writes a message, waits 5 seconds, and starts the server again; at most 5 times within 10 minutes. A server that you asked to stop is not restarted. |

lip reads `restart` each time the server exits, so you can change it while the server runs. See [`lip run`](commands/run.md#restarting).

## Changing the configuration

With commands, see [`lip config`](commands/config.md):

```shell
lip config list
lip config set github_proxy https://proxy.example.com/
lip config delete github_proxy
lip config set restart on-failure --workspace
```

`lip config set` fails and changes nothing when the value is not one the key accepts.

In the interface, the [Settings](../interface/settings.md) page changes the same files:

| In Settings | Key | Does the same as |
| --- | --- | --- |
| Language | `language` | `lip config set language <value>`; **Follow system** is `lip config delete language`. |
| GitHub proxy | `github_proxy` | `lip config set github_proxy <URI>`; **Direct** sets the empty string; **Reset** is `lip config delete github_proxy`. |
| Auto restart | `restart` of the selected workspace | `lip config set restart on-failure --workspace` when on, `never` when off. |

The interface also writes `github_proxy` when you answer its question about reaching GitHub: a proxy sets the URI, **Connect directly** sets the empty string, and **Later** changes nothing.

You can edit the files by hand too. lip reads them when a command needs them.

## liprc.json

lip v0.x kept its configuration in `liprc.json` in the config directory. When the config directory has no `config.toml` and has a `liprc.json`, lip reads `github_proxy` from it, writes it as `config.toml`, and deletes `liprc.json`. This happens once, before the first command reads the configuration.

A [dry run](cli.md#dry-run) reads `liprc.json` the same way, but writes no `config.toml` and keeps `liprc.json`.

## See also

- [`lip config`](commands/config.md)
- [Configuring lip](../guides/configuration.md): the same, as a guide.
- [Settings](../interface/settings.md) in the interface.
- [Files](files.md): where the files are on each system.
