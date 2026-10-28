# lip view

Shows the manifest of a package.

The package argument takes the same forms as [`lip install`](install.md#arguments). For a package path, the newest version is shown, the given version with `@<version>` (e.g., `github.com/user/repo@1.0.0`), or the commit a git ref names (e.g., `github.com/user/repo@branch:main`). A local archive or URL is shown as it is.

## Synopsis

```sh
lip view <PACKAGE>
```

## Arguments

| Argument | Description |
| --- | --- |
| `<PACKAGE>` | The package to view, e.g. `<path>`, `<path>@<version>`, `<path>@branch:<branch>`, `./package.mcmod` or an `https://` URL. |

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

Reads the root document of the archive the argument points to and displays it as JSON: `tooth.json` if the archive has one, otherwise `manifest.json`. For a package path, the archive is the repository at the version's tag, or at the commit of the git ref. The document is shown as it is: a routing document is not followed. In a terminal it is highlighted; when the output is redirected or piped, the document is written unchanged, so that `lip view <PACKAGE> > tooth.json` saves it.
