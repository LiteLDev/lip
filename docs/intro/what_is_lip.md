# What is lip?

After this page you know what `lip` does, the two ways to use it, and the five words that the rest of the documentation relies on.

## What lip does

`lip` runs a [LeviLamina](https://github.com/LiteLDev/LeviLamina) server and manages its packages. LeviLamina is a mod loader for the Minecraft Bedrock Dedicated Server, the server program that Mojang publishes for Minecraft Bedrock Edition.

With `lip` you can:

- **Set up a server.** `lip` downloads the game and LeviLamina in versions that work together, and puts them in a folder.
- **Add and remove mods.** `lip` installs a mod together with everything it depends on, updates it, and removes it again.
- **Run the server.** `lip` starts the server, shows what it writes, passes on the commands you type, and can restart it after a crash.
- **Edit the configuration.** `lip` shows `server.properties`, the allowlist, the permissions and the configuration files of the mods, and lets you change them.

`lip` manages servers only. Mods for the game client are managed by launchers such as LeviLauncher.

## Two ways to use it

Both do the same work. Use whichever suits what you are doing.

| | The interface | The commands |
| --- | --- | --- |
| How you start it | Type `lip` and press `Enter`. | Type `lip` and a command, such as `lip install`. |
| What it is | A full-screen program in your terminal, with a sidebar of nine pages. You use the arrow keys and `Enter`. | One line, one action. `lip` does it and returns. |
| Good for | Getting started, browsing mods, watching the server, editing configuration. | Scripts, automation, servers that you reach over SSH. |
| Where to read on | [Your first server](first_server.md), then [A tour of the interface](../interface/tour.md) | [A server from the commands](../guides/server_from_commands.md), then the [command line reference](../reference/cli.md) |

![The Home page of lip in an empty folder, with the sidebar of nine pages at the left and three next steps: create a server, install mods, start the server.](/screenshots/en/home-01-empty.svg)

## Five words to know

- A **workspace** is a folder that holds one server. `lip` works in one workspace at a time: the folder you start it in, or one you pick in the interface. What `lip` records about a workspace is in its `.lip` folder.
- The **game** is the Minecraft Bedrock Dedicated Server of one version, such as 1.26.60. A workspace has one game.
- The **loader** is LeviLamina. It lets the server run mods. A server without a loader is a plain Minecraft server.
- A **mod** adds something to the server. To `lip`, a mod is a **package**: something it can install from a Git repository, a URL or a file on your disk, in a version, with the packages it depends on. The game and the loader are packages too.
- The **daemon** is a `lip` process that works in the background for you. It carries out what you ask for in the interface and in the commands, and it keeps a server running after you close the interface. `lip` starts it when needed, and it exits by itself when it has nothing to do.

[How lip works](../concepts/how_lip_works.md) explains each of them in depth.

## Where lip runs

`lip` is released for these systems. A server with LeviLamina is a Windows program; on Linux and macOS, `lip` runs it through [Wine](https://www.winehq.org/), which it downloads and sets up itself.

| System | A server with LeviLamina and mods | A server without a loader |
| --- | --- | --- |
| Windows on x64 or ARM64 | Yes | Yes |
| Linux on x64 | Yes, through Wine | Yes: the Windows server through Wine, or the Linux server directly |
| macOS on Intel or Apple silicon | Yes, through Wine | Yes, the Windows server through Wine |

There is no release for Linux on ARM64, where a LeviLamina server does not run. See [Linux and macOS](../guides/linux_macos.md) for what `lip` installs there and for the limits.

## Next

[Install lip](installation.md), then build [your first server](first_server.md).
