# config.toml(5)

## NAME

config.toml - the configuration files

## SYNOPSIS

`config.toml`, in the config directory ([lip-userspace(7)](lip-userspace.md)).

`.lip/config.toml`, in a workspace ([lip-workspace(7)](lip-workspace.md)).

## DESCRIPTION

There are two **scopes** of configuration, each with its own file and its own configuration keys:

| Scope | File | Schema |
|---|---|---|
| user | `config.toml` in the config directory: the **user configuration**. | [`config.schema.toml`](config.schema.toml) |
| workspace | `.lip/config.toml` in a workspace: the **workspace configuration** of that workspace. | [`workspace-config.schema.toml`](workspace-config.schema.toml) |

Each file is a TOML file, valid against the schema of its scope: `format` and a mapping of configuration keys of its scope to values. Keys that are not configuration keys of its scope are ignored. If the file is missing, unreadable or invalid, every key of its scope is absent, unless, for the user configuration, [lip-compatibility(7), "liprc.json"](lip-compatibility.md#liprcjson) reads a configuration instead.

The **value** of a configuration key is the value the file gives it; when the file does not give it one, it is its default, and a key without a default then has no value.

`lip config set` and `lip config delete` ([lip-config(1)](lip-config.md)) write the file of the scope atomically, with `format` and without the keys at their default and the keys that have no value. Writing `.lip/config.toml` creates `.lip` when it is missing.

### User configuration

| Key | Presence | Value |
|---|---|---|
| `format` | required | `4`. |
| `github_proxy` | optional | A string: a URI, or empty. |
| `language` | optional | `"en"`, `"zh-Hans"` or `"zh-Hant"`. |

The configuration keys of the user scope are `github_proxy` and `language`, both without a default.

`github_proxy` says how `lip` sends **GitHub requests**: its HTTPS requests whose URL has the host `github.com`, or a host ending with `.github.com` or `.githubusercontent.com` (git repositories, downloads and the other requests to GitHub that this protocol names):

- A URI: a GitHub request for the URL U is sent to the URL that is the URI, followed by `/` unless it ends with `/`, followed by U; a request that fails through it is sent to U directly. A URI that ends with `/github.com` or `/https://github.com`, optionally followed by `/`, is read without that ending.
- Empty: `lip` sends them directly.
- No value: `lip` sends them directly. `github_proxy` is then **not configured**; with any value, it is configured.

`language` is the language of the interface ([lip-tui(1), "Language"](lip-tui.md#language)): English with `en`, Simplified Chinese with `zh-Hans`, and Traditional Chinese with `zh-Hant`.

### Workspace configuration

| Key | Presence | Value |
|---|---|---|
| `format` | required | `4`. |
| `restart` | optional | `"never"` or `"on-failure"`. |

The only configuration key of the workspace scope is `restart`, default `never`: whether the server of the workspace is restarted when it exits with an exit code other than 0 ([lip-run(1), "Restarting"](lip-run.md#restarting)): never with `never`, and with `on-failure` as lip-run(1) defines.

## FILES

- `config.toml` in the config directory: the user configuration.
- `.lip/config.toml` in a workspace: the workspace configuration ([lip-workspace(7), "The .lip directory"](lip-workspace.md#the-lip-directory)).

## SEE ALSO

[lip-config(1)](lip-config.md), [lip-tui(1)](lip-tui.md), [lip-userspace(7)](lip-userspace.md), [lip-workspace(7)](lip-workspace.md)
