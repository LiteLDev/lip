# lip self-update

Update lip itself to its latest release.

## Synopsis

```
lip self-update [--check] [-h | --help]
```

## Description

`lip self-update` finds the latest release of lip and, when it is newer than the running lip, installs it the way lip was installed.

### Checking

lip reads the [releases of lip](https://github.com/LiteLDev/lip/releases) from the GitHub API, at `https://api.github.com/repos/LiteLDev/lip/releases?per_page=100`. Drafts, and releases whose tag is not `v` followed by a SemVer version, are ignored.

The **latest release** is the one with the highest version. Pre-releases such as `1.1.0-beta.1` count only when the running lip is itself a pre-release.

When there is no release, or the latest one is not newer than the running lip, lip writes that it is up to date and changes nothing. With `--check`, lip stops here in every case.

### Installation kind

| Kind | Is | How lip knows |
| --- | --- | --- |
| `msi` | lip was installed by the Windows installer, `lip-<version>-<platform>.msi`. | The file `install.json` beside the `lip` executable holds `{"kind": "msi"}`. The installer writes it. |
| `portable` | lip was unpacked from an archive, or installed any other way. | There is no `install.json`. |

When `install.json` exists and is anything other than a JSON object whose `kind` is `"msi"`, `lip self-update` fails.

`<platform>` is the system lip runs on: `win-`, `linux-` or `osx-`, followed by `x64` or `arm64`. On any other system `lip self-update` fails before it changes anything.

### Updating

Without `--check`, when a newer release `V` exists, lip does, in order:

1. It reads the installation kind.
2. It picks the file of the release to install. When the release does not have it, or has no `SHA256SUMS` file, the command fails. The kind `msi` on a system other than Windows fails too.

   | Kind | File |
   | --- | --- |
   | `msi` | `lip-V-<platform>.msi` |
   | `portable` on Linux | `lip-V-<platform>.tar.xz` |
   | `portable` on Windows and macOS | `lip-V-<platform>.zip` |

3. It empties and creates the directory `self-update` in the [cache directory](../files.md#cache-directory).
4. It downloads `SHA256SUMS`, the checksums of the release.
5. It downloads the file into `self-update`. When `SHA256SUMS` has no line for the file, or the SHA-256 of the download differs, lip deletes the download and fails.
6. It installs the file, by the installation kind; see below.

A failure in steps 1 to 5 changes no file of the installation.

**`msi`.** lip starts `msiexec /i <file> /qn /norestart` as administrator; Windows asks you to allow it. When you decline, or `msiexec` does not start, the command fails. Otherwise lip writes that the installer runs and exits with code 0 at once, without waiting. The installer replaces lip in the background, silently and without restarting Windows. Close the other running `lip` processes so that it can.

**`portable`.** lip extracts the archive into `self-update`, then replaces the files of lip in its directory one by one: each existing file is renamed to `<file>.old`, and the new file is written in its place. When any step fails, lip puts every renamed file back and fails. When all files are written, lip deletes the `.old` files it can and the `self-update` directory.

- Files of the directory that are not in the archive are kept.
- On Windows the running `lip.exe` cannot be deleted, so `lip.exe.old` stays. The next `lip` that starts deletes it.
- On Linux, extracting the `.tar.xz` archive needs `tar` and `xz` on the system.

### After the update

The [daemon](daemon.md) that carried out an update which replaced lip or started the installer exits once it is idle, so that the next command starts the new version. While it still runs a managed server, it keeps running; see [After an update](daemon.md#after-an-update).

### Network

The list of releases and `SHA256SUMS` are always read from GitHub directly, so that a proxy cannot change what lip installs. Only the update file itself is downloaded through [`github_proxy`](../configuration.md#github-proxy).

## Options

| Option | Description |
| --- | --- |
| `--check` | Only find the latest release. Installs nothing and changes nothing. |
| `-h`, `--help` | Write the help and exit. |

## Output

Only `lip self-update --check` has a result. When a newer release exists, standard output receives its version and a line break:

```
1.1.0
```

When lip is up to date, standard output receives nothing. Everything else is a message on standard error.

So a script tells the two cases apart by whether the output is empty, not by the exit code.

## Exit status

| Code | Meaning |
| --- | --- |
| `0` | lip is up to date; or `--check` finished, whether a newer release exists or not; or the update was installed; or the installer was started. |
| not `0` | A usage error, or the check or the update failed. |

## Examples

Check for a newer version:

```shell
lip self-update --check
```

Update lip:

```shell
lip self-update
```

Update only when a newer version exists, in a POSIX shell script:

```shell
if [ -n "$(lip self-update --check 2> /dev/null)" ]; then
  lip self-update
fi
```

## See also

- [Your first server](https://levimc.org/tutorials/server/first-server/): installing lip.
- [Settings](../../interface/settings.md): **Check for updates** in the interface.
- [Files](../files.md#the-installation-directory): `install.json` and the `.old` files.
