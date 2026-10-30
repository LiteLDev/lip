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

Reads the workspace's [lockfile](../../concepts/lockfiles.md) (or a lock left by earlier versions: in the per-user data directory, or as `lip_lock.json` or legacy `tooth_lock.json` in the workspace) and displays the installed packages as `<name>@<version>` in two groups:

- **User-Requested** — Packages explicitly installed by the user.
- **Dependencies** — Packages installed as dependencies.

Packages are listed in install order, dependencies first. The name is the package's identity: the `name` of its `manifest.json`, or for a `tooth.json` package its `tooth` path, followed by `#<label>` for a labeled variant (e.g. `github.com/LiteLDev/LegacyScriptEngine#lua`).
