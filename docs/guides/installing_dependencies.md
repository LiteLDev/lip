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

## Server Workspaces Only

lip manages server workspaces: mods are installed to `plugins/<name>/` (see [Environment](../concepts/package_manifest.md#environment)). Client workspaces are managed by launchers such as LeviLauncher, which read the same packages.

## Command Flags

-   `-n, --dry-run`: Simulate the installation without making any changes. Useful to see what would happen.
-   `--no-dependencies`: Install only the specified package, ignoring its dependencies.
-   `--ignore-scripts`: Skip running the `preInstall` and `postInstall` scripts defined in the package.
-   `--rid <RID>`: Set the .NET RID of the workspace, e.g. `win-x64`.
