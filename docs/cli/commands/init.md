# lip init

Initialize a new package by creating a `lip.toml` in the current directory.

## Synopsis

```shell
lip init
```

## Description

Asks on the terminal for the package id, a [Go module path](https://go.dev/ref/mod#module-path) such as `github.com/user/repo`, until the answer is valid, then creates a `lip.toml` (see [lip.toml](../../concepts/lip_toml.md)) with these values:

- `format`: `4`
- `id`: The package id you entered.
- `version`: `0.1.0`
- `variants`: one entry of the variant `""`, with `side = "server"` and `platform = "win-x64"`

If a `lip.toml` already exists in the current directory, the command fails and writes nothing. When standard input or standard error is not a terminal, it cannot ask, so it fails instead.

See [Creating Packages](../../guides/creating_packages.md) for what to do next.
