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

Reads the workspace's [lockfile](../../concepts/lockfiles.md) (or the legacy `tooth_lock.json` of lip v0.x) and displays the installed packages as `<name>@<version>` in two groups:

- **User-Requested** — Packages explicitly installed by the user.
- **Dependencies** — Packages installed as dependencies.

Packages are listed in install order, dependencies first. The name is the package's identity: the `id` of its `manifest.json` (the `name` of a LeviLamina manifest), or for a `tooth.json` package its tooth name, the last element of its `tooth` path, followed by `-<label>` for a labeled variant (e.g. `LegacyScriptEngine-lua`).
