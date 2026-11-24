# lip-update(1)

## NAME

lip-update - update installed packages

## SYNOPSIS

```
lip update [<PACKAGES>...] [--accept-rosetta-license] [-n | --dry-run] [-h | --help]
```

## DESCRIPTION

Each of `<PACKAGES>` is a package argument ([lip(1), "Package arguments"](lip.md#package-arguments)).

With operands, `lip update` makes the request of each package argument again, as [lip-workspace(7), "Operations"](lip-workspace.md#operations) defines updating for its source. Every requested package must already be installed; otherwise the command fails. The requested packages become explicitly installed.

Without operands, `lip update` updates every explicitly installed package as lip-workspace(7), "Operations" defines, except that a package whose source is a local path that no longer exists is kept as it is.

A package is replaced only as lip-workspace(7), "Operations" defines. Dependency packages are updated only as far as [lip-workspace(7), "Valid states"](lip-workspace.md#valid-states) requires. Updating a package installed from a source with a ref, by giving that source without the ref, moves the package back to the version tags.

`lip update` keeps the game and the loader ([lip-setup(1), "Terms"](lip-setup.md#terms)) as they are, and installs, uninstalls and replaces no package of them: without operands, it does not request them again; an operand whose source is a remote repository whose location is `github.com/LiteLDev/bds` or `github.com/LiteLDev/LeviLamina`, compared ASCII case-insensitively, fails the command before anything changes, and its message should name `lip setup`. When the requests cannot be satisfied with the game and the loader as they are, the command fails.

## OPTIONS

- `--accept-rosetta-license`: The user accepts the license of Rosetta 2, so that preparing the system installs Rosetta 2 without asking ([lip-workspace(7), "System dependencies"](lip-workspace.md#system-dependencies)).
- `-n`, `--dry-run`: A dry run ([lip(1), "Dry run"](lip.md#dry-run)). The messages name the packages that the operation would install, uninstall or mark explicitly installed.

## SEE ALSO

[lip(1)](lip.md), [lip-install(1)](lip-install.md), [lip-setup(1)](lip-setup.md), [lip-workspace(7)](lip-workspace.md)
