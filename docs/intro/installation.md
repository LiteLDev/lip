# Installation

`lip` runs a LeviLamina server and manages its packages. To get started, you need to install the `lip` executable on your system. `lip` is a single self-contained executable: it needs no .NET runtime or other prerequisite.

## Windows installer <Badge platform="windows" />

Each [release](https://github.com/LiteLDev/lip/releases/latest) provides two installers for Windows, for x64 (`win-x64`) and ARM64 (`win-arm64`). Both install `lip` to `C:\Program Files\lip` and add that directory to the system `PATH`. Open a new terminal afterwards to use `lip`.

- `lip-<version>-<platform>-setup.exe`: run it, or install silently:

  ```shell
  lip-<version>-win-x64-setup.exe /quiet
  ```

- `lip-<version>-<platform>.msi` suits deployment tools such as Group Policy, Intune or Ansible. To install silently:

  ```shell
  msiexec /i lip-<version>-win-x64.msi /qn
  ```

Running a newer installer upgrades `lip` in place, and replaces an installation made by the `setup.exe` of lip 0.35.0 or earlier. Uninstall `lip` from **Settings > Apps > Installed apps**.

## Portable archive

Each [release](https://github.com/LiteLDev/lip/releases/latest) also provides a portable archive for every platform: `lip-<version>-<platform>.tar.xz` for Linux and `lip-<version>-<platform>.zip` for macOS and Windows, where `<platform>` is one of `linux-x64`, `osx-x64`, `osx-arm64`, `win-x64` and `win-arm64`. Extract the archive to a directory of your choice and add that directory to `PATH`. There is no archive for Linux on ARM64, where a LeviLamina server does not run (see [Running a Server](../guides/running_server.md#macos-and-arm64)).

## Upgrading

Run [`lip self-update`](../cli/commands/self_update.md) to upgrade `lip` to its latest release, whichever way you installed it: it runs the newer installer of the kind that installed `lip`, or replaces the files of a portable archive in place. Running a newer installer yourself, or extracting a newer archive over the old one, works too.
