# Installation

After this page `lip` is installed on your computer, you have checked that it runs, and you know how to update and remove it.

`lip` is one executable file. It needs no .NET runtime, no Git and nothing else installed first. Every [release](https://github.com/LiteLDev/lip/releases/latest) has an installer for Windows and a portable archive for each system.

## Windows: the installer <Badge platform="windows" />

1. Open the [latest release](https://github.com/LiteLDev/lip/releases/latest) and download `lip-<version>-win-x64.msi`. On a computer with an ARM processor, take `lip-<version>-win-arm64.msi`.
2. Run the file. The installer asks for the folder to install `lip` in, `C:\Program Files\lip` unless you change it, and installs once you click **Install** and allow Windows to make changes. It adds that folder to the system `PATH`.
3. Open a **new** terminal window (Windows Terminal or PowerShell). A terminal that was open before does not know the new `PATH` yet.
4. [Check that it runs](#check-that-it-runs).

To install without any window, to `C:\Program Files\lip`, for example with Group Policy, Intune or Ansible:

```shell
msiexec /i lip-<version>-win-x64.msi /qn
```

::: details Upgrading from an older installer
Running a newer installer upgrades `lip` in place, in the folder you chose before. It also replaces an installation made by the `setup.exe` of lip 0.35.0 or earlier.

The releases of lip 0.36.0 to 0.40.0 also had a `setup.exe`. The installer upgrades what that installed as well, but **Installed apps** then still lists that `setup.exe` as a second `lip`. You can uninstall that entry: it no longer removes `lip`.
:::

## Any system: the portable archive

Each release has one archive per system. It holds the single file `lip` (`lip.exe` on Windows).

| System | Archive |
| --- | --- |
| Linux on x64 | `lip-<version>-linux-x64.tar.xz` |
| macOS on Apple silicon | `lip-<version>-osx-arm64.zip` |
| macOS on Intel | `lip-<version>-osx-x64.zip` |
| Windows on x64 | `lip-<version>-win-x64.zip` |
| Windows on ARM64 | `lip-<version>-win-arm64.zip` |

1. Download the archive for your system from the [latest release](https://github.com/LiteLDev/lip/releases/latest).
2. Extract it into a folder of your choice.
3. Add that folder to your `PATH`, or extract into a folder that is on it already.
4. [Check that it runs](#check-that-it-runs).

::: code-group

```shell [Linux]
mkdir -p ~/.local/bin
tar -xJf lip-<version>-linux-x64.tar.xz -C ~/.local/bin
```

```shell [macOS]
mkdir -p ~/.local/bin
unzip lip-<version>-osx-arm64.zip -d ~/.local/bin
```

```powershell [Windows]
Expand-Archive lip-<version>-win-x64.zip -DestinationPath "$env:LOCALAPPDATA\Programs\lip"
```

:::

On Linux and macOS, `~/.local/bin` must be on your `PATH`. If `echo $PATH` does not show it, add the line `export PATH="$HOME/.local/bin:$PATH"` to `~/.profile` (Linux) or `~/.zshrc` (macOS), and open a new terminal. On Windows, add the folder you extracted into to `PATH` under **Settings > System > About > Advanced system settings > Environment Variables**.

The archives keep the file executable, so there is nothing to `chmod`.

::: tip macOS refuses to run lip
The releases of `lip` are not notarized by Apple, so macOS may refuse to run a `lip` that came from a download, saying that it cannot check it. If that happens, remove the quarantine mark from the file you extracted:

```shell
xattr -d com.apple.quarantine ~/.local/bin/lip
```
:::

There is no archive for Linux on ARM64, where a LeviLamina server does not run. On Linux and macOS, `lip` installs what the server needs of the system by itself the first time it needs it; see [Linux and macOS](../guides/linux_macos.md).

## Check that it runs

Ask `lip` for its version:

```shell
lip -v
```

It answers with a version number, such as `0.41.0`. If the terminal says that it does not know `lip`, the folder of `lip` is not on the `PATH` of this terminal: open a new terminal, and check the `PATH`.

Then type `lip` alone and press `Enter` to open the interface. [Your first server](first_server.md) goes on from there. To leave the interface, press `Ctrl+C` and confirm.

## Updating

`lip` updates itself, whichever way you installed it:

```shell
lip self-update
```

- Installed with the installer: `lip` downloads the newer installer and runs it. Windows asks you to allow it.
- From a portable archive: `lip` replaces its own file in place.

Either way it checks what it downloaded against the checksums of the release first. `lip self-update --check` changes nothing: it prints the version of a newer release, or nothing when `lip` is up to date.

In the interface, the same is on the **Settings** page: **Check for updates**, then **Install update**. See [Settings](../interface/settings.md) and [`lip self-update`](../reference/commands/self_update.md).

Running a newer installer yourself, or extracting a newer archive over the old file, works too.

## Uninstalling

1. Stop your servers, then stop the background process of `lip`:

   ```shell
   lip daemon stop
   ```

2. Remove `lip`:
   - Installed with the installer: uninstall **lip** under **Settings > Apps > Installed apps**.
   - From a portable archive: delete the file `lip` (`lip.exe`), and remove its folder from `PATH` if you added it.

Your servers are ordinary folders, and uninstalling `lip` does not touch them. `lip` also keeps a few folders of its own, which you can delete:

| What | Windows | Linux and macOS |
| --- | --- | --- |
| Your settings | `%APPDATA%\lip` | `~/.config/lip` |
| Downloaded files (the cache) | `%LOCALAPPDATA%\lip\Cache` | `~/.cache/lip` |
| Wine and other things `lip` installed for the server | `%LOCALAPPDATA%\lip\Data` | `~/.local/share/lip` |
| The list of your workspaces | `%LOCALAPPDATA%\lip\State` | `~/.local/state/lip` |

On Linux and macOS these are the defaults; the `XDG_*` variables move them. [Files](../reference/files.md) lists everything that `lip` reads and writes.
