# lip uninstall

Uninstall packages from the workspace.

## Synopsis

```
lip uninstall <PACKAGES>... [-n | --dry-run] [-h | --help]
```

## Description

`lip uninstall` uninstalls the installed packages that `<PACKAGES>` name, as one [workspace operation](../cli.md#workspace-operations).

Each of `<PACKAGES>` names installed packages by the first of these rules that matches any installed package:

1. **By identity.** The operand equals the identity that [`lip list`](list.md) shows without the version, such as `github.com/LiteLDev/LegacyScriptEngine#lua`.
2. **By source.** The operand, read as a [package argument](../cli.md#package-arguments), equals the source the package was installed from, such as `github.com/user/repo@branch:main` or `./package.zip`. Repository names are compared ignoring case. An operand without a variant matches every variant, and an operand without a ref matches every ref.

A name without `/`, such as `LeviLamina`, is a repository of LiteLDev, as for `lip install`.

What it does:

- The named packages stop being explicitly installed.
- Every package that no remaining explicitly installed package needs is uninstalled: its `pre_uninstall` hooks run, the files it owns are deleted except those its `preserve` keeps, the paths of its `purge` are deleted, and its `post_uninstall` hooks run.
- A named package that another explicitly installed package still needs stays installed, as a dependency package.

What it refuses, before anything changes:

- An operand that names no installed package.
- A named package that is not explicitly installed. Uninstall the package that needs it instead.

The game and the loader are uninstalled like any other package. A workspace whose game is uninstalled has no game: set it up again with [`lip setup`](setup.md) before you install mods or run the server. Uninstalling the game keeps `server.properties`, `allowlist.json` and `permissions.json`; see [The server files](../files.md#the-server-files).

## Options

| Option | Description |
| --- | --- |
| `-n`, `--dry-run` | A [dry run](../cli.md#dry-run). The messages name the packages that would be uninstalled or marked no longer explicitly installed. Nothing is changed. |
| `-h`, `--help` | Write the help and exit. |

## Output

`lip uninstall` has no result: it writes nothing to standard output. Progress and the final line are messages on standard error.

## Exit status

| Code | Meaning |
| --- | --- |
| `0` | The packages were uninstalled, or the dry run finished. |
| not `0` | A usage error, or the command failed. An operation that failed is undone. |

## Examples

Uninstall a package by its identity:

```shell
lip uninstall github.com/LiteLDev/LegacyScriptEngine#lua
```

See what uninstalling two packages would remove:

```shell
lip uninstall github.com/user/repo github.com/user/other --dry-run
```

Uninstall a package that was installed from a local ZIP file:

```shell
lip uninstall ./package.zip
```

## See also

- [Managing packages](../../guides/managing_packages.md)
- [Mods](../../interface/mods.md): uninstalling mods in the interface.
- [`lip list`](list.md), [`lip install`](install.md), [`lip setup`](setup.md)
