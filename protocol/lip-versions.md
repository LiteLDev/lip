# lip-versions(1)

## NAME

lip-versions - list the versions of a remote repository

## SYNOPSIS

```
lip versions <PACKAGE> [--json] [-h | --help]
```

## DESCRIPTION

`<PACKAGE>` is a package argument ([lip(1), "Package arguments"](lip.md#package-arguments)) that must be a remote repository without a ref; otherwise the command fails. Its variant does not affect the result.

The result is the versions of the version tags of the repository, in ascending [SemVer](https://semver.org/spec/v2.0.0.html) precedence, each once. `lip versions` changes nothing.

## OPTIONS

- `--json`: The result is written as JSON ([lip(1), "Output"](lip.md#output)), as a JSON array of strings.

## SEE ALSO

[lip(1)](lip.md), [lip-package(7)](lip-package.md)
