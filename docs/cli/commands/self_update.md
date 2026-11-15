# lip self-update

Update lip itself to its latest release.

## Synopsis

```shell
lip self-update [options]
```

## Options

| Option | Description |
| --- | --- |
| `--check` | Only check: print the version of the newer release, if there is one, and install nothing. |

## Description

lip reads the [releases of lip](https://github.com/LiteLDev/lip/releases) from the GitHub API. The latest release is the one of the highest version; prereleases such as `0.41.0-beta.1` count only when the running lip is itself a prerelease. When the latest release is not newer than the running lip, `lip self-update` says that lip is up to date and changes nothing.

Otherwise lip downloads the update for the way it was installed, verifies it against the `SHA256SUMS` file of the release, and installs it:

| Installed with | Update |
| --- | --- |
| `setup.exe` | Downloads `lip-<version>-<platform>-setup.exe` and runs it as administrator with `/quiet`. Windows asks you to allow it. |
| `.msi` | Downloads `lip-<version>-<platform>.msi` and runs `msiexec /i <file> /qn` as administrator. Windows asks you to allow it. |
| A portable archive | Downloads `lip-<version>-<platform>.zip` (`.tar.xz` on Linux) and replaces the files of lip in its directory in place. |

The Windows installers write `install.json` next to `lip.exe`, which tells lip which installer installed it; without that file lip treats itself as portable. lip starts an installer and exits at once; the installer finishes in the background.

A portable update keeps the other files of the directory. On Windows the running `lip.exe` cannot be deleted, so lip renames it to `lip.exe.old`, which the next run of lip deletes. If any file cannot be replaced, lip puts the old files back. On Linux, extracting the `.tar.xz` archive needs `tar` and `xz`.

Downloads are kept in the `self-update` directory of the [cache](cache_clean.md) while lip installs them. The update is downloaded through `github_proxy` when it is set ([Configuration](../configuration.md)); the GitHub API and `SHA256SUMS` are always reached directly, so that a proxy cannot change what lip installs.

## Examples

Check for a newer version:

```shell
lip self-update --check
```

Update lip:

```shell
lip self-update
```
