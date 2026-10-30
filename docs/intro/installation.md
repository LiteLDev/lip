# Installation

`lip` is a general package installer. To get started, you need to install the `lip` executable on your system.

## npm

The default installation method for all platforms is npm:

```shell
npm install -g @levimc/lip
```

This installs both `lip` and `lipd` and works on Windows, Linux, and macOS.

If you only want to run `lip` without installing it globally, you can also use `npx`:

```shell
npx @levimc/lip --help
```

This is useful for trying `lip` or running one-off commands.

::: tip
This package was previously published as `@futrime/lip`. If you installed it under the old name, uninstall it first with `npm uninstall -g @futrime/lip`.
:::

## winget <Badge platform="windows" />

```shell
winget install levimc.lip
```

::: tip
This package was previously published as `futrime.lip`. If you installed it under the old ID, uninstall it first with `winget uninstall futrime.lip`.
:::

## Windows installer <Badge platform="windows" />

Each [release](https://github.com/LiteLDev/lip/releases/latest) provides two installers for Windows, for x64 (`win-x64`) and ARM64 (`win-arm64`). Both install `lip` and `lipd` to `C:\Program Files\lip` and add that directory to the system `PATH`. Open a new terminal afterwards to use `lip`.

- `lip-<version>-<runtime>-setup.exe` also installs the .NET runtime that `lip` needs, downloading it when it is missing. Run it, or install silently:

  ```shell
  lip-<version>-win-x64-setup.exe /quiet
  ```

- `lip-<version>-<runtime>.msi` installs only `lip` and suits deployment tools such as Group Policy, Intune or Ansible. Install the [.NET 10 runtime](https://dotnet.microsoft.com/download/dotnet/10.0) yourself first. To install silently:

  ```shell
  msiexec /i lip-<version>-win-x64.msi /qn
  ```

Running a newer installer upgrades `lip` in place, and replaces an installation made by the `setup.exe` of lip 0.35.0 or earlier. Uninstall `lip` from **Settings > Apps > Installed apps**.
