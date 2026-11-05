# Quick Start

This guide will help you get started with `lip`, covering basic usage for managing packages in your project.

## Prerequisites

Before proceeding, ensure that you have [installed lip](installation.md). lip does not require `git` to be installed.

## Initializing a Project

If you are creating a mod or another package, initialize its `tooth.json`. Run the following command in your project's root directory:

```shell
lip init
```

This command asks for the tooth path of your package, such as `github.com/user/MyMod`, and creates a `tooth.json` file in the current directory (it fails if one already exists). This file describes your package to lip. You do not need it just to install packages.

## Installing Packages

Run lip in the root directory of your server (the workspace). To install a package, use the `install` command followed by the tooth path. You can specify a version using the `@` symbol.

```shell
lip install github.com/LiteLDev/LeviLamina@1.0.0
```

`lip` supports installing from various sources, including Git repositories, URLs, and ZIP files and directories on your disk. When you install a package:
- It is downloaded to the local cache.
- Its dependencies are resolved and installed.
- It is recorded in the [workspace file](../concepts/lockfiles.md), `.lip/workspace.json` in the workspace. Your `tooth.json` is not modified.

lip manages server workspaces only. Client mods are managed by launchers such as LeviLauncher.

## Listing Installed Packages

To view all packages currently installed in your project:

```shell
lip list
```

This will display a list of packages along with their installed versions.

## Viewing Package Information

You can retrieve detailed information about a package, such as its available versions or metadata, without installing it.

**View package metadata:**

```shell
lip view github.com/LiteLDev/LeviLamina
```

**List available versions:**

```shell
lip versions github.com/LiteLDev/LeviLamina
```

## Updating Packages

To update a package to a newer version, use the `update` command.

```shell
lip update github.com/LiteLDev/LeviLamina
```

To change to a specific version, including an older one, add it with `@`, e.g. `lip update github.com/LiteLDev/LeviLamina@1.0.0`. This pins the package to that version until you update it by its path without `@`. Running `lip update` without arguments updates every package you installed explicitly.

## Uninstalling Packages

To remove a package and the files it installed from your project:

```shell
lip uninstall github.com/LiteLDev/LeviLamina
```

## Configuration

`lip` allows you to configure global settings, such as proxies.

**List current configuration:**

```shell
lip config list
```

**Set a configuration value:**

```shell
lip config set github_proxy https://mirror.ghproxy.com/
```

## Next Steps

- Explore the [CLI Overview](../cli/overview.md) for more advanced command usage.
- Learn more about the [tooth.json](../concepts/tooth_json.md) structure.
