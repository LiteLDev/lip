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

Removes the files the packages own and runs their uninstall hooks. Only explicitly installed packages can be uninstalled.

Dependencies that no explicitly installed package needs any more are uninstalled too. A package that other packages still need is kept, as a dependency.

## Options

| Option | Description |
| --- | --- |
| `-n, --dry-run` | Run without making any changes. |
| `--platform <RID>` | The platform of the workspace [environment](../../concepts/lip_toml.md#environment), a .NET RID such as `linux-x64`. Without it, the platform the state file records, or else `win-x64`, whatever system lip runs on. |
