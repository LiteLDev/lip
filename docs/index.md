---
layout: home

hero:
  name: "lip"
  text: "Run a LeviLamina server and manage its mods"
  tagline: Set up a Minecraft Bedrock server, add mods, and run it, from one screen in your terminal.
  image:
    src: /logo.webp
    alt: lip
  actions:
    - theme: brand
      text: Your first server
      link: https://levimc.org/tutorials/server/first-server/
    - theme: alt
      text: Guide
      link: /interface/tour
    - theme: alt
      text: Reference
      link: /reference/cli

features:
  - title: A server in three steps
    details: Pick a folder, a game version and a loader. lip downloads the Bedrock Dedicated Server and LeviLamina, and starts the server for you.
    link: https://levimc.org/tutorials/server/first-server/
  - title: Mods from levimc.org
    details: Browse the mods, see which versions fit your server, and install, update or remove them together with what they depend on.
    link: /interface/get_mods
  - title: One interface, no commands to learn
    details: Type lip and use the arrow keys and Enter. Every change is shown as a plan before lip applies it.
    link: /interface/tour
  - title: Commands when you want them
    details: Everything the interface does is also a command, for scripts and for servers without a screen. On Windows, Linux and macOS.
    link: /reference/cli
---

## What lip is

`lip` runs a [LeviLamina](https://levimc.org/LeviLamina/) server and manages its packages. LeviLamina is the mod loader of LeviMC for the Minecraft Bedrock Dedicated Server, the server program that Mojang publishes for Minecraft Bedrock Edition.

With `lip` you can:

- **Set up a server.** `lip` downloads the game and LeviLamina in versions that work together, and puts them in a folder.
- **Add and remove mods.** `lip` installs a mod together with everything it depends on, updates it, and removes it again. The mods of [LeviMC Mods](https://levimc.org/mods/) are on its Get mods page, and each mod's page there gives the `lip install` line for it. A mod that is not listed installs just the same, from its Git repository, a URL or a file.
- **Run the server.** `lip` starts the server, shows what it writes, passes on the commands you type, and can restart it after a crash.
- **Edit the configuration.** `lip` shows `server.properties`, the allowlist, the permissions and the configuration files of the mods, and lets you change them.

`lip` manages servers only. Mods for the game client are managed by launchers such as [LeviLauncher](https://levimc.org/LeviLauncher/).

## The interface and the commands

Both do the same work. Use whichever suits what you are doing.

| | The interface | The commands |
| --- | --- | --- |
| How you start it | Type `lip` and press `Enter`. | Type `lip` and a command, such as `lip install`. |
| What it is | A full-screen program in your terminal, with a sidebar of nine pages. You use the arrow keys and `Enter`. | One line, one action. `lip` does it and returns. |
| Good for | Getting started, browsing mods, watching the server, editing configuration. | Scripts, automation, servers that you reach over SSH. |
| Where to read on | [A tour of the interface](interface/tour.md) | The [command line reference](reference/cli.md) |

This is the Server page of the interface while the server runs: what the server writes, and a line to type its commands in.

![The Server page of lip while the server runs: the output of the server says "Server started.", and the input line below it waits for a command.](/screenshots/en/first-server-21-running.svg)

## Start here

The tutorials of LeviMC take you from nothing to one thing done, step by step:

- [Your first server](https://levimc.org/tutorials/server/first-server/): install `lip`, and go from an empty folder to a running server with one mod, in the interface.
- [A server from the command line](https://levimc.org/tutorials/server/command-line/): the same with `lip setup`, `lip install` and `lip run`.
- [Publish your mod with lip](https://levimc.org/tutorials/develop/publish-a-package/): make a package of your mod, publish it, and get it listed on LeviMC Mods.

## Five words to know

- A **workspace** is a folder that holds one server. `lip` works in one workspace at a time: the folder you start it in, or one you pick in the interface. What `lip` records about a workspace is in its `.lip` folder.
- The **game** is the Minecraft Bedrock Dedicated Server of one version, such as 1.26.60. A workspace has one game.
- The **loader** is LeviLamina. It lets the server run mods. A server without a loader is a plain Minecraft server.
- A **mod** adds something to the server. To `lip`, a mod is a **package**: something it can install from a Git repository, a URL or a file on your disk, in a version, with the packages it depends on. The game and the loader are packages too.
- The **daemon** is a `lip` process that works in the background for you. It carries out what you ask for in the interface and in the commands, and it keeps a server running after you close the interface. `lip` starts it when needed, and it exits by itself when it has nothing to do.

[How lip works](concepts/how_lip_works.md) explains each of them in depth.

## Where lip runs

`lip` is released for these systems. A server with LeviLamina is a Windows program; on Linux and macOS, `lip` runs it through [Wine](https://www.winehq.org/), which it downloads and sets up itself.

| System | A server with LeviLamina and mods | A server without a loader |
| --- | --- | --- |
| Windows on x64 or ARM64 | Yes | Yes |
| Linux on x64 | Yes, through Wine | Yes: the Windows server through Wine, or the Linux server directly |
| macOS on Intel or Apple silicon | Yes, through Wine | Yes, the Windows server through Wine |

There is no release for Linux on ARM64, where a LeviLamina server does not run. See [Linux and macOS](guides/linux_macos.md) for what `lip` installs there and for the limits.

## The rest of this site

- **Guide**: [using the interface](interface/tour.md), page by page; [managing packages](guides/managing_packages.md), [configuration](guides/configuration.md) and [Linux and macOS](guides/linux_macos.md) with the commands; [migrating packages](authors/migrating_packages.md) of lip v0.x; [troubleshooting](guides/troubleshooting.md) and the [FAQ](faq.md).
- **Concepts**: [how lip works](concepts/how_lip_works.md), [lip.toml](concepts/lip_toml.md), the [state file](concepts/state_file.md) and the [internals](concepts/internals.md).
- **Reference**: the [command line](reference/cli.md), every [command](reference/commands/setup.md), the [interface](reference/interface.md), the [configuration](reference/configuration.md) and the [files](reference/files.md).
