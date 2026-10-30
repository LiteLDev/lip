# 安装

`lip` 是一个通用包安装器。开始使用前，你需要先在系统中安装 `lip` 可执行文件。

## npm

所有平台的默认安装方式是 npm：

```shell
npm install -g @levimc/lip
```

该方式会同时安装 `lip` 和 `lipd`，适用于 Windows、Linux 与 macOS。

如果你只想运行 `lip` 而不全局安装，也可以使用 `npx`：

```shell
npx @levimc/lip --help
```

这适合试用 `lip` 或执行一次性命令。

::: tip
该包此前以 `@futrime/lip` 名称发布。如果你使用旧名称安装过，请先执行 `npm uninstall -g @futrime/lip` 卸载。
:::

## winget <Badge platform="windows" />

```shell
winget install levimc.lip
```

::: tip
该包此前以 `futrime.lip` ID 发布。如果你使用旧 ID 安装过，请先执行 `winget uninstall futrime.lip` 卸载。
:::

## Windows 安装包 <Badge platform="windows" />

每个 [Release](https://github.com/LiteLDev/lip/releases/latest) 都为 x64（`win-x64`）和 ARM64（`win-arm64`）提供两种 Windows 安装包。两者都会把 `lip` 和 `lipd` 安装到 `C:\Program Files\lip`，并把该目录加入系统 `PATH`。安装后请打开新的终端再使用 `lip`。

- `lip-<version>-<runtime>-setup.exe` 还会安装 `lip` 所需的 .NET 运行时，缺失时自动下载。直接运行即可，也可以静默安装：

  ```shell
  lip-<version>-win-x64-setup.exe /quiet
  ```

- `lip-<version>-<runtime>.msi` 只安装 `lip`，适合组策略、Intune、Ansible 等部署工具。请先自行安装 [.NET 10 运行时](https://dotnet.microsoft.com/download/dotnet/10.0)。静默安装：

  ```shell
  msiexec /i lip-<version>-win-x64.msi /qn
  ```

运行更新版本的安装包会原地升级 `lip`，也会替换由 lip 0.35.0 及更早版本的 `setup.exe` 所做的安装。可在 **设置 > 应用 > 安装的应用** 中卸载 `lip`。
