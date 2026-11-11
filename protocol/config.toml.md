# config.toml(5)

## NAME

config.toml - the configuration file

## SYNOPSIS

`config.toml`, in the config directory ([lip-userspace(7)](lip-userspace.md)).

## DESCRIPTION

The **configuration** is the TOML file `config.toml` in the config directory, valid against [`config.schema.toml`](config.schema.toml): `format` and a mapping of configuration keys to values. Absent keys take their default; keys that are not configuration keys are ignored. If the file is missing, unreadable or invalid, every key takes its default, unless [lip-compatibility(7), "liprc.json"](lip-compatibility.md#liprcjson) reads a configuration instead.

`lip config set` and `lip config delete` ([lip-config(1)](lip-config.md)) write the file atomically, with `format` and without the keys at their default.

### Keys

| Key | Presence | Value |
|---|---|---|
| `format` | required | `4`. |
| `github_proxy` | optional | A string: a URI. |

The only **configuration key** is `github_proxy`: a URI prefix, default none, through which `lip` sends network requests to GitHub (git repositories and downloads); a request that fails through it is sent to GitHub directly.

## SEE ALSO

[lip-config(1)](lip-config.md), [lip-userspace(7)](lip-userspace.md)
