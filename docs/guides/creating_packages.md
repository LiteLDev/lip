# Creating Packages

This guide walks you through creating a new package with lip. A package is an archive, usually a git repository, with a `tooth.json` at its root; see [tooth.json](../concepts/tooth_json.md) for every field.

## Using `lip init`

The easiest way to start a new package is using the `lip init` command in the root directory of your repository.

```sh
lip init
```

lip asks for the tooth path of the package, a [Go module path](https://go.dev/ref/mod#module-path) such as `github.com/user/MyTool`, and asks again until the answer is valid. It then creates `tooth.json` in the current directory:

```json
{
  "format_version": 4,
  "format_uuid": "f74a5fda-cb2a-4de8-959f-a1a76bad3102",
  "tooth": "github.com/user/MyTool",
  "version": "0.1.0",
  "variants": {
    "": [
      {
        "platform": "server",
        "runtime": "win-x64"
      }
    ]
  }
}
```

The command fails if a `tooth.json` already exists there, or if it cannot ask (when standard input or standard error is not a terminal).

The package does not install anything yet: add `dependencies`, `assets` and the other fields to its entry, as below.

## Publishing a Version

A tooth path such as `github.com/user/MyTool` is the git repository `https://github.com/user/MyTool.git`, and the `tooth` of the `tooth.json` in that repository must be exactly that path. To publish a version, push a tag named `v` followed by the [SemVer 2.0.0](https://semver.org/) version, e.g. `v1.0.0`. The `version` in the `tooth.json` at the tag must be the same version. Tags that are not exactly `v<SemVer>`, such as `v1.0`, are not versions.

## A File Package

A file package places files in the workspace. Each asset is an archive to take files from: `self` is the package's own archive (the repository at the tag), and `zip` is a ZIP file that lip downloads. Its `placements` copy a file, a directory or the files matching a glob:

```json
{
  "format_version": 4,
  "format_uuid": "f74a5fda-cb2a-4de8-959f-a1a76bad3102",
  "tooth": "github.com/user/MyTool",
  "version": "1.0.0",
  "variants": {
    "": [
      {
        "platform": "server",
        "runtime": "win-x64",
        "assets": [
          {
            "type": "self",
            "placements": [{ "src": "config/", "dest": "config/mytool/" }]
          },
          {
            "type": "zip",
            "urls": ["https://github.com/user/MyTool/releases/download/v1.0.0/MyTool-win-x64.zip"],
            "placements": [{ "src": "MyTool.exe", "dest": "MyTool.exe" }]
          }
        ],
        "preserve_files": ["config/mytool/"]
      }
    ]
  }
}
```

`preserve_files` keeps the configuration a user has changed: an existing file is not overwritten, and is not deleted on uninstall. Its globs are relative to the workspace.

## A Mod

A mod is an entry with an `mcmod` asset: a ZIP archive with the LeviLamina `manifest.json` of the mod at its root. lip writes the whole archive into `mods/<name>/`, where `<name>` is the `name` in that `manifest.json`. On a server, `plugins` is a link to `mods`, so LeviLamina finds the mod under `plugins/<name>/` as well.

Build the archive from the output directory of your build, which holds `manifest.json` and the files of the mod, with [`lip pack`](../cli/commands/pack.md), and attach it to the release:

```sh
lip pack build/ -o MyMod-server-win-x64.zip
```

Then point to it from `tooth.json`:

```json
{
  "format_version": 4,
  "format_uuid": "f74a5fda-cb2a-4de8-959f-a1a76bad3102",
  "tooth": "github.com/user/MyMod",
  "version": "1.0.0",
  "info": { "name": "My Mod" },
  "variants": {
    "": [
      {
        "platform": "server",
        "runtime": "win-x64",
        "dependencies": {
          "github.com/LiteLDev/LeviLamina": ">=1.0.0 <2.0.0",
          "github.com/user/MyLib": "^0.3.0"
        },
        "assets": [
          {
            "type": "mcmod",
            "urls": ["https://github.com/user/MyMod/releases/download/v1.0.0/MyMod-server-win-x64.zip"]
          }
        ],
        "preserve_files": ["mods/MyMod/config/"]
      }
    ]
  }
}
```

`dependencies` maps the tooth path of each dependency to a version range, e.g. `^0.3.0` or `>=1.0.0 <2.0.0`, in the syntax of [Semver for .NET](https://github.com/WalkerCodeRanger/semver/tree/v3.0.0). lip installs the dependencies, and keeps them in line with the LeviLamina `manifest.json` of the mod: for each dependency that is installed as a mod, the mod's `manifest.json` lists `{ "name": "<that mod's name>" }` in its `dependencies`, in the order of the `tooth.json` dependencies. Leave `dependencies` out of the mod's `manifest.json` and lip writes it in when it installs the mod; if you write it yourself, it must be exactly that list, or the installation fails.

On the client, mods are also installed to `mods/<name>/`, by launchers such as LeviLauncher; lip itself installs to servers only.

## Several Platforms and Runtimes

An entry applies to exactly one platform and one runtime, which must equal those of the workspace. Add an entry for each platform and runtime the package supports, each with its own assets:

```json
{
  "variants": {
    "": [
      {
        "platform": "server",
        "runtime": "win-x64",
        "assets": [{ "type": "mcmod", "urls": ["https://github.com/user/MyMod/releases/download/v1.0.0/MyMod-server-win-x64.zip"] }]
      },
      {
        "platform": "client",
        "runtime": "win-x64",
        "assets": [{ "type": "mcmod", "urls": ["https://github.com/user/MyMod/releases/download/v1.0.0/MyMod-client-win-x64.zip"] }]
      },
      {
        "platform": "client",
        "runtime": "win-arm64",
        "assets": [{ "type": "mcmod", "urls": ["https://github.com/user/MyMod/releases/download/v1.0.0/MyMod-client-win-arm64.zip"] }]
      }
    ]
  }
}
```

A workspace whose platform and runtime has no entry cannot install the package.

## Variants

Use variants to offer several packages from one repository. Users select one with `path#variant`, e.g. `lip install github.com/user/repo#lua`, and the variant `""` is used without `#`:

```json
{
  "variants": {
    "lua": [
      {
        "platform": "server",
        "runtime": "win-x64",
        "assets": [{ "type": "self", "placements": [{ "src": "lua/", "dest": "myengine/lua/" }] }]
      }
    ],
    "quickjs": [
      {
        "platform": "server",
        "runtime": "win-x64",
        "assets": [{ "type": "self", "placements": [{ "src": "quickjs/", "dest": "myengine/quickjs/" }] }]
      }
    ]
  }
}
```

Each variant installs as a package of its own, with the identity `<tooth path>#<variant>`, so several variants of one repository can be installed side by side. Another package depends on a variant with the key `<tooth path>#<variant>` in its `dependencies`.

## Testing Locally

You can install a package from your disk before publishing it, from its directory or from a ZIP file with `tooth.json` at its root:

```sh
lip install ../MyTool
lip pack ../MyTool -o MyTool.zip
lip install ./MyTool.zip
```

A `self` asset then takes its files from that directory or ZIP file.

## Existing `tooth.json` Packages

lip still installs packages whose `tooth.json` has format version 1, 2 or 3, but no longer creates them. See [Migrating Packages](migrating_packages.md) to convert one.
