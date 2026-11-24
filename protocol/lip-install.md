# lip-install(1)

## NAME

lip-install - install packages into the workspace

## SYNOPSIS

```
lip install <PACKAGES>... [--accept-eula] [--accept-rosetta-license] [-n | --dry-run] [-h | --help]
```

## DESCRIPTION

Each of `<PACKAGES>` is a package argument ([lip(1), "Package arguments"](lip.md#package-arguments)).

`lip install` installs a set of packages that satisfies [lip-workspace(7), "Valid states"](lip-workspace.md#valid-states) for the requests of the package arguments and the requests that made the installed explicitly installed packages:

- The requested packages, including those already installed as dependency packages, become explicitly installed.
- An installed package keeps its version when that satisfies lip-workspace(7), "Valid states"; otherwise a dependency package may change version. Each dependency package that no explicitly installed package needs is uninstalled.
- When a requested package is already explicitly installed, or two package arguments request the same identity, the command fails.

### The game and the loader

The game and the loader are those of [lip-setup(1), "Terms"](lip-setup.md#terms).

`lip install` installs, uninstalls and replaces no package of the game or the loader, other than as "Setting up" below defines: when the requests cannot be satisfied with them as they are, the command fails, and its message should name the game version and the version of the loader, and `lip setup`.

When the workspace has no game, and `lip install` is not carried out as a setup, and the package that a package argument names first (the first candidate of its request), or a package that the operation would install, is a mod ([lip-workspace(7), "Terms"](lip-workspace.md#terms)), a package of the server source, or a package of the loader source, the command fails before anything changes, and its message should name `lip setup`.

### Setting up

A package argument **names the server** when its source is a remote repository whose location is `github.com/LiteLDev/bds`, and **names the loader** when its source is a remote repository whose location is `github.com/LiteLDev/LeviLamina`, without a variant, and without a ref or with the ref `<version>`; locations are compared ASCII case-insensitively. When two package arguments name the server, or two name the loader, the command fails.

When a package argument names the server or the loader, `lip install` is carried out as a setup ([lip-setup(1), "Setting up"](lip-setup.md#setting-up)) whose requests include those of the other package arguments, and which is otherwise that of `lip setup` with:

- the build `linux` when the argument that names the server has the variant `linux`, and `windows` otherwise;
- the loader `levilamina@<V>` when an argument names the loader with the ref `<V>`, `levilamina` when one names it without a ref and an argument names the server with a ref, the version of the first candidate of the request of the argument that names the loader when only that argument gives a version, and no loader when no argument names the loader;
- the game version of the ref of the argument that names the server; without such a ref, when an argument names the loader, the version of the first candidate of Resolve(game request without its ref, R, environment), R the range of the dependency on the server source of the loader that the item above gives (a loader that has none fails the command); otherwise the version of the first candidate of Resolve(game request without its ref, any version, environment).

`--accept-eula` is that of `lip setup`.

## OPTIONS

- `--accept-eula`: The user accepts the license of the game ([lip-setup(1), "The license"](lip-setup.md#the-license)), for a `lip install` carried out as a setup; otherwise it has no effect.
- `--accept-rosetta-license`: The user accepts the license of Rosetta 2, so that preparing the system installs Rosetta 2 without asking ([lip-workspace(7), "System dependencies"](lip-workspace.md#system-dependencies)).
- `-n`, `--dry-run`: A dry run ([lip(1), "Dry run"](lip.md#dry-run)). The messages name the packages that the operation would install, uninstall or mark explicitly installed.

## SEE ALSO

[lip(1)](lip.md), [lip-setup(1)](lip-setup.md), [lip-uninstall(1)](lip-uninstall.md), [lip-update(1)](lip-update.md), [lip-workspace(7)](lip-workspace.md)
