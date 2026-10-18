# Installation

`lip` is a general package installer. To get started, you need to install the `lip` executable on your system.

## npm

The default installation method for all platforms is npm:

```shell
npm install -g @futrime/lip
```

This installs both `lip` and `lipd` and works on Windows, Linux, and macOS.

If you only want to run `lip` without installing it globally, you can also use `npx`:

```shell
npx @futrime/lip --help
```

This is useful for trying `lip` or running one-off commands.

## winget

```shell
winget install futrime.lip
```

## Windows setup

Download the latest `lip-<version>-<runtime>-setup.exe` from the [Releases page](https://github.com/LiteLDev/lip/releases/latest) and run it.
