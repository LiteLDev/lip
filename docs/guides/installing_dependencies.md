# Installing Dependencies

lip provides flexible ways to install dependencies.

## Basic Installation

To install a package by its name or ID:

```sh
lip install <PACKAGES>
```

Example:
```sh
lip install github.com/user/repo
```

This installs the package into your workspace and records it in the lockfile (`tooth_lock.json`). It does not modify `tooth.json`.

## Specifying Versions

You can specify a version using the `@` syntax:

```sh
lip install github.com/user/repo@1.0.0
```

## Installing from Local Files

You can install a package directly from a local archive file (`.zip`, `.tar`, etc.):

```sh
lip install ./path/to/package.zip
```

This is useful for testing packages before publishing.

## Installing from Remote URLs

You can also install packages directly from a URL:

```sh
lip install https://example.com/package.zip
```

## Command Flags

-   `-n, --dry-run`: Simulate the installation without making any changes. Useful to see what would happen.
-   `--no-dependencies`: Install only the specified package, ignoring its dependencies.
-   `--ignore-scripts`: Skip running any `pre_install`, `install`, or `post_install` lifecycle scripts defined in the package.
