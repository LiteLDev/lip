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

Reads the [lockfile](../../concepts/lockfiles.md) `lip_lock.json` (or a legacy `tooth_lock.json`) and displays the installed packages as `<name>@<version>` in two groups:

- **User-Requested** — Packages explicitly installed by the user.
- **Dependencies** — Packages installed as dependencies.

Packages are listed in install order, dependencies first. The name is the package's identity: the `name` of its `manifest.json`, or the `tooth` path of a `tooth.json` package.
