# Installing Dependencies

lip provides flexible ways to install dependencies.

## Basic Installation

To install a package by its tooth path:

```sh
lip install <PACKAGES>
```

Example:
```sh
lip install github.com/user/repo
```

This installs the newest version of the package that has an entry for your workspace's platform and RID, and the dependencies it declares, into your workspace, and records them in the [workspace file](../concepts/lockfiles.md). It does not modify any `tooth.json`.

## Specifying Versions

You can install exactly one version using the `@` syntax:

```sh
lip install github.com/user/repo@1.0.0
```

To select a variant, append `#<variant>` to the path, e.g. `github.com/user/repo#lua@1.0.0`.

## Installing a Branch, Tag or Commit

To try unreleased changes, install the head of a branch, any tag, or a commit:

```sh
lip install github.com/LiteLDev/LeviLamina@branch:main
lip install github.com/user/repo@tag:nightly
lip install github.com/user/repo@rev:0123456789abcdef0123456789abcdef01234567
```

The workspace file records the commit, so the workspace stays on it until `lip update`, which follows a branch to its new head. See [Git refs](../cli/commands/install.md#git-refs).

## Installing from Local Files and Directories

You can install a package directly from a local ZIP file or directory with a `tooth.json` at its root, by a path relative to the current directory or an absolute path:

```sh
lip install ./path/to/package.zip
lip install C:\Downloads\package.zip
lip install ../MyTool
```

This is useful for testing packages before publishing.

## Installing from Remote URLs

You can also install a ZIP archive with a `tooth.json` at its root directly from an HTTPS URL:

```sh
lip install https://example.com/package.zip
```

## Server Workspaces Only

lip manages server workspaces: mods are installed to `mods/<name>/` (see [Environment](../concepts/tooth_json.md#environment)). Client workspaces are managed by launchers such as LeviLauncher, which read the same packages.

In a server workspace, `plugins` is a link to `mods` (a directory junction on Windows, a symbolic link elsewhere), so software that looks in `plugins/` keeps working, and a path under `plugins/` is the same file as the path under `mods/`. Before `lip install`, `lip uninstall` or `lip update` changes any package, lip sets this up:

-   It creates `mods` if it is missing, and the `plugins` link if `plugins` is missing.
-   If `plugins` is a regular directory (a workspace set up before), lip moves its contents into `mods`, then replaces `plugins` with the link. If an entry exists in both `plugins` and `mods`, lip stops before moving anything; merge the two, then run the command again.
-   If `plugins` is a file or a link to somewhere else, lip stops and changes nothing.

With `--dry-run`, lip only prints what it would do.

## Command Flags

-   `-n, --dry-run`: Simulate the installation without making any changes. Useful to see what would happen.
-   `--no-dependencies`: Install only the specified package, ignoring its dependencies, and uninstall no other package. This may leave the workspace broken.
-   `--ignore-scripts`: Do not run any package hooks, including the uninstall hooks of packages the installation removes or replaces.
-   `--rid <RID>`: Set the .NET RID of the workspace, e.g. `win-x64`.
