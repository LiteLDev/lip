# lip-self-update(1)

## NAME

lip-self-update - update lip itself to its latest release

## SYNOPSIS

```
lip self-update [--check] [-h | --help]
```

## DESCRIPTION

### Terms

- **running version**: The version that `lip -v` writes ([lip(1), OPTIONS](lip.md#options)).
- **installation directory**: The directory that holds the executable file of the running `lip`.
- **installation kind**: How `lip` was installed: `"setup"` (by the Windows installer `lip-<version>-<platform>-setup.exe`), `"msi"` (by the Windows Installer package `lip-<version>-<platform>.msi`), or `"portable"` (from a portable archive, or any other way). It is the member `kind` of `install.json` in the installation directory (FILES); without that file, it is `"portable"`.
- **platform of lip**: The platform ([lip-package(7)](lip-package.md)) of the system `lip` runs on: `win-`, `linux-` or `osx-`, followed by `x64` or `arm64`, by the operating system and the processor architecture of the running process. On another system, `lip self-update` fails before it changes anything.
- **release**: An element of the JSON array in the body of a successful response to an HTTPS GET of `https://api.github.com/repos/LiteLDev/lip/releases?per_page=100`, whose member `draft` is `false` and whose member `tag_name` is `v` followed by a version ([SemVer]); that version is the version of the release. Elements of any other form are ignored. The request is sent to GitHub directly, whatever `github_proxy` ([config.toml(5)]) is.
- **asset**: An element of the member `assets` of a release, named by its member `name`. It is downloaded with an HTTPS GET of its member `browser_download_url`, following redirects: the asset `SHA256SUMS` directly, and any other asset through `github_proxy` as [config.toml(5)] defines.
- **latest release**: Of the releases whose version is not a pre-release version, or of all releases when the running version is a pre-release version, the one with the highest version by SemVer precedence.

### Checking

`lip self-update` first finds the latest release. When there is none, or its version does not have a higher precedence than the running version, `lip self-update` writes a message that `lip` is up to date, and changes nothing.

### Updating

Otherwise, without `--check`, `lip self-update` updates `lip` to the latest release, of version V, in these steps:

1. It reads the installation kind. When `install.json` exists and is not a JSON file whose value is a JSON object with the member `kind` of value `"setup"` or `"msi"`, it fails.
2. The **update asset** is the asset of the latest release named:

   | Installation kind | Update asset |
   |---|---|
   | `"setup"` | `lip-V-<platform>-setup.exe` |
   | `"msi"` | `lip-V-<platform>.msi` |
   | `"portable"` on Linux | `lip-V-<platform>.tar.xz` |
   | `"portable"` on other systems | `lip-V-<platform>.zip` |

   where `<platform>` is the platform of lip. When the latest release has no update asset or no asset named `SHA256SUMS`, it fails. When the installation kind is `"setup"` or `"msi"` and `lip` does not run on Windows, it fails.
3. It deletes the directory `self-update` of the cache directory ([lip-userspace(7)]) with every file of it that it can delete, and creates that directory.
4. It downloads the asset `SHA256SUMS`, a text file of lines `<hex>  <name>` (two spaces, or a space and `*`): `<hex>` is the lowercase or uppercase hexadecimal SHA-256 of the asset `<name>`.
5. It downloads the update asset to a file of its name in `self-update`, written atomically. When `SHA256SUMS` has no line for the update asset, or the SHA-256 of the file is not the one of its line, it deletes the file and fails.
6. It installs the update asset, by the installation kind:
   - `"setup"`: it starts the file elevated, as administrator, with the arguments `/quiet` and `/norestart`.
   - `"msi"`: it starts `msiexec` elevated, as administrator, with the arguments `/i`, the path of the file, `/qn` and `/norestart`.
   - `"portable"`: it extracts the archive to the directory `self-update/lip-V-<platform>`. Then, for each regular file F of the archive, at the path P relative to the root of the archive, it renames the file at P in the installation directory, when one exists, to `P.old`, replacing any file of that name; and writes F atomically to P in the installation directory, creating its parent directories. When a step fails, it renames each file it renamed to `P.old` back to P, deleting the file written in its place and any `P.tmp` file it wrote, and fails. Once every file is written, it deletes each `P.old` file that it can delete, and the directory `self-update`.

   For `"setup"` and `"msi"`, when the user declines the elevation, or the program does not start, it fails; otherwise `lip self-update` writes a message that the installer runs, and exits with code 0 without waiting for the installer to end. The installer replaces `lip` in the installation directory.

Files of the installation directory that are not in the archive are kept.

A failure in steps 1 to 5 changes no file of the installation directory.

### Starting

Every time `lip` starts, before it reads its arguments, it deletes the file `<executable>.old`, where `<executable>` is the path of its executable file, when that file exists and it can delete it. On Windows this deletes the executable that a portable update could not delete because it was running.

## OPTIONS

- `--check`: Only finds the latest release. When it would update `lip`, the result is the version of the latest release, followed by a line break; otherwise the result is empty. `lip self-update --check` changes nothing.

## FILES

- `install.json` in the installation directory: a JSON file, `{"kind": "setup"}` or `{"kind": "msi"}`. The Windows installers write it; `lip` never writes it.
- `self-update` in the cache directory: the files that `lip self-update` downloads and extracts.

## SEE ALSO

[lip(1)](lip.md), [config.toml(5)], [lip-userspace(7)]

[config.toml(5)]: config.toml.md
[lip-userspace(7)]: lip-userspace.md
[SemVer]: https://semver.org/spec/v2.0.0.html
