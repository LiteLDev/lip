# lip versions

Shows available versions for a package.

Versions are read from the git tags of the package repository (`https://<package path>.git`). Only tags named exactly `v<SemVer 2.0.0>` (e.g., `v1.2.3`) are versions; tags such as `v1.2` are ignored. The versions are listed lowest first, without the leading `v`.

## Synopsis

```sh
lip versions <PACKAGE> [options]
```

## Arguments

| Argument | Description |
| --- | --- |
| `<PACKAGE>` | The package to show versions for, specified as `<path>` (e.g. `github.com/user/repo`). |

## Options

| Option | Description |
| --- | --- |
| `--json` | Output as JSON. |

## Examples

List available versions for a package:

```sh
lip versions github.com/user/repo
```
