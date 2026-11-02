# lip pack

Pack the `manifest.json` in the current directory into a package document, or into a `.mcmod` file.

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
| `--variant <VARIANT>` | The variant to pack. Defaults to `""`, the default variant. |
| `--platform <PLATFORM>` | The platform to pack for: `server` or `client`. Defaults to `server`. |
| `--rid <RID>` | The .NET RID to pack for, e.g. `win-x64`. Defaults to the RID lip runs on. |
| `-o, --output <FILE>` | Write a `.mcmod` file to `<FILE>` instead of printing the package document. |

## Description

Reads the `manifest.json` in the current directory, which must have `"format": "bf45b1"` (see [Package Manifest](../../concepts/package_manifest.md)), and turns it into the package document of one platform and RID:

- For a meta document, lip selects the target for `--variant`, `--platform` and `--rid` as `lip install` would, and [packs](../../concepts/package_manifest.md#packing) it: the document's other keys, completed with the keys of the target, and with `platform` and `runtime` set when the target names them.
- For a package document, the result is the document itself: `--variant` must then be empty or not given, and `--platform` and `--rid` are not used.

Without `--output`, the package document is printed to standard output as JSON. With `--output`, lip writes a ZIP archive to `<FILE>`, replacing any existing file: the package document as `manifest.json` at its root, and every file below `DIR` at its path relative to `DIR`. The `manifest.json` at the root of `DIR`, everything in `.git/` and `.lip/` at the root of `DIR`, and `<FILE>` itself are left out.

The command fails and writes nothing when `manifest.json` is missing or not a valid document of format `bf45b1`, when no target or more than one matches the variant, platform and RID, or when the result is not a valid package document.

Publish each `.mcmod` file where the `source` of its target points, e.g. as a release asset. See [Creating Packages](../../guides/creating_packages.md#pointing-to-release-assets).

## Examples

Print the package document for a `win-x64` server:

```shell
lip pack --rid win-x64
```

Pack the files of `build/` into the server and client packages of a mod:

```shell
lip pack build/ --platform server --rid win-x64 -o LegacyMoney-server-win-x64.mcmod
lip pack build/ --platform client --rid win-x64 -o LegacyMoney-client-win-x64.mcmod
```
