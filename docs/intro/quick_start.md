# Quick Start

This guide will help you get started with `lip`, covering basic usage for managing packages in your project.

## Prerequisites

Before proceeding, ensure that you have [installed lip](installation.md). lip does not require `git` to be installed.

## Initializing a Project

If you are creating a mod or another package, initialize its manifest. Run the following command in your project's root directory:

```shell
lip init
```

This command creates a `manifest.json` file in the current directory (it fails if one already exists). This file is both the LeviLamina manifest of your mod and its lip package manifest. You do not need it just to install packages.

## Installing Packages

Run lip in the root directory of your server or client (the workspace). To install a package, use the `install` command followed by the package path. You can specify a version using the `@` symbol.

```shell
lip install github.com/LiteLDev/LeviLamina@1.0.0
```

`lip` supports installing from various sources, including Git repositories, `.mcmod` files and URLs. When you install a package:
- It is downloaded to the local cache.
- Its dependencies are resolved and installed.
- It is recorded in the lockfile (`lip_lock.json`). Your `manifest.json` is not modified.

lip detects a server or a client from `bedrock_server.exe`, `bedrock_server_mod.exe` or `Minecraft.Windows.exe` in the workspace. In an empty directory, pass `--platform server` or `--platform client` if lip asks for it.

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

To change to a specific version, including an older one, add it with `@`, e.g. `lip update github.com/LiteLDev/LeviLamina@1.0.0`. Running `lip update` without arguments updates every package you installed explicitly.

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
- Learn more about the [Package Manifest](../concepts/package_manifest.md) structure.
