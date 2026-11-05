# lip pack

Pack the package document in the current directory into a `.mcmod` file, or print it.

## Synopsis

```shell
lip pack [DIR] [options]
```

## Arguments

| Argument | Description |
| --- | --- |
| `[DIR]` | The directory whose files go into the `.mcmod` file with `--output`. Defaults to the current directory. |

## Options

| Option | Description |
| --- | --- |
| `-o, --output <FILE>` | Write a `.mcmod` file to `<FILE>` instead of printing the package document. |

## Description

Reads the `manifest.json` in the current directory, which must be a package document of format `bf45b1` (see [Package Manifest](../../concepts/package_manifest.md)). A meta document cannot be packed.

Without `--output`, the package document is printed to standard output as JSON. With `--output`, lip writes a ZIP archive to `<FILE>`, replacing any existing file: the package document as `manifest.json` at its root, and every file below `DIR` at its path relative to `DIR`. The `manifest.json` at the root of `DIR`, everything in `.git/` and `.lip/` at the root of `DIR`, and `<FILE>` itself are left out.

The command fails and writes nothing when `manifest.json` is missing or not a valid package document.

Publish each `.mcmod` file where the `source` of a target of your meta document points, e.g. as a release asset. See [Creating Packages](../../guides/creating_packages.md#pointing-to-release-assets).

## Examples

Check the package document and print it:

```shell
lip pack
```

Pack the files of `build/` into a `.mcmod` file:

```shell
lip pack build/ -o LegacyMoney-server-win-x64.mcmod
```
