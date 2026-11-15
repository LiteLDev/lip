# lip pkg pack

Pack a directory into a ZIP archive.

## Synopsis

```
lip pkg pack [<DIR>] (-o | --output) <FILE> [-h | --help]
```

## Description

`lip pkg pack` writes to `<FILE>`, replacing it when it exists, a ZIP archive that holds every regular file below `<DIR>` at its path relative to `<DIR>`.

`<DIR>` is the directory to pack; without it, the current working directory. When it is not an existing directory, the command fails and writes nothing.

Left out of the archive:

- `<FILE>` itself, when it is below `<DIR>`;
- everything below `.git/` and `.lip/` at the root of `<DIR>`.

The files are packed as they are. `lip pkg pack` does not read or check `lip.toml` or `manifest.json`, so it packs any directory.

A ZIP archive cannot hold `\` in a name. On Linux and macOS the command fails, without writing `<FILE>`, when the name of a file to pack, or of a directory above it, contains `\`.

Use it to build:

- **the archive of an `mcmod` asset**: pack the directory that has the LeviLamina `manifest.json` of the mod at its root, such as the output directory of your build. Attach the archive to your release and put its URL in the `urls` of the asset.
- **a package as one ZIP file**: pack a directory that has `lip.toml` at its root, to install it with [`lip install`](install.md) or to publish it at a URL.

`lip pkg pack` is not a workspace operation.

## Options

| Option | Description |
| --- | --- |
| `-o <FILE>`, `--output <FILE>` | The path of the ZIP archive to write. Required. |
| `-h`, `--help` | Write the help and exit. |

## Output

`lip pkg pack` has no result: it writes nothing to standard output. The final line is a message on standard error.

## Exit status

| Code | Meaning |
| --- | --- |
| `0` | The archive was written. |
| not `0` | A usage error, such as a missing `-o`; `<DIR>` is not a directory; or a file cannot be read or written. |

## Examples

Pack the files of `build/` as the `mcmod` archive of a mod:

```shell
lip pkg pack build/ -o MyMod-server-win-x64.zip
```

Pack the package in the current directory, then install it into a server to test it:

```shell
lip pkg pack -o ../MyTool.zip
cd ../server
lip install ../MyTool.zip
```

## See also

- [Creating packages](../../authors/creating_packages.md)
- [Tools](../../interface/tools.md): **Pack ZIP…** in the interface.
- [`lip pkg view`](pkg_view.md), [`lip install`](install.md)
