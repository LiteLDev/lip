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
  "formatVersion": 4,
  "formatUuid": "0c6b977a-64ad-441f-9a29-c7f4b196882f",
  "name": "MyMod",
  "entry": "MyMod.dll",
  "type": "native",
  "version": "0.1.0",
  "dependencies": []
}
```

This is both a LeviLamina mod manifest and a lip package. Edit `name`, `entry` and `type` to match your mod.

## Publishing a Version

A package path such as `github.com/user/MyMod` is the git repository `https://github.com/user/MyMod.git`. To publish a version, push a tag named `v` followed by the [SemVer 2.0.0](https://semver.org/) version, e.g. `v1.0.0`. The `version` in the `manifest.json` at the tag must be the same version. Tags that are not exactly `v<SemVer>`, such as `v1.0`, are ignored.

## A Mod

A mod is a concrete package whose `type` is not `"files"`. The whole archive is written to `plugins/<name>/` on a server (by lip) or `mods/<name>/` on a client (by launchers such as LeviLauncher).

Dependencies listed with `refs` are installed by lip; LeviLamina still reads `name` and `version` as usual. Use `installDependencies` for packages that only lip should install, such as LeviLamina itself:

```json
{
  "formatVersion": 4,
  "formatUuid": "0c6b977a-64ad-441f-9a29-c7f4b196882f",
  "name": "LegacyMoney",
  "entry": "LegacyMoney.dll",
  "type": "native",
  "version": "0.22.0",
  "platform": "server",
  "rid": "win-x64",
  "refs": ["github.com/LiteLDev/LegacyMoney"],
  "dependencies": [
    {
      "name": "iListenAttentively",
      "version": "^0.17.0",
      "refs": ["github.com/MiracleForest/iListenAttentively"]
    }
  ],
  "installDependencies": [
    { "version": ">=26.51.0 <26.52.0", "refs": ["github.com/LiteLDev/LeviLamina"] }
  ]
}
```

Version ranges use LeviLamina's syntax. A bare version such as `1.2.3` is rejected; write `=1.2.3`.

## A File Package

A file package (`"type": "files"`) places files in the workspace with `placements`:

```json
{
  "formatVersion": 4,
  "formatUuid": "0c6b977a-64ad-441f-9a29-c7f4b196882f",
  "name": "PeEditor",
  "type": "files",
  "version": "3.9.0",
  "rid": "win-x64",
  "placements": [{ "src": "PeEditor.exe", "dest": "PeEditor.exe" }]
}
```

## Packing a `.mcmod`

A `.mcmod` file is a ZIP archive with the `manifest.json` at its root, next to the files of the package. You can test it before publishing:

```sh
lip install ./LegacyMoney-server-win-x64.mcmod
```

## Routing to Release Assets

Built mods usually do not live in the repository. Instead, the repository's `manifest.json` at each tag is a routing document that points to the `.mcmod` files attached to the release, one per platform and RID:

```json
{
  "formatVersion": 4,
  "formatUuid": "0c6b977a-64ad-441f-9a29-c7f4b196882f",
  "name": "LegacyMoney",
  "version": "0.22.0",
  "artifacts": [
    {
      "platform": ["server", "client"],
      "rid": "win-x64",
      "refs": [
        "https://github.com/LiteLDev/LegacyMoney/releases/download/v{{version}}/{{name}}-{{platform}}-{{rid}}.mcmod"
      ]
    }
  ]
}
```

On a `win-x64` server, `lip install github.com/LiteLDev/LegacyMoney` reads this document at the newest tag and installs `LegacyMoney-server-win-x64.mcmod`.

## Variants

Use variants to offer several packages from one repository. Users select one with `path#variant`, e.g. `lip install github.com/user/repo#lua`:

```json
{
  "formatVersion": 4,
  "formatUuid": "0c6b977a-64ad-441f-9a29-c7f4b196882f",
  "artifacts": [
    { "variant": "quickjs", "refs": ["./quickjs/"] },
    { "variant": "lua", "refs": ["./lua/"] }
  ]
}
```

Each of `./quickjs/` and `./lua/` is a directory of the repository with its own concrete `manifest.json`.

## Existing `tooth.json` Packages

lip still installs packages that use `tooth.json`, but no longer creates them. See [Migrating Packages](migrating_packages.md) to convert one.
