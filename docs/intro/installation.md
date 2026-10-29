# Installation

`lip` is a general package installer. To get started, you need to install the `lip` executable on your system.

## npm

The default installation method for all platforms is npm:

```shell
npm install -g @levimc/lip
```

This installs both `lip` and `lipd` and works on Windows, Linux, and macOS.

If you only want to run `lip` without installing it globally, you can also use `npx`:

```shell
npx @levimc/lip --help
```

This is useful for trying `lip` or running one-off commands.

::: tip
This package was previously published as `@futrime/lip`. If you installed it under the old name, uninstall it first with `npm uninstall -g @futrime/lip`.
:::

## winget <Badge platform="windows" />

```shell
winget install levimc.lip
```

::: tip
This package was previously published as `futrime.lip`. If you installed it under the old ID, uninstall it first with `winget uninstall futrime.lip`.
:::

## Windows setup <Badge platform="windows" />

Download the latest `lip-<version>-<runtime>-setup.exe` from the [Releases page](https://github.com/LiteLDev/lip/releases/latest) and run it.
