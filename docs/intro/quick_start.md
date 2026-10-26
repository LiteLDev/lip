# Quick Start

This guide will help you get started with `lip`, covering basic usage for managing packages in your project.

## Prerequisites

Before proceeding, ensure that you have [installed lip](installation.md). lip does not require `git` to be installed.

## Initializing a Project

To start tracking packages, initialize a new project manifest. Run the following command in your project's root directory:

```shell
lip init
```

This command creates a `tooth.json` file in the current directory. This file acts as a manifest, storing metadata about your project and its dependencies.

## Installing Packages

To install a package, use the `install` command followed by the package identifier. You can specify a version using the `@` symbol.

```shell
lip install github.com/LiteLDev/LeviLamina@1.0.0
```

`lip` supports installing from various sources, including Git repositories. When you install a package:
- It is downloaded to the local cache.
- It is added to your `tooth.json` manifest.
- Its dependencies are resolved and installed.

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

Alternatively, running `install` with a specific version will also update or downgrade the package.

## Uninstalling Packages

To remove a package from your project and `tooth.json`:

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
