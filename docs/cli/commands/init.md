# lip init

Initialize a new package by creating a `tooth.json` in the current directory.

## Synopsis

```shell
lip init
```

## Description

Asks on the terminal for the tooth path of the package, a [Go module path](https://go.dev/ref/mod#module-path) such as `github.com/user/repo`, until the answer is valid, then creates a `tooth.json` of format version 4 with these values:

- `format_version`: `4`
- `format_uuid`: `"f74a5fda-cb2a-4de8-959f-a1a76bad3102"`
- `tooth`: The tooth path you entered.
- `version`: `"0.1.0"`
- `variants`: `{ "": [ { "platform": "server", "runtime": "win-x64" } ] }`

If a `tooth.json` already exists in the current directory, the command fails and writes nothing. When standard input or standard error is not a terminal, it cannot ask, so it fails instead. The `Init` method of [`lip rpc`](rpc.md) never asks, so it fails in the same way.

See [Creating Packages](../../guides/creating_packages.md) for what to do next.
