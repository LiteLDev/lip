# lip setup

Choose the game and the loader of the workspace.

## Synopsis

```shell
lip setup --game <VERSION> [--build <BUILD>] [--loader <LOADER> | --no-loader] [options]
```

## Description

`lip setup` makes the server of the workspace the Bedrock Dedicated Server of the version `<VERSION>`, of the build `<BUILD>`, with the loader `<LOADER>` or without a loader, and keeps the other packages of the workspace that work with them. Run it in a new workspace before you install mods, and again whenever you want another version of the game.

### The game and the loader

- The **game** is the installed package of `github.com/LiteLDev/bds`, the Bedrock Dedicated Server. A workspace has at most one; a workspace without one has no game.
- The **build** of the game is `windows` or `linux`. `windows` is the Windows server, `github.com/LiteLDev/bds`; `linux` is the Linux server, `github.com/LiteLDev/bds#linux`.
- The **loader** is the installed package of `github.com/LiteLDev/LeviLamina`, or none.

`lip setup` installs both explicitly, pinned to their versions: the game from `github.com/LiteLDev/bds@<VERSION>` or `github.com/LiteLDev/bds#linux@<VERSION>`, and the loader from `github.com/LiteLDev/LeviLamina@<version>`. [`lip install`](install.md) and [`lip update`](update.md) never change them afterwards: run `lip setup` again to change the game or the loader. [`lip uninstall`](uninstall.md) uninstalls them as any other package.

When the workspace already has the game and the loader that you ask for, of the same versions, `lip setup` changes nothing of them.

### The loader

Without `--loader` and `--no-loader`, the loader is LeviLamina for the build `windows`, and no loader for the build `linux`. LeviLamina loads only into the Windows server, so `--build linux` with `--loader` fails.

`--loader levilamina` takes the newest version of LeviLamina whose dependency on `github.com/LiteLDev/bds` accepts the game version; when no version of LeviLamina supports that game version, `lip setup` fails before anything changes. `--loader levilamina@<version>` takes exactly that version of LeviLamina, which must support the game version too.

### The Minecraft EULA

Before lip downloads the game, it shows the links to the [Minecraft End User License Agreement](https://www.minecraft.net/eula) and the [Microsoft Privacy Policy](https://go.microsoft.com/fwlink/?LinkId=521839), and asks whether you accept them: type `yes` to go on. Any other answer fails the command before anything changes.

lip does not remember your answer: it asks every time `lip setup` installs or changes the game, and not when the game stays as it is. With `--accept-eula`, lip does not ask. When the standard input or the standard error of lip is not a terminal, as in a script, `lip setup` without `--accept-eula` fails before anything changes.

### Changing the game of a workspace with mods

Changing the game or the loader resolves the other explicitly installed packages again, for the new game and loader. A package keeps its version when that version works with them; otherwise it may change version. The game and the loader that the workspace had before are uninstalled, together with every dependency that no explicitly installed package needs any more.

When an installed mod has no version that works with the new game and loader, lip lists such mods and asks whether to uninstall them; when you do not answer yes, the command fails before anything changes. When the standard input or the standard error of lip is not a terminal, `lip setup` fails instead, naming them: uninstall them first with [`lip uninstall`](uninstall.md), then run `lip setup` again. When the other packages cannot be kept at all, the command fails.

lip asks about such mods first, then about the EULA, after it has decided what to do and before it downloads the game.

### System Dependencies

For the build `windows`, `lip setup` installs what the Windows server needs of the system when it is missing: Wine on Linux and macOS, and the Microsoft Visual C++ Redistributable (see [System Dependencies](../../guides/internal_mechanism.md#system-dependencies)). The Linux server needs neither.

## Options

| Option | Description |
| --- | --- |
| `--game <VERSION>` | The version of the game, e.g. `1.21.0`. Required. |
| `--build <BUILD>` | `windows` (the default) or `linux`. |
| `--loader <LOADER>` | `levilamina`, the newest LeviLamina for the game, or `levilamina@<version>`. The default for the build `windows`. |
| `--no-loader` | Set up the game without a loader. The default for the build `linux`. Cannot be given with `--loader`. |
| `--accept-eula` | Accept the Minecraft EULA and the Microsoft Privacy Policy without being asked. |
| `-n, --dry-run` | Run without making any changes. Asks nothing; says which packages would be installed, uninstalled or marked explicitly installed, which mods would be left out, and whether lip would ask you to accept the EULA. |

## Examples

Set up the game 1.21.0 with the newest LeviLamina that supports it:

```shell
lip setup --game 1.21.0
```

Set up a given version of LeviLamina, accepting the EULA in a script:

```shell
lip setup --game 1.21.0 --loader levilamina@1.0.0 --accept-eula
```

Set up the Windows server without a loader:

```shell
lip setup --game 1.21.0 --no-loader
```

Set up the Linux server, which has no loader:

```shell
lip setup --game 1.21.0 --build linux
```

See what moving to another game version would do to the mods of the workspace:

```shell
lip setup --game 1.21.50 --dry-run
```
