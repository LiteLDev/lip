# install.sh(1)

## NAME

install.sh - install lip on Linux or macOS

## SYNOPSIS

```
curl -fsSL https://levimc.org/lip/install.sh | sh
sh install.sh
```

## DESCRIPTION

`install.sh` is a script for the POSIX shell, at the root of the repository `github.com/LiteLDev/lip`. The same file is served at `https://levimc.org/lip/install.sh`. It installs `lip` for the user who runs it, without administrator rights, and never runs `sudo`. It reads no arguments, and its standard input may be the script itself.

It writes its messages to the standard output, and the message of a failure to the standard error. It exits with code 0 when it installed `lip`, and with code 1 when it failed. It does nothing before it has been read to its end.

### Environment

The script reads these environment variables:

- `LIP_VERSION`: The version to install, with or without a leading `v`.
- `LIP_INSTALL_DIR`: The installation directory.
- `LIP_GITHUB_PROXY`: As `github_proxy` with a URI as value ([config.toml(5), "User configuration"](config.toml.md#user-configuration)), for the HTTPS requests to `github.com` of the script. Unset or empty, they are sent directly.
- `LIP_NO_MODIFY_PATH`: With the value `1`, the script changes no file outside the installation directory and the temporary directory.
- `HOME`, `SHELL`, `ZDOTDIR`, `PATH`: As the sections below say.

### Terms

- **release**, **asset**: As [lip-self-update(1), "Terms"](lip-self-update.md#terms) defines, except that an asset is downloaded through `LIP_GITHUB_PROXY` ("Environment") instead of `github_proxy`, and that an HTTPS request goes to GitHub directly when it fails through `LIP_GITHUB_PROXY`.
- **platform**: By the output of `uname -s` and `uname -m`:

  | `uname -s` | `uname -m` | Platform |
  |---|---|---|
  | `Linux` | `x86_64` or `amd64` | `linux-x64` |
  | `Darwin` | `arm64` | `osx-arm64` |
  | `Darwin` | `x86_64`, and `sysctl -n sysctl.proc_translated` writes `1` (the shell runs under Rosetta 2) | `osx-arm64` |
  | `Darwin` | `x86_64`, otherwise | `osx-x64` |

  On any other system, including Linux on arm64 and Windows, the script fails, and its message says that `lip` is not released for it; on Windows (`uname -s` starting with `MINGW`, `MSYS` or `CYGWIN`), the message names the Windows Installer package of the latest release instead.
- **version**: The value of `LIP_VERSION` without a leading `v`, when it is set and not empty. Otherwise the version of the latest release: the last path segment, without its leading `v`, of the URL that an HTTPS HEAD request of `https://github.com/LiteLDev/lip/releases/latest` ends at, following redirects, when that URL ends with `/LiteLDev/lip/releases/tag/v<version>`; when it does not, the script fails. The script also fails when the version is not one or more ASCII letters, digits, `.`, `+` and `-`.
- **archive**: The asset `lip-<version>-<platform>.tar.xz` on Linux, and `lip-<version>-<platform>.zip` on macOS: the portable archive of [lip-self-update(1)](lip-self-update.md).
- **installation directory**: The value of `LIP_INSTALL_DIR` when it is set and not empty, and `$HOME/.local/bin` otherwise, without trailing `/`. The script fails when it is not an absolute path.

### Installing

The script, in order:

1. Fails unless `curl` is found, `HOME` is set and not empty, and `sha256sum` or `shasum` is found; on Linux, fails unless `xz` is found, with a message that says how to install it.
2. Finds the platform and the version.
3. Downloads the archive and the asset `SHA256SUMS` of the release to a new temporary directory, which it deletes when it exits.
4. Fails unless `SHA256SUMS` has a line that is the lowercase hexadecimal SHA-256 digest of the archive, two spaces (or a space and `*`), and the name of the archive.
5. Extracts the archive, and fails unless it has the file `lip` at its root.
6. Creates the installation directory with its parent directories. For each regular file of the archive, at the path P relative to its root, writes it to `P.tmp` in the installation directory, creating its parent directories, makes it executable, and renames it to P, replacing any file of that name. Files of the installation directory that are not in the archive are kept.
7. Runs `<installation directory>/lip -v`, and fails when it does not exit with code 0. Otherwise it writes the path of `lip` and the output of `lip -v`.
8. Sets up `PATH` ("PATH").
9. Writes that `mkdir servers && cd servers && lip` sets up a server; on the platform `osx-arm64`, also that `lip` offers to install Rosetta 2 when it needs it.

A failure in steps 1 to 5 changes no file outside the temporary directory.

The script writes no `install.json`, so the installation kind of the `lip` it installs is `"portable"` ([lip-self-update(1), "Terms"](lip-self-update.md#terms)).

### PATH

The **shell** is the last path segment of the environment variable `SHELL`. The **startup files** of the shell are:

| Shell | Startup files |
|---|---|
| `zsh` | `.zshenv`, `.zprofile`, `.zshrc` and `.zlogin` in `$ZDOTDIR`, or in `$HOME` when `ZDOTDIR` is unset or empty |
| `bash` | `~/.bash_profile`, `~/.bash_login`, `~/.profile` and `~/.bashrc` |
| `fish` | `~/.config/fish/config.fish` |
| any other | `~/.profile` |

A startup file **adds** the installation directory when a line of it that does not start with `#` (after blanks) contains a **form** of the installation directory not followed by an ASCII letter or digit, `.`, `_` or `-`. The forms are its absolute path and, when it is in `$HOME` at the path R relative to `$HOME`, `$HOME/R`, `${HOME}/R` and `~/R`. So the line `PATH="$HOME/.local/bin:$PATH"` of `~/.profile` on Debian and Ubuntu adds `~/.local/bin`.

The **rc file** and the **PATH line**, where D is the installation directory, written `$HOME/<path>` when it is in `$HOME`:

| Shell | rc file | PATH line |
|---|---|---|
| `zsh` | `.zshrc` in `$ZDOTDIR`, or in `$HOME` | `export PATH="D:$PATH"` |
| `bash`, on macOS | The first of `~/.bash_profile`, `~/.bash_login` and `~/.profile` that exists, or `~/.bash_profile` when none does | `export PATH="D:$PATH"` |
| `bash`, on Linux | `~/.bashrc` | `export PATH="D:$PATH"` |
| `fish` | `~/.config/fish/config.fish` | `fish_add_path "D"` |
| any other | `~/.profile` | `export PATH="D:$PATH"` |

The script sets up `PATH` by the first case that applies:

1. The installation directory is an entry of `PATH`: it does nothing.
2. A startup file adds the installation directory: it changes no file, and writes that `lip` is on `PATH` in new shells, and the path to run it by until then.
3. `LIP_NO_MODIFY_PATH` is not `1`, `/dev/tty` can be opened, and the user, asked through `/dev/tty`, answers `y`, `yes` or nothing (in any case): unless the rc file has a line that is the PATH line, it appends to the rc file, creating it and its parent directories, when the file is not empty a line break when it does not end with one and an empty line, then the line `# Added by the lip installer`, and the PATH line. It then writes that `lip` is on `PATH` in new shells, and the path to run it by until then.
4. Otherwise it changes no file, and writes the PATH line and the rc file to add it to, and the path to run `lip` by.

When the installation directory was an entry of `PATH`, and the first `lip` found on `PATH` is not in it, the script then writes the path of that `lip`.

## EXIT STATUS

0 when `lip` was installed, 1 otherwise.

## SEE ALSO

[lip(1)](lip.md), [lip-self-update(1)](lip-self-update.md), [config.toml(5)](config.toml.md)
