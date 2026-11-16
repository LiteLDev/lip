# lip setup

Choose the game and the loader of the workspace.

## Synopsis

```
lip setup --game <VERSION> [--build <BUILD>] [--loader <LOADER> | --no-loader] [--accept-eula] [-n | --dry-run] [-h | --help]
```

## Description

`lip setup` makes the server of the workspace the game `<VERSION>` of the build `<BUILD>`, with the loader `<LOADER>` or without a loader, and keeps the other packages of the workspace that work with them. Run it in an empty directory to create a server, and again whenever you want another game version or loader.

### Terms

- The **game** is the installed package of `github.com/LiteLDev/bds`, the Minecraft Bedrock Dedicated Server. A workspace has at most one. A workspace without one **has no game**.
- The **build** of the game is `windows` or `linux`: the Windows server or the Linux server.
- The **loader** is the installed package of `github.com/LiteLDev/LeviLamina`. A workspace without one **has no loader**.
- A LeviLamina version **fits** a game version when its dependency on `github.com/LiteLDev/bds` accepts that version.

### What it does

In order:

1. It works out the game, the build and the loader from the options. See [The loader](#the-loader).
2. It fails, before anything changes, when
   - the build is `linux` and a loader is asked for: LeviLamina loads only into the Windows server;
   - the game version has no build of that kind (see [Game versions and builds](#game-versions-and-builds));
   - the loader version does not exist or does not fit the game version.
3. It plans the packages. The game is requested from `github.com/LiteLDev/bds@<VERSION>`, or `github.com/LiteLDev/bds#linux@<VERSION>` for the build `linux`; the loader from `github.com/LiteLDev/LeviLamina@<version>`. Every other explicitly installed package is requested again from the source it was installed from.
   - A package keeps its version when that still works; otherwise it may change version.
   - The game and the loader that the workspace had are uninstalled, unless they are the ones asked for.
   - Dependencies that no explicitly installed package needs any more are uninstalled.
4. It asks about [left-out packages](#left-out-packages), if there are any.
5. It asks you to accept [the license](#the-license), if it installs a game.
6. For the build `windows`, it installs what the Windows server needs of the system when that is missing: Wine on Linux and macOS, and the Microsoft Visual C++ Redistributable. See [Linux and macOS](../../guides/linux_macos.md).
7. It installs the packages, as one [workspace operation](../cli.md#workspace-operations).

When the workspace already has the game and the loader asked for, of the same builds and versions, `lip setup` installs and uninstalls nothing for them.

The game and the loader are installed explicitly and pinned to their versions. [`lip install`](install.md) and [`lip update`](update.md) never change them afterwards; run `lip setup` again to change them. [`lip uninstall`](uninstall.md) removes them like any other package.

### The loader

| Options | Loader |
| --- | --- |
| neither `--loader` nor `--no-loader`, build `windows` | The same as `--loader levilamina`. |
| neither `--loader` nor `--no-loader`, build `linux` | No loader. |
| `--loader levilamina` | The newest LeviLamina that fits the game version. When none fits, the command fails before anything changes. |
| `--loader levilamina@<version>` | Exactly that LeviLamina version. It must fit the game version. |
| `--no-loader` | No loader. |

`levilamina` is compared ignoring case.

### Game versions and builds

lip takes the list of server builds from `https://levimc.org/api/v4/mc/versions/`. A build is four numbers, such as `1.21.80.03`. It gives two game versions:

| Build | Game version | Names |
| --- | --- | --- |
| `1.21.80.03` | `1.21.80` | The first build `1.21.80.*` in the list of that system. |
| `1.21.80.03` | `1.21.80-patch.3` | Exactly the build `1.21.80.03`. |

So give `--game 1.21.80` in general, and `--game 1.21.80-patch.3` when you need one particular build. The Windows builds and the Linux builds are separate lists: a version may exist for one and not for the other.

The server itself is downloaded from `www.minecraft.net`.

| Build | Where it runs |
| --- | --- |
| `windows` | On Windows directly. On Linux and macOS through the Wine that lip installs. |
| `linux` | On Linux on x64 only. `lip setup --build linux` works on every system, but [`lip run`](run.md) refuses to start the server elsewhere. |

::: info
The interface offers the build `linux` only when lip runs on Linux on x64. The command has no such limit.
:::

### The license

The game is under the [Minecraft End User License Agreement](https://www.minecraft.net/eula) and the [Microsoft Privacy Policy](https://go.microsoft.com/fwlink/?LinkId=521839).

When `lip setup` installs a game, it writes both links and asks whether you accept them. Type `yes` to go on. With any other answer the command fails before anything changes.

- With `--accept-eula`, lip does not ask.
- Without it, when standard input or standard error is not a terminal, as in a script, the command fails before anything changes.
- The answer is not recorded. Every setup that installs a game asks again; a setup that leaves the game as it is does not ask.

### Left-out packages

An explicitly installed package, other than the game and the loader, is **left out** when no version of it works with the new game and loader. It can be a mod or any other package.

lip names the left-out packages and asks whether to uninstall them. When you do not answer `yes`, the command fails before anything changes. When standard input or standard error is not a terminal, lip cannot ask: the command fails and names them. Uninstall them first with [`lip uninstall`](uninstall.md), then run `lip setup` again.

When the remaining packages cannot be installed together at all, the command fails.

lip asks about left-out packages first, then about the license. Both questions come after lip has decided what to do and before it downloads the game.

## Options

| Option | Description |
| --- | --- |
| `--game <VERSION>` | The game version, such as `1.21.0`: a SemVer version without a leading `v`. Required. Any other value is a usage error. |
| `--build <BUILD>` | `windows` or `linux`. Without it, `windows`. Any other value is a usage error. |
| `--loader <LOADER>` | `levilamina` or `levilamina@<version>`. Any other value is a usage error. |
| `--no-loader` | Set up the game without a loader. Giving both `--loader` and `--no-loader` is a usage error. |
| `--accept-eula` | Accept the license without being asked. |
| `-n`, `--dry-run` | A [dry run](../cli.md#dry-run). It asks no question. The messages name the packages that would be installed, uninstalled or marked explicitly installed, the left-out packages, and whether lip would ask you to accept the license. |
| `-h`, `--help` | Write the help and exit. |

## Output

`lip setup` has no result: it writes nothing to standard output. Progress, the questions and the final line are messages on standard error.

## Exit status

| Code | Meaning |
| --- | --- |
| `0` | The setup was carried out, or the dry run finished. |
| not `0` | A usage error, or the setup failed. A setup that failed is undone. |

## Examples

Create a server of the game 1.21.0 with the newest LeviLamina that fits it:

```shell
lip setup --game 1.21.0
```

Set up a given LeviLamina version, accepting the license in a script:

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

See what moving to another game version would do to the installed packages:

```shell
lip setup --game 1.21.50 --dry-run
```

## See also

- [A server from the command line](https://levimc.org/tutorials/server/command-line/)
- [Workspaces](../../interface/workspaces.md): the new-server wizard and changing versions in the interface.
- [What survives a game change](../files.md#what-survives-a-game-change)
- [`lip install`](install.md#setting-up-with-install), which sets up a server when you install LeviLamina or the server itself.
- [`lip run`](run.md)
