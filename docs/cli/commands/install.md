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

1. **Package path** — `<path>` or `<path>#<variant>` (e.g. `github.com/user/repo`): the newest version.
2. **Package path with version** — `<path>@<version>` or `<path>#<variant>@<version>` (e.g. `github.com/user/repo#lua@1.0.0`): exactly that version. The version is a SemVer 2.0.0 version without the leading `v`.
3. **Package path with a git ref** — `<path>[#<variant>]@branch:<branch>`, `@tag:<tag>` or `@rev:<commit>` (e.g. `github.com/LiteLDev/LeviLamina@branch:main`): the commit that the branch head, the tag or the commit id names, whatever its version. Any branch or tag name is accepted, not only `v<SemVer>` tags; a commit id must be the full 40 hexadecimal digits. See [Git refs](#git-refs).
4. **Remote archive** — an `https://` URL of a `.mcmod` or `.zip` file (e.g. `https://example.com/package.mcmod`), optionally followed by `#<variant>`. The download is cached under the URL.
5. **Local archive** — a `.mcmod` or `.zip` file, by an absolute path or a path relative to the current directory (e.g. `./package.mcmod` or `C:\Downloads\package.zip`), optionally followed by `#<variant>`. An argument is local when it starts with `./`, `../`, `.\` or `..\`, is an absolute path, ends with `.mcmod` or `.zip`, or names an existing file. Whether the file is a ZIP archive depends on its content, not on its extension.

A local directory is not a package: `lip install ./some/dir` fails without touching the workspace. Pack the directory into a `.mcmod` file (a ZIP archive with `manifest.json` at its root) instead. Relative refs (`./...`) inside a package, such as the routes of a routing document or a `.mcmod` nested in an archive, keep working.

The dependencies of the packages are installed too. Installing a package that is already explicitly installed is an error; use [`lip update`](update.md) to change its version. A package that is installed as a dependency becomes explicitly installed.

### Git refs

A git ref pins a package path to one commit of its repository:

- `@branch:<branch>` takes the head of the branch now. [`lip update`](update.md) follows the branch to its new head.
- `@tag:<tag>` takes the commit the tag points to; any tag name works, e.g. `@tag:nightly`. `lip update` resolves the tag again.
- `@rev:<commit>` takes exactly that commit, which never changes. Abbreviated commit ids are not accepted, since resolving them would need the whole history of the repository.

The version of a package installed this way is the `version` that its `manifest.json` (or `tooth.json`) declares at that commit. Only a tag named `v<SemVer>`, such as `@tag:v1.2.0`, also requires the document to declare that version, as for plain versions. The [lock](../../concepts/lockfiles.md) records the git ref and the commit it resolved to, so the workspace keeps that commit until you update.

A package installed from a git ref also satisfies dependencies on its package path without a git ref, as long as its version is in the dependency's range: a mod requiring `github.com/LiteLDev/LeviLamina` `>=1.5.0` accepts a LeviLamina installed from `@branch:main` that declares `1.6.0`, and fails if it declares `1.4.0`.

## Options

| Option | Description |
| --- | --- |
| `-n, --dry-run` | Run without making any changes. |
| `--ignore-scripts` | Skip running `preInstall` and `postInstall` scripts (`pre_install`, `install` and `post_install` for `tooth.json` packages). |
| `--no-dependencies` | Skip installing dependencies. |
| `--rid <RID>` | The .NET RID of the workspace, e.g. `win-x64`. |

## Examples

```shell
lip install github.com/LiteLDev/LeviLamina
lip install github.com/LiteLDev/LegacyMoney@0.22.0 --rid win-x64
lip install github.com/LiteLDev/LeviLamina@branch:main
lip install github.com/LiteLDev/LegacyMoney@tag:v0.22.0
lip install https://github.com/LiteLDev/LegacyMoney/releases/download/v0.22.0/LegacyMoney-server-win-x64.mcmod
lip install ./LegacyMoney-server-win-x64.mcmod
```
