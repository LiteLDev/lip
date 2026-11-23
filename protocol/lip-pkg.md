# lip-pkg(1)

## NAME

lip-pkg - develop packages

## SYNOPSIS

```
lip pkg init [-h | --help]
lip pkg migrate [-h | --help]
```

## DESCRIPTION

The commands of the command group `pkg` create and migrate packages ([lip-package(7)]). None of them is a workspace operation.

### `lip pkg init`

`lip pkg init` writes `lip.toml` in the current working directory: a package file ([lip.toml(5)]) with exactly the members of `{ "format": 4, "id": "<id>", "version": "0.1.0", "variants": { "": [ { "side": "server", "platform": "win-x64" } ] } }`. `lip` asks the user on the terminal for `<id>` until the answer is a [Go module path]. When standard input or standard error is not a terminal, the command fails instead.

When `lip.toml` exists, the command fails and writes nothing.

### `lip pkg migrate`

`lip pkg migrate` migrates the current working directory as [lip-compatibility(7), "lip pkg migrate"](lip-compatibility.md#lip-pkg-migrate) defines.

## SEE ALSO

[lip(1)](lip.md), [lip.toml(5)], [lip-package(7)], [lip-compatibility(7)](lip-compatibility.md)

[lip.toml(5)]: lip.toml.md
[lip-package(7)]: lip-package.md
[Go module path]: https://pkg.go.dev/golang.org/x/mod/module#CheckPath
