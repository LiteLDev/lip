# lip pkg view

Shows the package file (`lip.toml`) of a package and the versions of its repository.

## Synopsis

```sh
lip pkg view <PACKAGE>
```

## Arguments

| Argument | Description |
| --- | --- |
| `<PACKAGE>` | The package to view, in the same forms as for [`lip install`](install.md#arguments), e.g. `<path>`, `<path>@<version>`, `<path>@branch:<branch>`, `./package.zip`, `./MyTool` or an `https://` URL. |

## Description

Prints, as JSON, an object with two members:

```json
{ "manifest": { ... }, "versions": ["<version>", ...] }
```

- **`manifest`**: The `lip.toml` at the root of the archive the argument names, with its [templates](../../concepts/lip_toml.md#templates) rendered. For a remote repository, the archive is the repository at the newest version tag, at the tag of the given version with `@<version>`, or at the commit of a git ref. A local ZIP file or directory, or a URL, is read as it is. If that archive cannot be obtained or is not a package, the command fails.
- **`versions`**: For a remote repository, its versions: the tags named exactly `v<SemVer 2.0.0>` (e.g. `v1.2.3`), without the leading `v`, lowest first. Tags such as `v1.2` are not versions. The variant and the git ref of the argument do not change the list. For a URL or a local path, an empty array.

A `tooth.json` of an [earlier format](../../concepts/lip_toml.md#earlier-formats) is shown as the package file lip reads from it for the [environment](../../concepts/lip_toml.md#environment) of the workspace in the current directory: the side `server` and the platform its [state file](../../concepts/lockfiles.md) records, or `win-x64` when there is no state file. A state file that lip cannot read fails the command, as it fails `lip install`.

The output is always JSON; there is no `--json` option. The command changes nothing.

## Examples

View the newest version, and list the versions:

```sh
lip pkg view github.com/LiteLDev/LeviLamina
```

View a specific version:

```sh
lip pkg view github.com/user/repo@1.0.0
```

View the head of a branch:

```sh
lip pkg view github.com/user/repo@branch:main
```
