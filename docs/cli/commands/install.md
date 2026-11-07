# lip install

Install packages.

## Synopsis

```shell
lip install <PACKAGES> [options]
```

## Arguments

| Argument | Description |
| --- | --- |
| `<PACKAGES>` | The packages to install. |

Each package argument takes one of the following forms:

1. **Repository** — `<path>` or `<path>#<variant>` (e.g. `github.com/user/repo`): the git repository `https://<path>.git`, newest version first.
2. **Repository with version** — `<path>@<version>` or `<path>#<variant>@<version>` (e.g. `github.com/user/repo#lua@1.0.0`): exactly that version, the commit of the tag `v<version>`. The version is a SemVer 2.0.0 version without the leading `v`. Like a git ref, it pins the package: see [Git refs](#git-refs).
3. **Repository with a git ref** — `<path>[#<variant>]@branch:<branch>`, `@tag:<tag>` or `@rev:<commit>` (e.g. `github.com/LiteLDev/LeviLamina@branch:main`): the commit that the branch head, the tag or the commit id names, whatever its version. Any branch or tag name is accepted, not only `v<SemVer>` tags; a commit id must be the full 40 hexadecimal digits. See [Git refs](#git-refs).
4. **URL** — an `https://` URL of a ZIP file (e.g. `https://example.com/package.zip`), optionally followed by `#<variant>`. Other schemes, such as `http://` and `file://`, are not accepted.
5. **Local path** — a ZIP file or a directory on your disk, by an absolute path or a path relative to the current directory (e.g. `./package.zip`, `C:\Downloads\package.zip` or `../MyTool`), optionally followed by `#<variant>`. An argument is local when it starts with `./`, `../`, `.\` or `..\`, is `.` or `..`, is an absolute path, or contains no `@` and either ends with `.zip` (ignoring case) or names an existing file or directory. A local file must be a ZIP archive, whatever its name; a local directory is taken as the archive of its contents.

Every package must have a `lip.yaml` at its root (see [lip.yaml](../../concepts/lip_yaml.md)) with an entry of the requested variant for the platform and RID of the workspace.

The dependencies of the packages are installed too. Installing a package that is already explicitly installed is an error; use [`lip update`](update.md) to change its version. Two arguments that name the same identity are an error too. A package that is installed as a dependency becomes explicitly installed, keeping its version when that version satisfies the argument.

### Git refs

A git ref pins a repository to one commit:

- `@<version>` takes the commit of the tag `v<version>`. `lip update` keeps that version.
- `@branch:<branch>` takes the head of the branch now. [`lip update`](update.md) follows the branch to its new head.
- `@tag:<tag>` takes the commit the tag points to; any tag name works, e.g. `@tag:nightly`. `lip update` resolves the tag again.
- `@rev:<commit>` takes exactly that commit, which never changes. Abbreviated commit ids are not accepted, since resolving them would need the whole history of the repository.

The version of a package installed this way is the `version` that its `lip.yaml` declares at that commit. Only a tag named `v<SemVer>`, such as `@tag:v1.2.0`, gives the package the tag's version, as for plain versions: the `lip.yaml` there must declare that version (a `tooth.json` of an earlier format takes the tag's version). The [workspace file](../../concepts/lockfiles.md) records the source, the commit it resolved to and its package file, so the workspace keeps that commit until you update.

A package installed from a git ref also satisfies dependencies on its package id and variant without a git ref, as long as its version is in the dependency's range and the repository has a version tag that lip could install for the dependency: a mod requiring `github.com/LiteLDev/LeviLamina` `>=1.5.0` accepts a LeviLamina installed from `@branch:main` that declares `1.6.0`, and fails if it declares `1.4.0`, or if no version tag of LeviLamina is `1.5.0` or higher.

## Options

| Option | Description |
| --- | --- |
| `-n, --dry-run` | Run without making any changes. |
| `--ignore-scripts` | Do not run package `hooks`, including the uninstall hooks of packages the operation removes or replaces. |
| `--no-dependencies` | Install exactly the first candidate of each argument, replacing an installed package of the same identity; no dependencies are installed and no other package is uninstalled. An installed package with the same identity, version and commit is only marked explicitly installed. This may leave the workspace broken. |
| `--rid <RID>` | The .NET RID of the workspace, e.g. `win-x64`. |

## Examples

```shell
lip install github.com/LiteLDev/LeviLamina
lip install github.com/LiteLDev/LegacyScriptEngine#lua
lip install github.com/user/repo@1.0.0 --rid win-x64
lip install github.com/LiteLDev/LeviLamina@branch:main
lip install github.com/user/repo@tag:v1.0.0
lip install https://example.com/package.zip
lip install ./package.zip
lip install ../MyTool
```
