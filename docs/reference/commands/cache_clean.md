# lip cache clean

Delete the cache.

## Synopsis

```
lip cache clean [-h | --help]
```

## Description

`lip cache clean` deletes the [cache directory](../files.md#cache-directory): `%LOCALAPPDATA%\lip\Cache` on Windows, and `$XDG_CACHE_HOME/lip` elsewhere, which is `~/.cache/lip` by default.

The cache holds only what lip downloaded and can download again. Workspaces, installed packages and the configuration are not affected. The Wine that lip installed is in the data directory, not in the cache, and stays.

A lip command that is writing to the cache at the same moment may fail. Run `lip cache clean` while no other lip command runs.

## Options

| Option | Description |
| --- | --- |
| `-h`, `--help` | Write the help and exit. |

## Output

`lip cache clean` has no result: it writes nothing to standard output. The final line is a message on standard error.

## Exit status

| Code | Meaning |
| --- | --- |
| `0` | The cache is deleted. |
| not `0` | A usage error, or the directory could not be deleted. |

## Examples

```shell
lip cache clean
```

## See also

- [Files](../files.md#cache-directory): what the cache holds.
- [Settings](../../interface/settings.md): **Clear cache** in the interface.
- [Troubleshooting](../../guides/troubleshooting.md)
