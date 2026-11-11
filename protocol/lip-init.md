# lip-init(1)

## NAME

lip-init - create a package file

## SYNOPSIS

```
lip init [-h | --help]
```

## DESCRIPTION

`lip init` writes `lip.toml` in the current working directory: a package file ([lip.toml(5)](lip.toml.md)) with exactly the members of `{ "format": 4, "id": "<id>", "version": "0.1.0", "variants": { "": [ { "side": "server", "platform": "win-x64" } ] } }`. `lip` asks the user on the terminal for `<id>` until the answer is a [Go module path](https://pkg.go.dev/golang.org/x/mod/module#CheckPath). When standard input or standard error is not a terminal, the command fails instead.

When `lip.toml` exists, the command fails and writes nothing.

## SEE ALSO

[lip(1)](lip.md), [lip.toml(5)](lip.toml.md)
