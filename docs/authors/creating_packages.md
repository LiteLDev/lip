# Creating packages

This guide takes you from an empty repository to a package that anyone can install with lip: you write a `lip.toml`, test it on your own server, and publish it with a git tag.

A **package** is something lip can install into a server: a mod, a library, a tool, a set of files. Technically it is an archive, usually a git repository, with a file named `lip.toml` at its root. [lip.toml](../concepts/lip_toml.md) is the reference for every field; this guide shows the common cases.

## Before you start

You need:

- lip, [installed](../intro/installation.md);
- a git repository for the package, such as `https://github.com/user/MyMod`;
- a server to test in. [Create one](../intro/first_server.md) in another directory.

The address of the repository without `https://` and `.git` is the **package id**: `github.com/user/MyMod`. Users install your package by that id.

## Start with `lip pkg init`

In the root directory of your repository, run:

```shell
lip pkg init
```

lip asks for the package id, and asks again until the answer is a valid id. Then it writes `lip.toml`:

```toml
format = 4
id = "github.com/user/MyMod"
version = "0.1.0"

[[variants.""]]
side = "server"
platform = "win-x64"
```

The command fails when a `lip.toml` already exists, or when it cannot ask because it does not run in a terminal. In the interface, **Create lip.toml** on the [Tools](../interface/tools.md) page does the same for the selected workspace.

## The shape of `lip.toml`

```toml
format = 4                       # always 4
id = "github.com/user/MyMod"     # the package id: the path of the repository
version = "1.0.0"                # the version, without a leading "v"

[info]                           # for people and mod listings; lip ignores it
name = "My Mod"
description = "A short description"
tags = ["utility"]

[[variants.""]]                  # one entry of the default variant
side = "server"                  # "server" or "client"
platform = "win-x64"             # lip installs only win-x64 entries

[variants."".deps]               # what the entry depends on
"github.com/LiteLDev/LeviLamina" = ">=1.0.0 <2.0.0"

[[variants."".assets]]           # where the files come from
type = "mcmod"
urls = ["https://github.com/user/MyMod/releases/download/v{{version}}/MyMod-server-win-x64.zip"]
```

Three things to know:

- **Entries.** `variants` maps a variant name to a list of entries. Each entry is for exactly one side and one platform. `[[variants.""]]` adds an entry to the default variant, `""`.
- **lip installs only `server` entries for `win-x64`**, on every system. On Linux and macOS it runs the Windows server through Wine. A server mod therefore needs a `server` entry with `platform = "win-x64"`.
- **Templates.** Every string is a template: `{{version}}` becomes the `version` of the file. Use it in URLs, so that a release only needs a new `version`.

## A mod package

A **mod** is an entry with an asset of the type `mcmod`: a ZIP archive with the LeviLamina `manifest.json` of the mod at its root. lip writes the whole archive into `mods/<name>/`, where `<name>` is the `name` in that `manifest.json`.

1. Build your mod, so that one directory holds `manifest.json` and the files of the mod.
2. Pack that directory:

   ```shell
   lip pkg pack build/ -o MyMod-server-win-x64.zip
   ```

3. Attach the ZIP file to the release of the version on your git host.
4. Point to it from `lip.toml`:

   ```toml
   format = 4
   id = "github.com/user/MyMod"
   version = "1.0.0"

   [info]
   name = "My Mod"

   [[variants.""]]
   side = "server"
   platform = "win-x64"
   preserve = ["mods/MyMod/config/"]

   [variants."".deps]
   "github.com/LiteLDev/LeviLamina" = ">=1.0.0 <2.0.0"

   [[variants."".assets]]
   type = "mcmod"
   urls = ["https://github.com/user/MyMod/releases/download/v{{version}}/MyMod-server-win-x64.zip"]
   ```

lip reads only the `name` of `manifest.json` and installs the file unchanged. It does not read the `dependencies` there: list what your mod needs in `deps` yourself.

`preserve` keeps what the user changed: a file that exists is not overwritten when the package is installed or updated, and is not deleted when it is uninstalled. Its patterns are relative to the server directory, so write `mods/MyMod/config/`, not `config/`.

## A file package

A **file package** is any package that is not a mod: it places files in the server directory. Each asset names an archive to take files from, and its `place` says which files go where:

```toml
format = 4
id = "github.com/user/MyTool"
version = "1.0.0"

[[variants.""]]
side = "server"
platform = "win-x64"
preserve = ["plugins/mytool/config.json"]

[[variants."".assets]]
type = "self"
place = [{ src = "config/", dest = "plugins/mytool/" }]

[[variants."".assets]]
type = "zip"
urls = ["https://github.com/user/MyTool/releases/download/v{{version}}/MyTool-win-x64.zip"]
place = [{ src = "MyTool.exe", dest = "MyTool.exe" }]
```

| Asset type | Takes files from |
| --- | --- |
| `self` | The package's own archive: the repository at the tag. |
| `zip` | A ZIP file that lip downloads from `urls`. |

| `place` form | `src` | Does |
| --- | --- | --- |
| File | `MyTool.exe` | Writes that file to `dest`. |
| Directory | `config/` (ends with `/`) | Writes every file below it into `dest`. |
| Glob | `plugins/*.dll` | Writes every matching file into `dest`. |

An entry can mix assets: a mod can have a `zip` asset next to its `mcmod` asset, for files outside `mods/`.

## Dependencies

`deps` maps the package id of each dependency to a version range. lip installs the dependencies with your package and refuses a set of packages whose ranges cannot all be satisfied.

```toml
[variants."".deps]
"github.com/LiteLDev/LeviLamina" = ">=1.0.0 <2.0.0"
"github.com/user/MyLib" = "^0.3.0"
"github.com/LiteLDev/LegacyScriptEngine#lua" = "^0.9.0"
```

- A key is `<package id>`, or `<package id>#<variant>` for a variant of that package.
- A range is written as in [Semver for .NET](https://github.com/WalkerCodeRanger/semver/tree/v3.0.0): `^1.2.0`, `~1.2.0`, `>=1.0.0 <2.0.0`, `1.2.*`. A bare `1.2.3` means exactly that version.
- A mod should depend on `github.com/LiteLDev/LeviLamina`, with the range of versions it works with. That is how lip knows which LeviLamina, and through it which game version, your mod supports. Keep the range honest: lip refuses to install a mod into a server whose LeviLamina is outside it.

## Variants

Use variants to offer several packages from one repository. Users choose one with `#<variant>`, such as `lip install github.com/user/MyEngine#lua`; without `#` they get the default variant `""`.

```toml
[[variants.lua]]
side = "server"
platform = "win-x64"
assets = [{ type = "self", place = [{ src = "lua/", dest = "plugins/myengine/lua/" }] }]

[[variants.quickjs]]
side = "server"
platform = "win-x64"
assets = [{ type = "self", place = [{ src = "quickjs/", dest = "plugins/myengine/quickjs/" }] }]
```

A variant name is lowercase letters, digits and `_`, with at most one `/` in the middle. Each variant installs as a package of its own, `<package id>#<variant>`, so several variants can be installed side by side. Another package depends on one with the key `<package id>#<variant>`.

## Sides and platforms

An entry is for one side and one platform. lip itself uses only `server` and `win-x64`; other launchers, such as LeviLauncher for the client, read the other entries. Add one entry per side and platform that you support, each with its own assets:

```toml
[[variants.""]]
side = "server"
platform = "win-x64"
assets = [{ type = "mcmod", urls = ["https://github.com/user/MyMod/releases/download/v{{version}}/MyMod-server-win-x64.zip"] }]

[[variants.""]]
side = "client"
platform = "win-x64"
assets = [{ type = "mcmod", urls = ["https://github.com/user/MyMod/releases/download/v{{version}}/MyMod-client-win-x64.zip"] }]
```

Two entries of one variant must not have the same side and platform.

## Hooks

`hooks` are commands that lip runs in the server directory around installing and uninstalling:

```toml
[variants."".hooks]
post_install = ["echo MyTool installed"]
pre_uninstall = ["MyTool.exe --unregister"]
```

| Hook | Runs |
| --- | --- |
| `pre_install` | Before the files are written. |
| `post_install` | After the files are written. |
| `pre_uninstall` | Before the files are deleted. |
| `post_uninstall` | After the files are deleted. |

Things to know before you use them:

- The commands are run by `cmd`, also on Linux and macOS, where `cmd` runs in Wine. Write Windows commands, and avoid `"` in them: under Wine a command with `"` does not reach `cmd` as written.
- A command that exits with a code other than 0 fails the whole operation.
- When an operation fails, lip undoes its file changes, but not what your commands did. Write commands that are safe to run twice.
- An update runs the uninstall hooks of the old version, then the install hooks of the new one.

Prefer `preserve` and `purge` over hooks where they are enough: `purge` lists extra paths to delete on uninstall, such as logs that your mod wrote.

## Testing locally

Test the package in a real server before you publish it.

::: tip
An `mcmod` or `zip` asset is always downloaded from its `urls`, also when the package comes from a directory. To test before the release exists, upload the archive somewhere reachable by HTTPS first, or use a `self` asset while you develop.
:::

In the directory of a server:

1. Check that lip reads your `lip.toml`. It prints the package file as JSON, with the templates filled in, or says what is wrong:

   ```shell
   lip pkg view ../MyMod
   ```

2. See what installing would do, without changing anything:

   ```shell
   lip install ../MyMod --dry-run
   ```

3. Install it from the directory:

   ```shell
   lip install ../MyMod
   lip list
   ```

4. Start the server with `lip run` and try the mod.
5. After you change something, read the directory again, or remove the package:

   ```shell
   lip update ../MyMod
   lip uninstall ../MyMod
   ```

`lip update` replaces an installed package only when its `version` changed. While you test, raise `version`, or uninstall and install again.

To test exactly what users of a ZIP file get, pack the package and install the archive:

```shell
lip pkg pack ../MyMod -o ../MyMod.zip
lip install ../MyMod.zip
```

The interface does the same on its [Tools](../interface/tools.md) page: **Pack ZIP…** is `lip pkg pack`, and **View package…** is `lip pkg view`.

## Publishing a version

A version of your package is a git tag named `v` followed by the version:

1. Set `version` in `lip.toml` to the new version, such as `1.0.0`, and commit.
2. Create the release assets that `urls` point to, such as `MyMod-server-win-x64.zip`.
3. Tag the commit and push the tag:

   ```shell
   git tag v1.0.0
   git push origin v1.0.0
   ```

4. Check from any directory that lip sees the version:

   ```shell
   lip pkg view github.com/user/MyMod
   ```

   `versions` lists `1.0.0`, and `manifest` is the file you tagged.

The rules that lip checks:

- The `id` in `lip.toml` must be the path of the repository. Otherwise the commit is not a package.
- At the tag `v1.0.0`, `version` must be `1.0.0`. Otherwise lip skips the tag.
- The tag must be `v` and a full [SemVer](https://semver.org/) version. `v1.0` is not a version. `v1.1.0-beta.1` is a pre-release.
- The repository must be reachable at `https://<package id>.git` without signing in.

Users then install it with `lip install github.com/user/MyMod`. Without publishing, they can still install a branch or a commit: `lip install github.com/user/MyMod@branch:main`.

## Getting listed

The **Get mods** page of the interface and [levimc.org/mods](https://levimc.org/mods/) show the mods of the LeviMC catalogue. The catalogue is kept by levimc.org, not by lip: see [levimc.org](https://levimc.org/) for how a package gets into it.

A package that is not in the catalogue installs just the same, by its id.

`info.tags` helps people find your package in such listings; lip itself ignores tags. The listings show the tags below first, with a translated name, and let users filter by them. Write the ids as they are here, in lower case, and add any other tags you like:

```toml
[info]
tags = ["management", "economy"]
```

### Recommended tags

| Tag | Name | Use it for |
| --- | --- | --- |
| `library` | Library / API | A library or API that other mods depend on, e.g. events, forms or exported functions. |
| `utility` | Utility | Everyday tools for players, e.g. inventory helpers, info displays, tooltips and recipe viewers. |
| `management` | Server management | Running a server, e.g. backups, bans, whitelists, logins, commands and land claims. |
| `game-mechanics` | Game mechanics | Changes to how the game plays, e.g. vanilla-plus features and achievements. |
| `adventure` | Content & adventure | New content to explore, e.g. world generation, terrain and biomes. |
| `visual` | Visual & HUD | What players see, e.g. HUDs, sidebars, shaders, replays and recording. |
| `economy` | Economy | Money, shops and trading. |
| `teleport` | Teleport | Teleport commands, homes, warps and waystones. |
| `anticheat` | Anti-cheat | Detecting and stopping cheats, e.g. anti-xray. |
| `optimization` | Optimization | Better performance, e.g. entity and item cleaners. |
| `bugfix` | Bug fixes | Fixes for bugs of the game. |
| `technology` | Technical & redstone | Redstone, pistons and automation. |
| `protocol` | Protocol & multi-version | The network protocol, e.g. letting clients of several game versions join. |

You need not tag the side or platform, or that the package is a LeviLamina mod: the entries and `deps` already say so.

## Packages of lip v0.x

Packages of lip v0.x have a `tooth.json` instead of `lip.toml`. lip still installs them, but no longer creates them. See [Migrating packages](migrating_packages.md) to convert one.

## See also

- [lip.toml](../concepts/lip_toml.md): every field.
- [`lip pkg init`](../reference/commands/pkg_init.md), [`lip pkg pack`](../reference/commands/pkg_pack.md), [`lip pkg view`](../reference/commands/pkg_view.md)
- [Tools](../interface/tools.md): the same actions in the interface.
- [Migrating packages](migrating_packages.md)
