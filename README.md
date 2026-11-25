# lip

<img src=".github/logo.svg" alt="" width="128" align="right">

Run a LeviLamina server and manage its packages

`lip` sets up a Minecraft Bedrock server with [LeviLamina](https://github.com/LiteLDev/LeviLamina), installs and updates its mods, runs it, and edits its configuration, on Windows, Linux and macOS. Type `lip` in a terminal for its interface, or use its commands in scripts.

## Install

### Windows

Download `lip-<version>-win-x64.msi` (or `-win-arm64.msi`) from the [latest release](https://github.com/LiteLDev/lip/releases/latest) and run it. It installs `lip` for you, into `%LOCALAPPDATA%\Programs\lip`, and adds that folder to your `PATH`, without administrator rights. Where an earlier version installed `lip` for every user, it updates that installation instead, which needs an administrator.

### macOS

In Terminal:

```shell
curl -fsSL https://levimc.org/lip/install.sh | sh
```

The script installs `lip` to `~/.local/bin`, without administrator rights, and offers to add that folder to your `PATH`. Or download `lip-<version>-osx-arm64.zip` (Apple silicon) or `-osx-x64.zip` (Intel) from the [latest release](https://github.com/LiteLDev/lip/releases/latest) with `curl`, extract the file `lip` from it, and put it in a folder on your `PATH`; a file that a browser downloads is quarantined by macOS, and does not run as it is.

### Linux

In a terminal:

```shell
curl -fsSL https://levimc.org/lip/install.sh | sh
```

The script installs `lip` to `~/.local/bin`, without root, and offers to add that folder to your `PATH`. Or download `lip-<version>-linux-x64.tar.xz` from the [latest release](https://github.com/LiteLDev/lip/releases/latest), extract the file `lip` from it, and put it in a folder on your `PATH`. `lip` is released for x64 only.

### What it needs

`lip` is one executable file and needs nothing else installed. The server needs the Visual C++ runtime: on Windows, when it is not installed, `lip` downloads it and sets it up for you, without administrator rights. On Linux and macOS it runs the Windows server, the one LeviLamina loads into, through [Wine](https://www.winehq.org/), which it downloads and sets up itself, and which has a Visual C++ runtime of its own. On a Mac with Apple silicon, Wine needs Rosetta 2, which `lip` installs once you accept its license. On Linux, `lip` needs glibc 2.27 or later (so not Alpine), and Wine needs the GnuTLS library, and Fontconfig where FreeType is installed: `lip` names any that is missing and the command that installs it. See [Linux and macOS](https://levimc.org/lip/guides/linux_macos.html). `lip -v` prints its version, and `lip self-update` updates it. See [`lip self-update`](https://levimc.org/lip/reference/commands/self_update.html).

The script's settings, such as `LIP_VERSION`, `LIP_INSTALL_DIR` and `LIP_GITHUB_PROXY`, are at the top of [`install.sh`](install.sh).

## Use

```shell
mkdir servers
cd servers
lip
```

The interface opens, and its Home page leads you through three steps: create a server, install mods, start the server. The tutorial [Your first server](https://levimc.org/tutorials/server/first-server/) shows every step with a picture.

The same with commands:

```shell
lip install LeviLamina      # LeviLamina, and the newest game that it supports
lip install LeviOptimize    # a mod
lip run                     # the server
```

[The documentation](https://levimc.org/lip/) has the rest, in English, [简体中文](https://levimc.org/zh-Hans/lip/) and [繁體中文](https://levimc.org/zh-Hant/lip/).

## Contributing

Feel free to dive in! [Open an issue](https://github.com/LiteLDev/lip/issues/new/choose) or submit PRs. [CONTRIBUTING.md](CONTRIBUTING.md) says how to build and test `lip`, and where its documentation is written.

`lip` follows the [Contributor Covenant](https://www.contributor-covenant.org/version/2/1/code_of_conduct/) Code of Conduct.

### Contributors

This project exists thanks to all the people who contribute.

![Contributors](https://contrib.rocks/image?repo=LiteLDev/lip)

## License

GPL-3.0-only © 2023-2026 LeviMC
