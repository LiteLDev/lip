# lip init

Initialize a new project by creating a `manifest.json` in the current directory.

## Synopsis

```shell
lip init
```

## Description

Creates a `manifest.json` package document describing a LeviLamina native mod, with these values:

- `format`: `"bf45b1"`
- `name`: The name of the current directory (characters other than ASCII letters, digits, `.`, `_` and `-` are replaced with `-`).
- `version`: `"0.1.0"`
- `dependencies`: `[]`
- `entry`: `"<name>.dll"`
- `type`: `"native"`

If a `manifest.json` already exists in the current directory, or the resulting name is not a valid package id (for example, it starts with `_` or `-`), the command fails and writes nothing.

See [Creating Packages](../../guides/creating_packages.md) for what to do next.
