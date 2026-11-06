# lip pack

Pack a directory into a ZIP archive.

## Synopsis

```shell
lip pack [DIR] -o <FILE>
```

## Arguments

| Argument | Description |
| --- | --- |
| `[DIR]` | The directory whose files go into the archive. Defaults to the current directory. |

## Options

| Option | Description |
| --- | --- |
| `-o, --output <FILE>` | The ZIP archive to write. Required. |

## Description

Writes to `<FILE>`, replacing any existing file, a ZIP archive holding every regular file below `DIR` at its path relative to `DIR`. `<FILE>` itself, and everything in `.git/` and `.lip/` at the root of `DIR`, are left out. The files are packed as they are: `lip pack` does not read or check `lip.yaml` or `manifest.json`.

Use it to build:

- the archive of an `mcmod` asset, from a directory with the LeviLamina `manifest.json` of the mod at its root, e.g. the output directory of your build. Attach it to the release and list its URL in the `urls` of the asset. See [A Mod](../../guides/creating_packages.md#a-mod).
- a package as a single ZIP file, from a directory with a `lip.yaml` at its root, to install it with [`lip install`](install.md) or to publish it at a URL.

## Examples

Pack the files of `build/` as the `mcmod` archive of a mod:

```shell
lip pack build/ -o MyMod-server-win-x64.zip
```

Pack the package in the current directory:

```shell
lip pack -o MyTool.zip
```
