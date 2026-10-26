# lip view

Shows the manifest of a package.

The package argument can be a full package spec (e.g., `github.com/user/repo@1.0.0`) or just a package ID (e.g., `github.com/user/repo`). If only a package ID is provided, the latest version of the package will be resolved and its manifest shown.

## Synopsis

```sh
lip view <PACKAGE>
```

## Arguments

| Argument | Description |
| --- | --- |
| `<PACKAGE>` | The package to view, specified as `<path>@<version>` or just `<path>`. |

## Examples

View a specific version:

```sh
lip view github.com/user/repo@1.0.0
```

View the latest version:

```sh
lip view github.com/user/repo
```

## Description

Reads the package manifest (`tooth.json` at the version's tag in the package's git repository) and displays it as JSON.
