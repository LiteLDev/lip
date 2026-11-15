# Installation

`lip` runs a LeviLamina server and manages its packages. To get started, you need to install the `lip` executable on your system. `lip` is a single self-contained executable: it needs no .NET runtime or other prerequisite.

## Windows installer <Badge platform="windows" />

Each [release](https://github.com/LiteLDev/lip/releases/latest) provides a Windows installer, `lip-<version>-<platform>.msi`, for x64 (`win-x64`) and ARM64 (`win-arm64`). It installs `lip` to `C:\Program Files\lip` and adds that directory to the system `PATH`. Open a new terminal afterwards to use `lip`. Run it, or install silently, for example with deployment tools such as Group Policy, Intune or Ansible:

```shell
msiexec /i lip-<version>-win-x64.msi /qn
```

Running a newer installer upgrades `lip` in place, and replaces an installation made by the `setup.exe` of lip 0.35.0 or earlier. Uninstall `lip` from **Settings > Apps > Installed apps**.

The releases of lip 0.36.0 to 0.40.0 also had a `setup.exe`. The installer upgrades what it installed as well, but **Installed apps** then still lists that `setup.exe` as a second `lip`, which you can uninstall: it no longer removes `lip`.

## Portable archive

Each [release](https://github.com/LiteLDev/lip/releases/latest) also provides a portable archive for every platform: `lip-<version>-<platform>.tar.xz` for Linux and `lip-<version>-<platform>.zip` for macOS and Windows, where `<platform>` is one of `linux-x64`, `osx-x64`, `osx-arm64`, `win-x64` and `win-arm64`. Extract the archive to a directory of your choice and add that directory to `PATH`. There is no archive for Linux on ARM64, where a LeviLamina server does not run (see [Running a Server](../guides/running_server.md#macos-and-arm64)).

## Upgrading

Run [`lip self-update`](../cli/commands/self_update.md) to upgrade `lip` to its latest release, whichever way you installed it: it runs the newer installer of the kind that installed `lip`, or replaces the files of a portable archive in place. Running a newer installer yourself, or extracting a newer archive over the old one, works too.
