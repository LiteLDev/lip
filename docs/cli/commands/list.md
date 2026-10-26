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

Displays installed packages in two groups:

- **User-Requested** — Packages explicitly installed by the user.
- **Dependencies** — Packages installed as dependencies.

Packages are topologically sorted by their dependency relationships.
