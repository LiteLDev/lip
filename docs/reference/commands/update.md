# lip update

Update installed packages.

## Synopsis

```
lip update [<PACKAGES>...] [-n | --dry-run] [-h | --help]
```

## Description

`lip update` requests installed packages again and replaces those that changed, as one [workspace operation](../cli.md#workspace-operations).

**Without operands**, every explicitly installed package is requested again from the source it was installed from, which the [state file](../../concepts/state_file.md) records:

| Installed from | `lip update` installs |
| --- | --- |
| `<repo>` | The highest version tag. |
| `<repo>@<version>` | The same version: nothing changes. |
| `<repo>@branch:<name>` | The commit the branch points to now. |
| `<repo>@tag:<name>` | The commit the tag points to now. |
| `<repo>@rev:<commit>` | The same commit: nothing changes. |
| An `https://` URL | The ZIP archive, downloaded again. |
| A local ZIP file or directory | The same path, read again. When it no longer exists, the package is kept as it is. |

**With operands**, each of `<PACKAGES>` is a [package argument](../cli.md#package-arguments), in the forms that [`lip install`](install.md) takes, and only those packages are requested again, from the source you give. This is how you change the source of a package:

- `lip update github.com/user/repo@2.0.0` moves the package to version 2.0.0, up or down.
- `lip update github.com/user/repo` moves a package that was installed from a version or a ref back to the highest version tag.

The requested packages become explicitly installed.

In both cases:

- A package is replaced only when the package obtained differs from the installed one in identity, version or commit. A package from a URL or a local path has no commit, so it is replaced only when its version changed.
- Replacing a package uninstalls the old one, then installs the new one.
- Dependency packages are updated only as far as the new packages require.

What it refuses, before anything changes:

- An operand whose package is not installed. Use [`lip install`](install.md).
- An operand that names `github.com/LiteLDev/bds` or `github.com/LiteLDev/LeviLamina`. The message points to `lip setup`.
- An update that cannot be done with the game and the loader as they are.

`lip update` keeps the game and the loader as they are: without operands it does not request them again. To change the game version or the LeviLamina version, run [`lip setup`](setup.md).

## Options

| Option | Description |
| --- | --- |
| `-n`, `--dry-run` | A [dry run](../cli.md#dry-run). The messages name the packages that would be installed, uninstalled or marked explicitly installed. Nothing is changed. |
| `-h`, `--help` | Write the help and exit. |

## Output

`lip update` has no result: it writes nothing to standard output. Progress and the final line are messages on standard error.

## Exit status

| Code | Meaning |
| --- | --- |
| `0` | The update was carried out, also when nothing needed to change, or the dry run finished. |
| not `0` | A usage error, or the update failed. An update that failed is undone. |

## Examples

See what updating every package would do, then do it:

```shell
lip update --dry-run
lip update
```

Update one package to its newest version:

```shell
lip update github.com/user/repo
```

Move a package to a given version:

```shell
lip update github.com/user/repo@1.2.0
```

## See also

- [Managing packages](../../guides/managing_packages.md)
- [Mods](../../interface/mods.md): updating mods in the interface.
- [`lip install`](install.md), [`lip setup`](setup.md), [`lip list`](list.md)
