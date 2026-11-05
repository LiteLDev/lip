# Installation

`lip` is a general package installer. To get started, you need to install the `lip` executable on your system.

## winget <Badge platform="windows" />

```shell
winget install levimc.lip
```

::: tip
This package was previously published as `futrime.lip`. If you installed it under the old ID, uninstall it first with `winget uninstall futrime.lip`.
:::

## Windows installer <Badge platform="windows" />

Each [release](https://github.com/LiteLDev/lip/releases/latest) provides two installers for Windows, for x64 (`win-x64`) and ARM64 (`win-arm64`). Both install `lip` to `C:\Program Files\lip` and add that directory to the system `PATH`. Open a new terminal afterwards to use `lip`.

- `lip-<version>-<runtime>-setup.exe` also installs the .NET runtime that `lip` needs, downloading it when it is missing. Run it, or install silently:

  ```shell
  lip-<version>-win-x64-setup.exe /quiet
  ```

- `lip-<version>-<runtime>.msi` installs only `lip` and suits deployment tools such as Group Policy, Intune or Ansible. Install the [.NET 10 runtime](https://dotnet.microsoft.com/download/dotnet/10.0) yourself first. To install silently:

  ```shell
  msiexec /i lip-<version>-win-x64.msi /qn
  ```

Running a newer installer upgrades `lip` in place, and replaces an installation made by the `setup.exe` of lip 0.35.0 or earlier. Uninstall `lip` from **Settings > Apps > Installed apps**.

## Portable archive

Each [release](https://github.com/LiteLDev/lip/releases/latest) also provides a portable archive for every platform: `lip-<version>-<runtime>.tar.xz` for Linux and `lip-<version>-<runtime>.zip` for macOS and Windows, where `<runtime>` is one of `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`, `win-x64` and `win-arm64`. Install the [.NET 10 runtime](https://dotnet.microsoft.com/download/dotnet/10.0) first, extract the archive to a directory of your choice and add that directory to `PATH`.
