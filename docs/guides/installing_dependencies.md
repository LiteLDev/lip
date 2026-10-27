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

This installs the newest version of the package and the dependencies it declares into your workspace, and records them in the [lockfile](../concepts/lockfiles.md). It does not modify `manifest.json`.

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

lip manages server workspaces: mods are installed to `mods/<name>/` (see [Environment](../concepts/package_manifest.md#environment)). Client workspaces are managed by launchers such as LeviLauncher, which read the same packages.

In a server workspace, `plugins` is a link to `mods` (a directory junction on Windows, a symbolic link elsewhere), so software that looks in `plugins/` keeps working, and a path under `plugins/` is the same file as the path under `mods/`. Before `lip install`, `lip uninstall` or `lip update` changes any package, lip sets this up:

-   It creates `mods` if it is missing, and the `plugins` link if `plugins` is missing.
-   If `plugins` is a regular directory (a workspace set up before), lip moves its contents into `mods`, then replaces `plugins` with the link. If an entry exists in both `plugins` and `mods`, lip stops before moving anything; merge the two, then run the command again.
-   If `plugins` is a link to somewhere else, lip stops and changes nothing.

With `--dry-run`, lip only prints what it would do.

## Command Flags

-   `-n, --dry-run`: Simulate the installation without making any changes. Useful to see what would happen.
-   `--no-dependencies`: Install only the specified package, ignoring its dependencies.
-   `--ignore-scripts`: Skip running the `preInstall` and `postInstall` scripts defined in the package.
-   `--rid <RID>`: Set the .NET RID of the workspace, e.g. `win-x64`.
