# lip view

Shows the package file (`lip.toml`) of a package.

The package argument takes the same forms as [`lip install`](install.md#arguments). For a remote repository, the newest version is shown, the given version with `@<version>` (e.g., `github.com/user/repo@1.0.0`), or the commit a git ref names (e.g., `github.com/user/repo@branch:main`). A local ZIP file or directory, or a URL, is shown as it is.

## Synopsis

```sh
lip view <PACKAGE>
```

## Arguments

| Argument | Description |
| --- | --- |
| `<PACKAGE>` | The package to view, e.g. `<path>`, `<path>@<version>`, `<path>@branch:<branch>`, `./package.zip`, `./MyTool` or an `https://` URL. |

## Examples

View a specific version:

```sh
lip view github.com/user/repo@1.0.0
```

View the latest version:

```sh
lip view github.com/user/repo
```

View the head of a branch:

```sh
lip view github.com/user/repo@branch:main
```

## Description

Reads the `lip.toml` at the root of the archive the argument names and prints it as JSON. For a remote repository, the archive is the repository at the newest version tag, at the tag of the given version, or at the commit of the git ref. It changes nothing.

A `tooth.json` of an [earlier format](../../concepts/lip_toml.md#earlier-formats) is printed as the package file lip reads from it, for the environment of the workspace: the platform `server` and the RID the [state file](../../concepts/lockfiles.md) records, or else the RID lip runs on.
