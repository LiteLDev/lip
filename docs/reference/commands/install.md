# lip install

Install packages into the workspace.

## Synopsis

```
lip install <PACKAGES>... [--accept-eula] [-n | --dry-run] [-h | --help]
```

## Description

`lip install` installs each of `<PACKAGES>`, with its dependencies, into the workspace, as one [workspace operation](../cli.md#workspace-operations). Each of `<PACKAGES>` is a [package argument](../cli.md#package-arguments):

| Form | Example | Installs |
| --- | --- | --- |
| `<repo>` | `github.com/user/repo` | The highest version tag of the git repository `https://<repo>.git`. |
| `<repo>#<variant>` | `github.com/LiteLDev/LegacyScriptEngine#lua` | That variant of the package. Every form below takes `#<variant>` too, before the `@`. |
| `<repo>@<version>` | `github.com/user/repo@1.0.0` | The commit of the tag `v<version>`. |
| `<repo>@branch:<name>` | `github.com/user/repo@branch:main` | The commit the branch points to now. |
| `<repo>@tag:<name>` | `github.com/user/repo@tag:nightly` | The commit the tag points to now. Any tag name works. |
| `<repo>@rev:<commit>` | `github.com/user/repo@rev:0123456789abcdef0123456789abcdef01234567` | That commit. All 40 hexadecimal digits are needed. |
| An `https://` URL | `https://example.com/package.zip` | The ZIP archive at that URL. `#<variant>` may follow. |
| A local path | `./package.zip`, `../MyTool`, `C:\Downloads\package.zip` | A ZIP file, or a directory, on your disk. `#<variant>` may follow. |

A `<repo>` without `/` is a repository of LiteLDev: `LeviLamina` is `github.com/LiteLDev/LeviLamina`.

What it does:

- The packages you name become **explicitly installed**. One that was installed as a dependency becomes explicitly installed too.
- The dependencies of the packages are installed as **dependency packages**.
- An installed package keeps its version when that still works. Otherwise a dependency package may change version.
- Dependency packages that no explicitly installed package needs any more are uninstalled.

What it refuses, before anything changes:

- A package argument that is invalid, or names no package for the side `server` and the platform `win-x64`.
- A package that is already explicitly installed. Use [`lip update`](update.md) to change its version.
- Two arguments that name the same package.
- A set of packages that cannot be installed together, such as two mods with the same mod name, or dependencies that no version satisfies.
- Anything that would change the game or the loader. See below.

The version of a package installed from `@branch:`, `@tag:` or `@rev:` is the `version` in its `lip.toml` at that commit. Such a package also satisfies dependencies on the same repository and variant, when its version is in their range.

### The game and the loader

The **game** and the **loader** are the installed packages of `github.com/LiteLDev/bds` and `github.com/LiteLDev/LeviLamina`; see [`lip setup`](setup.md#terms).

`lip install` never installs, uninstalls or replaces the game or the loader, except as [Setting up with install](#setting-up-with-install) says. When the packages cannot be installed with them as they are, for example when a mod needs a newer LeviLamina, the command fails and names the game version, the loader version and `lip setup`. Set up a game and a loader that the mod supports with [`lip setup`](setup.md), then install the mod.

In a workspace that has no game, `lip install` fails and points to `lip setup` when a package you name, or one that it would install, is a mod, the server or LeviLamina. File packages that need none of them can be installed without a game.

### Setting up with install

An argument **names the server** when it is `github.com/LiteLDev/bds`, with or without a variant or ref. An argument **names the loader** when it is `github.com/LiteLDev/LeviLamina` without a variant, and without a ref or with `@<version>`. Both are compared ignoring case.

When an argument names the server or the loader, `lip install` is carried out as a [`lip setup`](setup.md), together with the other arguments, in one operation:

| Setting | Value |
| --- | --- |
| Build | `linux` when the server is named as `github.com/LiteLDev/bds#linux`; `windows` otherwise. |
| Loader | No loader when no argument names LeviLamina. Otherwise LeviLamina: at the version of its `@<version>`; without one, the newest version that fits the game version that the server argument gives; or the newest LeviLamina when no argument gives a game version. |
| Game version | The `@<version>` of the server argument. Without one: the newest game version that the LeviLamina above accepts, or the newest game version when no argument names LeviLamina. |

| Command | Sets up |
| --- | --- |
| `lip install LeviLamina` | The newest LeviLamina, with the newest game it supports. |
| `lip install LeviLamina@1.0.0` | LeviLamina 1.0.0, with the newest game it supports. |
| `lip install github.com/LiteLDev/bds@1.21.0 LeviLamina` | The game 1.21.0, with the newest LeviLamina that fits it. |
| `lip install github.com/LiteLDev/bds@1.21.0` | The Windows server 1.21.0, without a loader. |
| `lip install github.com/LiteLDev/bds#linux` | The newest Linux server, without a loader. |

Two arguments that name the server, or two that name the loader, fail the command. Everything that [`lip setup`](setup.md) says holds: the license question, `--accept-eula`, left-out packages and the system dependencies.

## Options

| Option | Description |
| --- | --- |
| `--accept-eula` | Accept the [license of the game](setup.md#the-license) without being asked, when `lip install` is carried out as a setup. Otherwise it has no effect. |
| `-n`, `--dry-run` | A [dry run](../cli.md#dry-run). The messages name the packages that would be installed, uninstalled or marked explicitly installed. Nothing is changed. |
| `-h`, `--help` | Write the help and exit. |

## Output

`lip install` has no result: it writes nothing to standard output. Progress and the final line are messages on standard error. The hook commands of the packages write to standard output and standard error themselves.

## Exit status

| Code | Meaning |
| --- | --- |
| `0` | The packages were installed, or the dry run finished. |
| not `0` | A usage error, or the installation failed. An installation that failed is undone. |

## Examples

Install a mod from its repository, at its newest version:

```shell
lip install github.com/LiteLDev/LegacyScriptEngine#lua
```

Install a given version, and see first what it would do:

```shell
lip install github.com/user/repo@1.0.0 --dry-run
lip install github.com/user/repo@1.0.0
```

Install several packages in one operation:

```shell
lip install github.com/user/repo github.com/user/other@2.1.0
```

Install from a branch, a URL, a ZIP file and a directory:

```shell
lip install github.com/user/repo@branch:main
lip install https://example.com/package.zip
lip install ./package.zip
lip install ../MyTool
```

Set up a server and install a mod in one step, in a script:

```shell
lip install LeviLamina github.com/user/repo --accept-eula
```

## See also

- [Managing packages](../../guides/managing_packages.md)
- [Get mods](../../interface/get_mods.md): finding and installing mods in the interface.
- [`lip setup`](setup.md), [`lip update`](update.md), [`lip uninstall`](uninstall.md), [`lip list`](list.md)
- [lip.toml](../../concepts/lip_toml.md): what a package is.
