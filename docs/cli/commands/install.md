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
3. **Local directory** — e.g. `./path/to/package/`.
4. **Local archive** — a ZIP archive such as `./package.mcmod`.
5. **Remote URL** — an `https://` URL of a ZIP archive (e.g. `https://example.com/package.mcmod`).

Any argument naming an existing file or directory is local, even without `./`. A local package inside the workspace is recorded as a path relative to the workspace; one outside it is opened from its parent directory. A local package or URL may also be followed by `#<variant>`.

The dependencies of the packages are installed too. Installing a package that is already explicitly installed is an error; use [`lip update`](update.md) to change its version. A package that is installed as a dependency becomes explicitly installed.

## Options

| Option | Description |
| --- | --- |
| `-n, --dry-run` | Run without making any changes. |
| `--ignore-scripts` | Skip running `preInstall` and `postInstall` scripts (`pre_install`, `install` and `post_install` for `tooth.json` packages). |
| `--no-dependencies` | Skip installing dependencies. |
| `--platform <PLATFORM>` | The platform of the workspace: `server` or `client`. See [Environment](../../concepts/package_manifest.md#environment). |
| `--rid <RID>` | The .NET RID of the workspace, e.g. `win-x64`. |

## Examples

```shell
lip install github.com/LiteLDev/LeviLamina
lip install github.com/LiteLDev/LegacyMoney@0.22.0 --platform server
lip install ./LegacyMoney-server-win-x64.mcmod
```
