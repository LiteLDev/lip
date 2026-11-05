# Creating Packages

This guide walks you through creating a new package with lip. A package is a `manifest.json` at the root of an archive; see [Package Manifest](../concepts/package_manifest.md) for every field.

## Using `lip init`

The easiest way to start a new project is using the `lip init` command.

```sh
lip init
```

This command creates a `manifest.json` in your current directory. It fails if a `manifest.json` already exists there.

For a directory named `MyMod`, the generated `manifest.json` looks like this:

```json
{
  "format": "bf45b1",
  "name": "MyMod",
  "version": "0.1.0",
  "dependencies": [],
  "entry": "MyMod.dll",
  "type": "native"
}
```

This is both a LeviLamina mod manifest and a lip package. Edit `name`, `entry` and `type` to match your mod.

## Publishing a Version

A package path such as `github.com/user/MyMod` is the git repository `https://github.com/user/MyMod.git`. To publish a version, push a tag named `v` followed by the [SemVer 2.0.0](https://semver.org/) version, e.g. `v1.0.0`. The `manifest.json` at the tag is a meta document (see [Pointing to Release Assets](#pointing-to-release-assets)); its `version`, if it has one, must be the same version. Tags that are not exactly `v<SemVer>`, such as `v1.0`, are ignored.

## A Mod

A mod is a package with the `entry` and `type` keys. The whole archive is written to `mods/<name>/`, on a server (by lip) as on a client (by launchers such as LeviLauncher). On a server, `plugins` is a link to `mods`, so LeviLamina finds the mod under `plugins/<name>/` as well.

`dependencies` is an array of objects with the `name` of the package depended on, and optionally a `version` range and a `source`. Dependencies with a `source` are installed by lip; LeviLamina reads all of them. `conflicts` lists, in the same way, packages that must not be installed together with this one; a conflict without `version` matches every version. Other keys that LeviLamina reads, such as `description`, `author`, `passive`, `loadBefore` and `loadAfter`, go at the top level, as LeviLamina defines them; lip ignores them:

```json
{
  "format": "bf45b1",
  "name": "LegacyMoney",
  "version": "0.22.0",
  "platform": "server",
  "runtime": "win-x64",
  "entry": "LegacyMoney.dll",
  "type": "native",
  "author": "LiteLDev",
  "dependencies": [
    { "name": "LeviLamina", "version": ">=26.51.0 <26.52.0", "source": "github.com/LiteLDev/LeviLamina" },
    { "name": "iListenAttentively", "version": "^0.17.0", "source": "github.com/MiracleForest/iListenAttentively" }
  ],
  "preserve": ["config/", "data/"]
}
```

Version ranges use the syntax of [node-semver](https://github.com/npm/node-semver#ranges), e.g. `^0.17.0` or `>=26.51.0 <26.52.0`. A bare version such as `1.2.3` matches exactly that version.

## A File Package

A file package (without `entry`) places files in the workspace with `files`. Each entry copies a file, a directory or the files matching a glob, from the package itself or from an archive listed in `archives` that lip downloads:

```json
{
  "format": "bf45b1",
  "name": "PeEditor",
  "version": "3.9.0",
  "runtime": "win-x64",
  "files": [{ "src": "PeEditor.exe", "dest": "PeEditor.exe" }]
}
```

## Packing a `.mcmod`

A `.mcmod` file is a ZIP archive with the `manifest.json` at its root, next to the files of the package. [`lip pack`](../cli/commands/pack.md) builds one from the `manifest.json` in the current directory and the files of a directory, e.g. the output directory of your build:

```sh
lip pack build/ -o LegacyMoney-server-win-x64.mcmod
```

You can test it before publishing:

```sh
lip install ./LegacyMoney-server-win-x64.mcmod
```

## Pointing to Release Assets

Built mods usually do not live in the repository. Instead, the repository's `manifest.json` at each tag is a meta document whose `targets` point to the `.mcmod` files attached to the release, one per platform and RID:

```json
{
  "format": "bf45b1",
  "version": "0.22.0",
  "targets": [
    {
      "platform": "server",
      "runtime": "win-x64",
      "source": "https://github.com/LiteLDev/LegacyMoney/releases/download/v{{version}}/LegacyMoney-server-win-x64.mcmod"
    },
    {
      "platform": "client",
      "runtime": "win-x64",
      "source": "https://github.com/LiteLDev/LegacyMoney/releases/download/v{{version}}/LegacyMoney-client-win-x64.mcmod"
    }
  ]
}
```

<span v-pre>`{{version}}` is replaced by the version of the tag. A `source` may also contain `{{platform}}` and `{{runtime}}`, replaced by the platform and RID of the workspace.</span>

On a `win-x64` server, `lip install github.com/LiteLDev/LegacyMoney` reads this document at the newest tag and installs `LegacyMoney-server-win-x64.mcmod`.

Each `.mcmod` file has its own package document. Keep one for each target in the repository, e.g. in `targets/0/manifest.json` and `targets/1/manifest.json` (the one `lip init` created can be moved there), with `"platform": "server"` and `"platform": "client"` respectively, and run `lip pack` in its directory to pack it with your build output:

```sh
cd targets/0
lip pack ../../build/ -o ../../LegacyMoney-server-win-x64.mcmod
cd ../1
lip pack ../../build/ -o ../../LegacyMoney-client-win-x64.mcmod
```

## Variants

Use variants to offer several packages from one repository. Users select one with `path#variant`, e.g. `lip install github.com/user/repo#lua`:

```json
{
  "format": "bf45b1",
  "targets": [
    { "variant": "quickjs", "source": "./quickjs/" },
    { "variant": "lua", "source": "./lua/" }
  ]
}
```

Each of `./quickjs/` and `./lua/` is a directory of the repository with its own package `manifest.json`.

## Existing `tooth.json` Packages

lip still installs packages that use `tooth.json`, but no longer creates them. See [Migrating Packages](migrating_packages.md) to convert one.
