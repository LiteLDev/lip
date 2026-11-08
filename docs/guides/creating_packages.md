# Creating Packages

This guide walks you through creating a new package with lip. A package is an archive, usually a git repository, with a `lip.toml` at its root; see [lip.toml](../concepts/lip_toml.md) for every field.

## Using `lip init`

The easiest way to start a new package is using the `lip init` command in the root directory of your repository.

```sh
lip init
```

lip asks for the package id, a [Go module path](https://go.dev/ref/mod#module-path) such as `github.com/user/MyTool`, and asks again until the answer is valid. It then creates `lip.toml` in the current directory:

```toml
format = 4
id = "github.com/user/MyTool"
version = "0.1.0"

[[variants.""]]
platform = "server"
runtime = "win-x64"
```

The command fails if a `lip.toml` already exists there, or if it cannot ask (when standard input or standard error is not a terminal).

The package does not install anything yet: add `deps`, `assets` and the other fields to its entry, as below.

## Publishing a Version

A package id such as `github.com/user/MyTool` is the git repository `https://github.com/user/MyTool.git`, and the `id` of the `lip.toml` in that repository must be exactly that path. To publish a version, push a tag named `v` followed by the [SemVer 2.0.0](https://semver.org/) version, e.g. `v1.0.0`. The `version` in the `lip.toml` at the tag must be the same version. Tags that are not exactly `v<SemVer>`, such as `v1.0`, are not versions.

## A File Package

A file package places files in the workspace. Each asset is an archive to take files from: `self` is the package's own archive (the repository at the tag), and `zip` is a ZIP file that lip downloads. Its `place` copy a file, a directory or the files matching a glob:

```toml
format = 4
id = "github.com/user/MyTool"
version = "1.0.0"

[[variants.""]]
platform = "server"
runtime = "win-x64"
preserve = ["config/mytool/"]

[[variants."".assets]]
type = "self"
place = [{ src = "config/", dest = "config/mytool/" }]

[[variants."".assets]]
type = "zip"
urls = ["https://github.com/user/MyTool/releases/download/v1.0.0/MyTool-win-x64.zip"]
place = [{ src = "MyTool.exe", dest = "MyTool.exe" }]
```

`preserve` keeps the configuration a user has changed: an existing file is not overwritten, and is not deleted on uninstall. Its globs are relative to the workspace.

## A Mod

A mod is an entry with an `mcmod` asset: a ZIP archive with the LeviLamina `manifest.json` of the mod at its root. lip writes the whole archive into `mods/<name>/`, where `<name>` is the `name` in that `manifest.json`; lip reads nothing else of `manifest.json` and installs it unchanged.

Build the archive from the output directory of your build, which holds `manifest.json` and the files of the mod, with [`lip pack`](../cli/commands/pack.md), and attach it to the release:

```sh
lip pack build/ -o MyMod-server-win-x64.zip
```

Then point to it from `lip.toml`:

```toml
format = 4
id = "github.com/user/MyMod"
version = "1.0.0"

[info]
name = "My Mod"

[[variants.""]]
platform = "server"
runtime = "win-x64"
preserve = ["mods/MyMod/config/"]

[variants."".deps]
"github.com/LiteLDev/LeviLamina" = ">=1.0.0 <2.0.0"
"github.com/user/MyLib" = "^0.3.0"

[[variants."".assets]]
type = "mcmod"
urls = ["https://github.com/user/MyMod/releases/download/v1.0.0/MyMod-server-win-x64.zip"]
```

`deps` maps the package id of each dependency to a version range, e.g. `^0.3.0` or `>=1.0.0 <2.0.0`, in the syntax of [Semver for .NET](https://github.com/WalkerCodeRanger/semver/tree/v3.0.0). lip installs the dependencies. It reads only the `name` of the mod's LeviLamina `manifest.json` and installs that file unchanged, so list the mods your mod depends on in its `deps` yourself, as LeviLamina expects.

On the client, mods are also installed to `mods/<name>/`, by launchers such as LeviLauncher; lip itself installs to servers only.

## Several Platforms and Runtimes

An entry applies to exactly one platform and one runtime, which must equal those of the workspace. Add an entry for each platform and runtime the package supports, each with its own assets:

```toml
[[variants.""]]
platform = "server"
runtime = "win-x64"
assets = [{ type = "mcmod", urls = ["https://github.com/user/MyMod/releases/download/v1.0.0/MyMod-server-win-x64.zip"] }]

[[variants.""]]
platform = "client"
runtime = "win-x64"
assets = [{ type = "mcmod", urls = ["https://github.com/user/MyMod/releases/download/v1.0.0/MyMod-client-win-x64.zip"] }]

[[variants.""]]
platform = "client"
runtime = "win-arm64"
assets = [{ type = "mcmod", urls = ["https://github.com/user/MyMod/releases/download/v1.0.0/MyMod-client-win-arm64.zip"] }]
```

A workspace whose platform and runtime has no entry cannot install the package.

## Variants

Use variants to offer several packages from one repository. Users select one with `path#variant`, e.g. `lip install github.com/user/repo#lua`, and the variant `""` is used without `#`:

```toml
[[variants.lua]]
platform = "server"
runtime = "win-x64"
assets = [{ type = "self", place = [{ src = "lua/", dest = "myengine/lua/" }] }]

[[variants.quickjs]]
platform = "server"
runtime = "win-x64"
assets = [{ type = "self", place = [{ src = "quickjs/", dest = "myengine/quickjs/" }] }]
```

Each variant installs as a package of its own, with the identity `<package id>#<variant>`, so several variants of one repository can be installed side by side. Another package depends on a variant with the key `<package id>#<variant>` in its `deps`.

## Testing Locally

You can install a package from your disk before publishing it, from its directory or from a ZIP file with `lip.toml` at its root:

```sh
lip install ../MyTool
lip pack ../MyTool -o MyTool.zip
lip install ./MyTool.zip
```

A `self` asset then takes its files from that directory or ZIP file.

## Packages of lip v0.x

lip still installs packages of lip v0.x, which have a `tooth.json` of format version 1, 2 or 3 instead of `lip.toml`, but no longer creates them. See [Migrating Packages](migrating_packages.md) to convert one.
