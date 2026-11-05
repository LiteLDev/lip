# lip list

List installed packages.

## Synopsis

```shell
lip list [options]
```

## Options

| Option | Description |
| --- | --- |
| `--json` | Output as JSON. |

## Description

Reads the workspace's [workspace file](../../concepts/lockfiles.md) and displays the installed packages as `<identity>@<version>` in two groups:

- **User-Requested** — Packages explicitly installed by the user.
- **Dependencies** — Packages installed as dependencies.

Packages are listed in installation order, dependencies first. The identity of a package is its tooth path, followed by `#<variant>` for a variant other than `""` (e.g. `github.com/LiteLDev/LegacyScriptEngine#lua`).

With `--json`, the result is:

```json
{ "ExplicitInstalled": ["<identity>@<version>", ...], "ImplicitInstalled": ["<identity>@<version>", ...] }
```
