# Frequently asked questions

Short answers to what new users ask most, each with a link to the page that says more.

## About lip

### What does lip do?

lip sets up a Minecraft Bedrock server in a folder, installs and updates its mods, and runs it. You use it through an interface in the terminal, or through commands. See [What is lip](intro/what_is_lip.md).

### Is it only for LeviLamina?

No, but that is what it is made for. The mods lip installs are LeviLamina mods, and LeviLamina is the only loader lip sets up. You can also run a plain server without a loader: the Windows one with `lip setup --game <version> --no-loader`, or the Linux one with `--build linux`. See [Choose the build and the loader](guides/server_from_commands.md#choose-the-build-and-the-loader).

### Can lip install client mods?

No. lip manages servers only. Mods for the game client are managed by launchers such as LeviLauncher, which read the same packages.

### Which systems does it run on?

Windows, Linux on x64, and macOS. On Linux and macOS, lip runs the Windows server through a Wine that it downloads itself. There is no lip for Linux on arm64. See [Linux and macOS](guides/linux_macos.md#what-works-where).

### Do I need git, Wine or anything else?

No. lip talks to git repositories itself, and downloads the game, the loader, Wine and the Microsoft Visual C++ Redistributable when it needs them. See [Internals](concepts/internals.md#the-git-client).

### Is there a Traditional Chinese version?

Yes. The interface has English, Simplified Chinese and Traditional Chinese, and follows the language of your system. To choose one, use the Settings page, or `lip config set language zh-Hant`. This documentation has all three languages too. What the commands write to the terminal is always English. See [language](guides/configuration.md#language).

## Using lip

### Can I use it without the interface?

Yes. Everything the interface does is a command: `lip setup`, `lip install`, `lip update`, `lip uninstall`, `lip run`, `lip config`. Start with [A server from the commands](guides/server_from_commands.md). The commands also work in scripts, where no one can answer questions: see `--accept-eula` there.

### And the other way round: do I have to learn the commands?

No. Run `lip` without anything after it and use the interface. See [Your first server](intro/first_server.md).

### Does the server stop when I close the terminal?

It depends on how you started it.

- **Started in the interface:** no. It keeps running after you quit the interface or close the terminal.
- **Started with `lip run --managed`:** no. Closing the terminal only detaches you.
- **Started with a plain `lip run`:** yes. lip asks the server to stop, and waits for it.

In the first two cases the server runs in the background process of lip. Come back to it with the interface, or with `lip run --managed`. See [Keep the server running](guides/server_from_commands.md#keep-the-server-running).

### How do I stop a server that runs in the background?

Open the interface and choose Stop on the Server page. Or run `lip run --managed` in the server folder to attach to it, and press Ctrl+C.

### Does the server start again after the machine restarts?

No. lip does not start anything when the system starts. To have that, let the service manager of your system run `lip run` in the server folder.

### Can lip restart a server that crashes?

Yes. Turn on "Auto restart" on the Server page, or run `lip config set restart on-failure --workspace`. See [Restart the server when it crashes](guides/server_from_commands.md#restart-the-server-when-it-crashes).

### Can I install or update mods while the server runs?

In the interface, no: it asks you to stop the server first. With the commands, yes, and the server sees the change when it next starts. On Windows such a command can fail because the running server has a file open; lip then undoes what it did. See [Change packages while the server runs](guides/server_from_commands.md#change-packages-while-the-server-runs).

### Can I run several servers?

Yes, each in a folder of its own, with its own ports in `server.properties`. One folder runs one server at a time. The interface keeps a list of your server folders on the [Workspaces](interface/workspaces.md) page.

### Can two people manage one server?

Two people who sign in as the **same user** of the machine share everything: both see the same running server in the interface, and either can stop it or change mods. lip makes sure that two changes never run at the same time: the second one fails at once and is to be tried again.

Two **different users** of the machine do not share the background process of lip. Each can change packages in the folder if the file permissions allow it. But a server that one of them started is, for the other, "Running outside this interface": visible, but not theirs to stop or to type into.

lip has no network access of its own. To manage a server on another machine, sign in to that machine, for example over SSH, and run lip there.

### How do I update lip?

Run `lip self-update`, or use "Check for updates" on the Settings page. `lip self-update --check` only tells you whether there is a newer version. See [Installation](intro/installation.md).

### How do I change the Minecraft version of my server?

Run `lip setup --game <version>` again in the server folder, or use the Workspaces page of the interface. Mods move to versions that work with the new game. See [Change the game later](guides/server_from_commands.md#change-the-game-later).

## Files

### Where are my worlds and my settings?

In the server folder, where the game puts them. lip does not move them.

| What | Where in the server folder |
| --- | --- |
| Worlds | `worlds/` |
| Settings of the game | `server.properties`, `allowlist.json`, `permissions.json` |
| A mod, and usually its settings | `mods/<name>/` |

The interface edits the settings on the [Game config](interface/game_config.md) and [Mod config](interface/mod_config.md) pages.

### What is the `.lip` folder?

lip's notes about the server folder: which packages are installed, the workspace settings, failure reports, and on Linux and macOS the Wine prefix. It is hidden on Windows. Leave it in place, and move it with the folder when you move the server. See [Files and directories](reference/files.md).

### What does lip keep outside the server folder?

Four per-user directories: your settings, a cache of downloads, the Wine of lip, and the list of workspaces of the interface.

| What | Windows | Linux and macOS |
| --- | --- | --- |
| Settings | `%APPDATA%\lip` | `~/.config/lip` |
| Cache | `%LOCALAPPDATA%\lip\Cache` | `~/.cache/lip` |
| Data: what lip installs to run the server, such as its Wine | `%LOCALAPPDATA%\lip\Data` | `~/.local/share/lip` |
| State: the list of workspaces | `%LOCALAPPDATA%\lip\State` | `~/.local/state/lip` |

The cache can be deleted at any time, with `lip cache clean`. lip writes nothing to the temporary directory of the system. See [Per-user directories](concepts/how_lip_works.md#per-user-directories).

### Will changing the game version delete my world?

No. `worlds/`, `server.properties`, `allowlist.json` and `permissions.json` are kept. Some folders of the game itself, such as `resource_packs/` and `behavior_packs/` at the root, are deleted and written again. See [What is kept and what is deleted](guides/server_from_commands.md#what-is-kept-and-what-is-deleted).

### How do I move a server to another folder or machine?

Stop the server and move or copy the whole folder, `.lip` included. Then run lip in the new place. On a new machine, lip installs what the server needs of the system again when you first start it.

## Packages

### Where do packages come from?

From public git repositories, mostly on GitHub. There is no central registry. A package is named by the address of its repository, such as `github.com/LiteLDev/LegacyScriptEngine`. The Get mods page of the interface lists the mods of [levimc.org](https://levimc.org). See [Name a package](guides/managing_packages.md#name-a-package).

### How do I install a package from my disk?

Give `lip install` the path of a ZIP file or a folder that has a `lip.toml` at its root, for example `lip install ./my-mod.zip`. See [A ZIP file or a folder on your disk](guides/managing_packages.md#a-zip-file-or-a-folder-on-your-disk).

### Does lip support private repositories?

No. lip downloads without signing in, so only public repositories work.

### Why is a tag of my repository not a version?

Only tags named `v` followed by a full version, such as `v1.2.3`, are versions. Tags such as `v1.2` or `1.2.3` are not. You can still install any tag, branch or commit: `lip install github.com/user/repo@tag:1.2.3`. See [A git repository](guides/managing_packages.md#a-git-repository).

### Can I still install packages made for lip v0.x, with a `tooth.json`?

Yes. lip reads them and installs them like any other package. A server folder and a configuration of lip v0.x are converted the first time lip works with them. Authors can convert a package with `lip pkg migrate`: see [Migrating packages](authors/migrating_packages.md).

### What is `lip.toml`?

The file that describes a package: its name, its version, what it depends on and which files it installs. You need one only to make a package. See [lip.toml](concepts/lip_toml.md).

### Does `lip install` change my `lip.toml`?

No. `lip install` installs into the server folder and records it in `.lip`. It never writes a `lip.toml`.

## When something goes wrong

### A command failed. Where do I find out why?

Read the `error:` and `hint:` lines that lip wrote. For everything else there is a failure report in `.lip/logs/` of the server folder; lip prints its path. See [Read the failure](guides/troubleshooting.md#read-the-failure).

### What happens when an install fails halfway?

Nothing is left half done. A command that changes the server folder does all of its work or none: on a failure, or when you press Ctrl+C, lip undoes what it did. If lip itself is killed, the next command in that folder puts things right first. See [Operations](concepts/how_lip_works.md#operations).

### GitHub is slow or blocked here. What can I do?

Set a GitHub proxy, or use the proxy of your network. See [When GitHub is slow or blocked](guides/configuration.md#when-github-is-slow-or-blocked).

### Can I run two lip commands at the same time?

Yes, in different server folders. In the same folder, only one command at a time may change it: a second one fails at once, and you run it again when the first is done. Commands that only read, such as `lip list`, always work. See [Another lip is changing the workspace](guides/troubleshooting.md#another-lip-is-changing-the-workspace).

### What is the lip process that keeps running in the background?

The daemon. It carries out your commands and keeps your managed servers running. It exits by itself after 10 minutes without work, unless it runs a server. `lip daemon status` shows it, and `lip daemon stop` ends it when it is idle. See [The daemon and its clients](concepts/how_lip_works.md#the-daemon-and-its-clients).
