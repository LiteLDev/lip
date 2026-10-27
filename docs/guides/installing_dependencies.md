# Installing Dependencies

lip provides flexible ways to install dependencies.

## Basic Installation

To install a package by its path:

```sh
lip install <PACKAGES>
```

Example:
```sh
lip install github.com/user/repo
```

This installs the newest version of the package and the dependencies it declares into your workspace, and records them in the lockfile (`lip_lock.json`). It does not modify `manifest.json`.

## Specifying Versions

You can install exactly one version using the `@` syntax:

```sh
lip install github.com/user/repo@1.0.0
```

To select a variant, append `#<variant>` to the path, e.g. `github.com/user/repo#lua@1.0.0`.

## Installing from Local Files

You can install a package directly from a local `.mcmod` file or directory:

```sh
lip install ./path/to/package.mcmod
lip install ./path/to/package/
```

This is useful for testing packages before publishing.

## Installing from Remote URLs

You can also install a ZIP archive directly from an HTTPS URL:

```sh
lip install https://example.com/package.mcmod
```

## Choosing the Platform

lip detects whether the workspace is a server or a client from the executables in it (see [Environment](../concepts/package_manifest.md#environment)). In an empty directory, tell lip which one it is the first time:

```sh
lip install --platform server github.com/user/repo
```

The platform is then recorded in the lockfile.

## Command Flags

-   `-n, --dry-run`: Simulate the installation without making any changes. Useful to see what would happen.
-   `--no-dependencies`: Install only the specified package, ignoring its dependencies.
-   `--ignore-scripts`: Skip running the `preInstall` and `postInstall` scripts defined in the package.
-   `--platform <server|client>`: Set the platform of the workspace.
-   `--rid <RID>`: Set the .NET RID of the workspace, e.g. `win-x64`.
