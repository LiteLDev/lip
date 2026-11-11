# lip-view(1)

## NAME

lip-view - show the package file of a package

## SYNOPSIS

```
lip view <PACKAGE> [-h | --help]
```

## DESCRIPTION

`<PACKAGE>` is a package argument ([lip(1), "Package arguments"](lip.md#package-arguments)).

The result is the package file of the first archive that the source of `<PACKAGE>` names ([lip-package(7), "Resolution"](lip-package.md#resolution), step 1), as it is read for the environment of [lip(1), "Environment"](lip.md#environment), written as JSON followed by a line break. `lip view` has no `--json` option, and changes nothing.

## SEE ALSO

[lip(1)](lip.md), [lip.toml(5)](lip.toml.md), [lip-package(7)](lip-package.md)
