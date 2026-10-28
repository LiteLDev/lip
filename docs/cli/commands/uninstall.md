# lip uninstall

Uninstall packages.

## Synopsis

```shell
lip uninstall <PACKAGES> [options]
```

## Arguments

| Argument | Description |
| --- | --- |
| `<PACKAGES>` | The packages to uninstall, specified by package name (e.g. `LegacyMoney`, compared ignoring case) or by the `<path>` or `<path>#<variant>` they were installed from (e.g. `github.com/LiteLDev/LegacyMoney`). A `tooth.json` package installed as `<path>#<variant>` is named `<path>#<variant>`. A path without a variant matches every variant, unless it is itself the name of an installed package (such as the unlabeled variant of a `tooth.json` package). |

The names are shown by [`lip list`](list.md).

## Description

Removes the files the packages own and runs their uninstall scripts. Only explicitly installed packages can be uninstalled, unless `--no-dependencies` is given.

Dependencies that no explicitly installed package needs any more are uninstalled too. A package that other packages still need is kept, as a dependency.

## Options

| Option | Description |
| --- | --- |
| `-n, --dry-run` | Run without making any changes. |
| `--ignore-scripts` | Skip running `preUninstall` and `postUninstall` scripts (`pre_uninstall`, `uninstall` and `post_uninstall` for `tooth.json` packages). |
| `--no-dependencies` | Uninstall exactly the given packages, even dependencies, and do not remove other dependencies. This may leave the workspace broken. |
| `--rid <RID>` | The .NET RID of the workspace, e.g. `win-x64`. |
