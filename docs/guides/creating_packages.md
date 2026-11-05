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
  "id": "MyMod",
  "version": "0.1.0",
  "dependencies": {},
  "mod": { "loader": "native", "entry": "MyMod.dll" }
}
```

This is both a LeviLamina mod manifest and a lip package. Edit `id` and `mod` to match your mod.

## Publishing a Version

A package path such as `github.com/user/MyMod` is the git repository `https://github.com/user/MyMod.git`. To publish a version, push a tag named `v` followed by the [SemVer 2.0.0](https://semver.org/) version, e.g. `v1.0.0`. The `version` in the `manifest.json` at the tag must be the same version. Tags that are not exactly `v<SemVer>`, such as `v1.0`, are ignored.

## A Mod

A mod is a package with a `mod` key. The whole archive is written to `mods/<id>/`, on a server (by lip) as on a client (by launchers such as LeviLauncher). On a server, `plugins` is a link to `mods`, so LeviLamina finds the mod under `plugins/<id>/` as well.

`dependencies` maps the id of each dependency to a version range, or to an object with `version` and `source`. Dependencies with a `source` are installed by lip; LeviLamina reads all of them and requires each to be a loaded mod. Add `"mod": false` to a dependency that is not a mod LeviLamina loads, such as a file package:

```json
{
  "format": "bf45b1",
  "id": "LegacyMoney",
  "version": "0.22.0",
  "platform": "server",
  "runtime": "win-x64",
  "info": { "name": "Legacy Money", "authors": ["LiteLDev"] },
  "dependencies": {
    "LeviLamina": { "version": ">=26.51.0 <26.52.0", "source": "github.com/LiteLDev/LeviLamina" },
    "iListenAttentively": { "version": "^0.17.0", "source": "github.com/MiracleForest/iListenAttentively" }
  },
  "mod": { "loader": "native", "entry": "LegacyMoney.dll" },
  "preserve": ["config/", "data/"]
}
```

Version ranges use the syntax of [node-semver](https://github.com/npm/node-semver#ranges), e.g. `^0.17.0` or `>=26.51.0 <26.52.0`. A bare version such as `1.2.3` matches exactly that version.

## A File Package

A file package (without `mod`) places files in the workspace with `files`. Each entry copies a file, a directory or the files matching a glob, from the package itself or from an archive listed in `archives` that lip downloads:

```json
{
  "format": "bf45b1",
  "id": "PeEditor",
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

Built mods usually do not live in the repository. Instead, the repository's `manifest.json` at each tag is a meta document whose `targets` point to the `.mcmod` files attached to the release, one per platform and RID. Its other keys are the template of those packages:

```json
{
  "format": "bf45b1",
  "id": "LegacyMoney",
  "version": "0.22.0",
  "info": { "name": "Legacy Money", "authors": ["LiteLDev"] },
  "dependencies": {
    "LeviLamina": { "version": ">=26.51.0 <26.52.0", "source": "github.com/LiteLDev/LeviLamina" },
    "iListenAttentively": { "version": "^0.17.0", "source": "github.com/MiracleForest/iListenAttentively" }
  },
  "mod": { "loader": "native", "entry": "LegacyMoney.dll" },
  "preserve": ["config/", "data/"],
  "targets": [
    {
      "platform": ["server", "client"],
      "runtime": "win-x64",
      "source": "https://github.com/LiteLDev/LegacyMoney/releases/download/v{{version}}/{{id}}-{{platform}}-{{runtime}}.mcmod"
    }
  ]
}
```

On a `win-x64` server, `lip install github.com/LiteLDev/LegacyMoney` reads this document at the newest tag and installs `LegacyMoney-server-win-x64.mcmod`.

To build the `.mcmod` of each platform and RID, run `lip pack` in the repository: it completes the template with the selected target into a package document, and packs it with your files. A target may also override keys of the template, or remove them with `null`:

```sh
lip pack build/ --platform server --rid win-x64 -o LegacyMoney-server-win-x64.mcmod
lip pack build/ --platform client --rid win-x64 -o LegacyMoney-client-win-x64.mcmod
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
