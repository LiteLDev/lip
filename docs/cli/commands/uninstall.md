# lip uninstall

Uninstall packages.

## Synopsis

```shell
lip uninstall <PACKAGES> [options]
```

## Arguments

| Argument | Description |
| --- | --- |
| `<PACKAGES>` | The packages to uninstall, specified by identity (e.g. `github.com/LiteLDev/LegacyScriptEngine#lua`) or by the source they were installed from (e.g. `github.com/user/repo@branch:main` or `./package.zip`). A source without a variant matches every variant installed from it, and a source without a git ref matches every git ref. |

The identities are shown by [`lip list`](list.md).

## Description

Removes the files the packages own and runs their uninstall hooks. Only explicitly installed packages can be uninstalled, unless `--no-dependencies` is given.

Dependencies that no explicitly installed package needs any more are uninstalled too. A package that other packages still need is kept, as a dependency.

## Options

| Option | Description |
| --- | --- |
| `-n, --dry-run` | Run without making any changes. |
| `--ignore-scripts` | Skip running `pre_uninstall` and `post_uninstall` hooks. |
| `--no-dependencies` | Uninstall exactly the given packages, even dependencies, and do not remove other dependencies. This may leave the workspace broken. |
| `--rid <RID>` | The .NET RID of the workspace, e.g. `win-x64`. |
