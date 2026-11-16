# lip

<img src="docs/logo.png" alt="" width="128" align="right">

Run a LeviLamina server and manage its packages

`lip` sets up a Minecraft Bedrock server with [LeviLamina](https://github.com/LiteLDev/LeviLamina), installs and updates its mods, runs it, and edits its configuration, on Windows, Linux and macOS. Type `lip` in a terminal for its interface, or use its commands in scripts.

## Install

- **Windows**: download `lip-<version>-win-x64.msi` (or `-win-arm64.msi`) from the [latest release](https://github.com/LiteLDev/lip/releases/latest) and run it.
- **Linux and macOS**: download the archive for your system from the [latest release](https://github.com/LiteLDev/lip/releases/latest), extract the file `lip` from it, and put it in a folder on your `PATH`.

`lip` is one executable file and needs nothing else installed. `lip -v` prints its version, and `lip self-update` updates it. See [`lip self-update`](https://levimc.org/lip/reference/commands/self_update.html).

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

[The documentation](https://levimc.org/lip/) has the rest, in English, 简体中文 and 繁體中文.

## Contributing

Feel free to dive in! [Open an issue](https://github.com/LiteLDev/lip/issues/new/choose) or submit PRs. [CONTRIBUTING.md](CONTRIBUTING.md) says how to build and test `lip` and its documentation.

`lip` follows the [Contributor Covenant](https://www.contributor-covenant.org/version/2/1/code_of_conduct/) Code of Conduct.

### Contributors

This project exists thanks to all the people who contribute.

![Contributors](https://contrib.rocks/image?repo=LiteLDev/lip)

## License

GPL-3.0-only © 2023-2026 LeviMC
