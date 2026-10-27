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

Reads the workspace's [lockfile](../../concepts/lockfiles.md) (or a `lip_lock.json` or legacy `tooth_lock.json` left in the workspace by earlier versions) and displays the installed packages as `<name>@<version>` in two groups:

- **User-Requested** — Packages explicitly installed by the user.
- **Dependencies** — Packages installed as dependencies.

Packages are listed in install order, dependencies first. The name is the package's identity: the `name` of its `manifest.json`, or the `tooth` path of a `tooth.json` package.
