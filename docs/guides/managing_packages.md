# Managing packages

After this page you can install, list, update and uninstall packages with `lip` commands, name a package in every way lip understands, and check what a command would do before it does it. In the interface, the same is done on the [Mods](../interface/mods.md) and [Get mods](../interface/get_mods.md) pages.

## What a package is

A **package** is anything lip installs into a workspace: a mod, the loader, the game, or a set of files that another package needs. Run every command of this page in the folder of the server, the workspace.

lip sorts the installed packages into two groups:

- **Packages you asked for.** You named them in `lip setup` or `lip install`. lip keeps them until you uninstall them.
- **Dependencies.** lip installed them because another package needs them. lip removes them by itself when nothing needs them any more.

Mods need a server. Set one up first: see [A server from the command line](https://levimc.org/tutorials/server/command-line/).

## Name a package

A package is named by where it comes from, its **source**. There are four kinds:

| Source | Example |
| --- | --- |
| A git repository | `github.com/LiteLDev/LegacyScriptEngine` |
| A ZIP file on the web | `https://example.com/downloads/my-mod.zip` |
| A ZIP file on your disk | `./my-mod.zip` |
| A folder on your disk | `../MyMod` |

### A git repository

Most packages live in a git repository, mostly on GitHub. Name it by its address without `https://`:

```sh
lip install github.com/LiteLDev/LegacyScriptEngine#lua
```

Three parts can follow the address:

- **A short name.** A name without `/` means a repository of `github.com/LiteLDev/`. `LeviLamina` is `github.com/LiteLDev/LeviLamina`.
- **A variant**, after `#`. Some packages come in several flavours and name each one. `LegacyScriptEngine#lua` is the Lua flavour of LegacyScriptEngine. The page of the package says which variants it has.
- **A ref**, after `@`. It picks which state of the repository to install:

| Ref | Installs | Example |
| --- | --- | --- |
| none | The newest released version that works in your workspace | `lip install LegacyScriptEngine#lua` |
| `@<version>` | Exactly that version | `lip install LegacyScriptEngine#lua@0.13.1` |
| `@branch:<name>` | The newest commit of a branch | `lip install github.com/user/repo@branch:main` |
| `@tag:<name>` | The commit of any tag | `lip install github.com/user/repo@tag:nightly` |
| `@rev:<commit>` | One commit, by its full 40-digit id | `lip install github.com/user/repo@rev:0123456789abcdef0123456789abcdef01234567` |

The released versions of a repository are its tags named `v` followed by a version, such as `v1.2.3`. A version is written without the `v`.

Only public repositories work. lip does not need `git` to be installed.

### A ZIP file on the web

Give the full address, starting with `https://`. The file must be a ZIP archive with a `lip.toml` at its root:

```sh
lip install https://example.com/downloads/my-mod.zip
```

Addresses starting with `http://` or another scheme are refused.

### A ZIP file or a folder on your disk

Give the path of a ZIP file, or of a folder, that has a `lip.toml` at its root:

::: code-group

```powershell [Windows]
lip install .\my-mod.zip
lip install D:\downloads\my-mod.zip
lip install ..\MyMod
```

```sh [Linux / macOS]
lip install ./my-mod.zip
lip install ~/downloads/my-mod.zip
lip install ../MyMod
```

:::

To be sure lip reads a name as a path and not as a repository, start it with `./` or `../` (`.\` or `..\` on Windows), or give an absolute path. A variant can follow a URL or a path too, as in `./my-mod.zip#lua`.

The exact rules are in [Command line](../reference/cli.md).

## Install

[`lip install`](../reference/commands/install.md) takes one or more packages:

```sh
lip install LegacyScriptEngine#lua LegacyMoney
```

lip finds a set of versions that work together and with your game and loader, downloads them, and installs them with their dependencies. It ends with:

```text
✓ Packages installed successfully.
```

Things to know:

- **Mods need a game.** In a workspace without a game, installing a mod fails and names `lip setup`.
- **Installing what you already asked for fails.** lip tells you to use `lip update` instead, which is the command that changes a version.
- **A dependency can become a package you asked for.** Install it by name. lip then notes that you want it, so it stays when nothing else needs it.
- **The game and the loader stay as they are.** When a package needs another game or loader than the workspace has, the command fails and names `lip setup`. The exception: `lip install LeviLamina`, or an install that names the game `github.com/LiteLDev/bds`, is a setup. See [`lip setup`](../reference/commands/setup.md).

## List

[`lip list`](../reference/commands/list.md) shows what is installed, in the two groups:

```sh
lip list
```

```text
User-Requested
├── github.com/LiteLDev/bds@<version>
├── github.com/LiteLDev/LeviLamina@<version>
└── github.com/LiteLDev/LegacyScriptEngine#lua@<version>
Dependencies
├── github.com/LiteLDev/LegacyRemoteCall@<version>
└── …
```

Each line is the name of a package, with its variant after `#`, and its version after `@`. The game is `github.com/LiteLDev/bds`.

For a script, ask for JSON:

```sh
lip list --json
```

`lip list` only reads. It works while another command changes the workspace and while the server runs.

## Update

[`lip update`](../reference/commands/update.md) without a name updates every package you asked for:

```sh
lip update
```

With names, it updates those packages:

```sh
lip update LegacyScriptEngine#lua
```

What "update" means depends on how the package was installed:

| Installed from | An update gives |
| --- | --- |
| A repository, without a ref | The newest released version that works in your workspace |
| A repository `@<version>` | The same version. The package is pinned. |
| A repository `@rev:<commit>` | The same commit. The package is pinned. |
| A repository `@branch:<name>` | The commit the branch points to now |
| A repository `@tag:<name>` | The commit the tag points to now |
| A URL | The file, downloaded again |
| A path on your disk | The file or folder, read again |

A package is replaced only when what lip finds differs from what is installed. Otherwise it is left alone.

More things to know:

- **Change or pin a version** by naming it: `lip update LegacyScriptEngine#lua@0.13.1`. This also goes back to an older version. The package then stays on that version.
- **Unpin** by naming the package without a ref: `lip update LegacyScriptEngine#lua`. It follows the released versions again.
- **Dependencies change only as far as needed.** lip does not move a dependency to a newer version unless a package you asked for needs that.
- **The game and the loader are never updated** by `lip update`. Naming one of them fails. Change them with [`lip setup`](../reference/commands/setup.md).
- **A package from a path that is gone is kept.** `lip update` without names warns and leaves it as it is.
- **The packages you name must be installed.** They become packages you asked for.

## Uninstall

[`lip uninstall`](../reference/commands/uninstall.md) takes the names of installed packages:

```sh
lip uninstall LegacyMoney
```

You can name a package the way `lip list` shows it, without the version, or by the source you installed it from. A name without a variant matches every installed variant of the package.

lip removes the files the package installed. Then it removes every dependency that nothing needs any more.

Things to know:

- **Only packages you asked for can be uninstalled.** A dependency goes away when the packages that need it do.
- **A package that others still need stays**, as a dependency. lip says so: `<name> is still required by other packages; keeping it as a dependency.`
- **Files you may have edited are kept** when the package says so. For example, uninstalling the game keeps `server.properties`. See [What survives a game change](../reference/files.md#what-survives-a-game-change).
- **The game and the loader can be uninstalled** like any package. A workspace without a game has no server to run.

## Dry run

Every command that changes the workspace takes `--dry-run`, or `-n` for short: `lip setup`, `lip install`, `lip update` and `lip uninstall`.

```sh
lip update --dry-run
```

A dry run does everything the real command does up to the point where it would change something. It reads the workspace, asks the network for versions, chooses the packages, and checks that their files can be placed. Then it tells you the plan and stops:

```text
Dry run: would uninstall LegacyMoney 0.16.1 (1 file)
Dry run: would uninstall LegacyRemoteCall 0.13.0 (1 file)
Dry run: nothing was changed.
```

What a dry run promises:

- **It changes nothing.** No file of the workspace, of the cache or of the configuration is created, changed or deleted. The one exception is a failure report, when the dry run itself fails.
- **It runs nothing.** No program of a package is run, and nothing is installed on your system.
- **It asks nothing.** Where the real command would ask you, such as for the Minecraft EULA or before a setup uninstalls mods, the dry run says that it would ask.
- **It does not wait for other commands.** It takes no lock, so it works while another lip command changes the workspace.
- **It still needs the network**, and may take as long as the real command up to that point.

A dry run that succeeds does not prove that the real command will: the network, the disk or the workspace may change in between. A dry run that fails tells you why the real command would fail.

When an earlier command was interrupted, a dry run tells you so and plans with the workspace as it is. See [After a crash or a power cut](troubleshooting.md#after-a-crash-or-a-power-cut).

The interface does a dry run for you before each change: the plan it shows before you choose Apply is this plan.

## When a command fails or you cancel

A command that changes the workspace does all of its work or none of it.

- **It fails before it changes anything** when the problem can be found in advance: a package that does not exist, versions that do not fit together, a download that fails, a file that another package owns. The workspace is untouched.
- **It fails in the middle**, for example because a file cannot be written. lip then undoes every change it made, and the workspace is as it was before the command.
- **You press Ctrl+C.** While lip is still preparing, it stops at once. While it changes the workspace, it cancels, undoes its changes, and writes `Cancelled. Any changes to the workspace were undone.` A second Ctrl+C ends lip at once, and the next command cleans up.

One thing cannot be undone: what the programs that a package runs while it is installed have already done.

After a failure, lip writes what went wrong and what you can do about it, and saves a failure report. See [Troubleshooting](troubleshooting.md).

## Next

- [How lip works](../concepts/how_lip_works.md): what happens inside an operation.
- [Internals](../concepts/internals.md): how lip finds versions and chooses between them.
- [Publish your mod with lip](https://levimc.org/tutorials/develop/publish-a-package/): make a package of your own.
