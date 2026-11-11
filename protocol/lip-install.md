# lip-install(1)

## NAME

lip-install - install packages into the workspace

## SYNOPSIS

```
lip install <PACKAGES>... [-n | --dry-run] [--ignore-scripts] [--no-dependencies] [--platform <RID>] [-h | --help]
```

## DESCRIPTION

Each of `<PACKAGES>` is a package argument ([lip(1), "Package arguments"](lip.md#package-arguments)).

Without `--no-dependencies`, `lip install` installs a set of packages that satisfies [lip-workspace(7), "Valid states"](lip-workspace.md#valid-states) for the requests of the package arguments and the requests that made the installed explicitly installed packages:

- The requested packages, including those already installed as dependency packages, become explicitly installed.
- An installed package keeps its version when that satisfies lip-workspace(7), "Valid states"; otherwise a dependency package may change version. Each dependency package that no explicitly installed package needs is uninstalled.
- When a requested package is already explicitly installed, or two package arguments request the same identity, the command fails.

## OPTIONS

- `-n`, `--dry-run`: A dry run ([lip(1), "Dry run"](lip.md#dry-run)). The messages name the packages that the operation would install, uninstall or mark explicitly installed.
- `--ignore-scripts`: The operation runs no hook command. Nothing else changes.
- `--no-dependencies`: `lip install` installs, for each package argument, exactly the first candidate of its request as an explicitly installed package, replacing an installed package of the same identity. It follows no dependency, and installs or uninstalls no other package. An installed package with the identity, version and commit of that candidate is only marked explicitly installed. lip-workspace(7), "Valid states" is not guaranteed to hold afterwards.
- `--platform <RID>`: The platform of the environment ([lip(1), "Environment"](lip.md#environment)) is `<RID>`.

## SEE ALSO

[lip(1)](lip.md), [lip-uninstall(1)](lip-uninstall.md), [lip-update(1)](lip-update.md), [lip-workspace(7)](lip-workspace.md)
