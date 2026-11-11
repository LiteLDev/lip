# lip-update(1)

## NAME

lip-update - update installed packages

## SYNOPSIS

```
lip update [<PACKAGES>...] [-n | --dry-run] [--ignore-scripts] [--platform <RID>] [-h | --help]
```

## DESCRIPTION

Each of `<PACKAGES>` is a package argument ([lip(1), "Package arguments"](lip.md#package-arguments)).

With operands, `lip update` makes the request of each package argument again, as [lip-workspace(7), "Operations"](lip-workspace.md#operations) defines updating for its source. Every requested package must already be installed; otherwise the command fails. The requested packages become explicitly installed.

Without operands, `lip update` updates every explicitly installed package as lip-workspace(7), "Operations" defines, except that a package whose source is a local path that no longer exists is kept as it is.

A package is replaced only as lip-workspace(7), "Operations" defines. Dependency packages are updated only as far as [lip-workspace(7), "Valid states"](lip-workspace.md#valid-states) requires. Updating a package installed from a source with a ref, by giving that source without the ref, moves the package back to the version tags.

## OPTIONS

- `-n`, `--dry-run`: A dry run ([lip(1), "Dry run"](lip.md#dry-run)). The messages name the packages that the operation would install, uninstall or mark explicitly installed.
- `--ignore-scripts`: The operation runs no hook command. Nothing else changes.
- `--platform <RID>`: The platform of the environment ([lip(1), "Environment"](lip.md#environment)) is `<RID>`.

## SEE ALSO

[lip(1)](lip.md), [lip-install(1)](lip-install.md), [lip-workspace(7)](lip-workspace.md)
