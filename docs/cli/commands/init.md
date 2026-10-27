# lip init

Initialize a new project by creating a `manifest.json` in the current directory.

## Synopsis

```shell
lip init
```

## Description

Creates a format 4 `manifest.json` describing a LeviLamina native mod, with these values:

- `name`: The name of the current directory (characters other than letters, digits, `.`, `_` and `-` are replaced with `-`).
- `entry`: `"<name>.dll"`
- `type`: `"native"`
- `version`: `"0.1.0"`
- `dependencies`: `[]`

If a `manifest.json` already exists in the current directory, the command fails and the file is left unchanged.

See [Creating Packages](../../guides/creating_packages.md) for what to do next.
