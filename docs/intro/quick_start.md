# Quick Start

This guide will help you get started with `lip`, covering basic usage for managing packages in your project.

## Prerequisites

Before proceeding, ensure that you have [installed lip](installation.md). lip does not require `git` to be installed.

## Setting Up the Server in the Interface

The simplest way: open a terminal, run `lip`, and press `n`. lip asks for the directory of the server, the game version, the build and the loader, shows what it will install, and asks you to accept the Minecraft EULA. When it is done, start the server in the **3 Server** tab and install mods from the **2 Browse** tab. See [Interactive Interface](../cli/tui.md#new-server).

If GitHub cannot be reached from your network, the interface also asks whether to set a GitHub proxy, connect directly anyway, or decide later. See [GitHub reachability](../cli/tui.md#github-reachability).

## Setting Up the Server

To set it up on the command line instead, run lip in the root directory of your server (the workspace), e.g. a new empty directory. First set up the server: the Bedrock Dedicated Server of a game version, with LeviLamina as its loader:

```shell
lip setup --game 1.21.0
```

This installs the newest LeviLamina that supports the game 1.21.0. To take the newest game that LeviLamina supports instead, install LeviLamina:

```shell
lip install LeviLamina
```

Before it downloads the game, lip shows the links to the Minecraft End User License Agreement and the Microsoft Privacy Policy, and asks whether you accept them: type `yes`. In a script, where lip cannot ask, add `--accept-eula`. See [`lip setup`](../cli/commands/setup.md).

## Installing Packages

To install a package, such as a mod, use the `install` command followed by the package id. You can specify a version using the `@` symbol, e.g. `github.com/LiteLDev/LegacyScriptEngine#lua@1.0.0`.

```shell
lip install github.com/LiteLDev/LegacyScriptEngine#lua
```

Mods need the server: in a workspace that has no game yet, `lip install` of a mod fails and asks you to run `lip setup` first.

`lip` supports installing from various sources, including Git repositories, URLs, and ZIP files and directories on your disk. When you install a package:
- It is downloaded to the local cache.
- Its dependencies are resolved and installed.
- It is recorded in the [state file](../concepts/lockfiles.md), `.lip/state.json` in the workspace. Your `lip.toml` is not modified.

lip manages server workspaces only. Client mods are managed by launchers such as LeviLauncher.

lip installs the packages of the platform `win-x64`, on every system. On Linux and macOS, it runs their hook commands and the server through Wine. The Linux server, without LeviLamina, is set up with `lip setup --game <version> --build linux`, and `lip run` starts it on Linux on x64. See [Running a Server](../guides/running_server.md#the-linux-server).

## Running the Server

Once the server is set up, start it in the workspace:

```shell
lip run
```

Type server commands as usual. Press Ctrl+C to stop the server: lip sends it `stop` and exits when it has exited. You can install and update packages from another terminal while the server runs; they take effect when you start it again.

## Listing Installed Packages

To view all packages currently installed in your project:

```shell
lip list
```

This will display a list of packages along with their installed versions.

## Viewing Package Information

You can retrieve the package file of a package and its available versions, as JSON, without installing it:

```shell
lip pkg view github.com/LiteLDev/LeviLamina
```

## Updating Packages

To update a package to a newer version, use the `update` command.

```shell
lip update github.com/LiteLDev/LegacyScriptEngine#lua
```

To change to a specific version, including an older one, add it with `@`, e.g. `lip update github.com/LiteLDev/LegacyScriptEngine#lua@1.0.0`. This pins the package to that version until you update it by its path without `@`. Running `lip update` without arguments updates every package you installed explicitly, except the game and LeviLamina: to change their versions, run `lip setup` again, e.g. `lip setup --game 1.21.50`.

## Uninstalling Packages

To remove a package and the files it installed from your project:

```shell
lip uninstall github.com/LiteLDev/LegacyScriptEngine#lua
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

## Creating a Package

If you are creating a mod or another package, initialize its `lip.toml`. Run the following command in your project's root directory:

```shell
lip pkg init
```

This command asks for the package id of your package, such as `github.com/user/MyMod`, and creates a `lip.toml` file in the current directory (it fails if one already exists). This file describes your package to lip. You do not need it just to install packages. See [Creating Packages](../guides/creating_packages.md).

## Next Steps

- Explore the [CLI Overview](../cli/overview.md) for more advanced command usage.
- Learn more about the [lip.toml](../concepts/lip_toml.md) structure.
