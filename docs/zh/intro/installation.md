# 安装

`lip` 是一个通用包安装器。开始使用前，你需要先在系统中安装 `lip` 可执行文件。

## Windows 安装包 <Badge platform="windows" />

每个 [Release](https://github.com/LiteLDev/lip/releases/latest) 都为 x64（`win-x64`）和 ARM64（`win-arm64`）提供两种 Windows 安装包。两者都会把 `lip` 安装到 `C:\Program Files\lip`，并把该目录加入系统 `PATH`。安装后请打开新的终端再使用 `lip`。

- `lip-<version>-<platform>-setup.exe` 还会安装 `lip` 所需的 .NET 运行时，缺失时自动下载。直接运行即可，也可以静默安装：

  ```shell
  lip-<version>-win-x64-setup.exe /quiet
  ```

- `lip-<version>-<platform>.msi` 只安装 `lip`，适合组策略、Intune、Ansible 等部署工具。请先自行安装 [.NET 10 运行时](https://dotnet.microsoft.com/download/dotnet/10.0)。静默安装：

  ```shell
  msiexec /i lip-<version>-win-x64.msi /qn
  ```

运行更新版本的安装包会原地升级 `lip`，也会替换由 lip 0.35.0 及更早版本的 `setup.exe` 所做的安装。可在 **设置 > 应用 > 安装的应用** 中卸载 `lip`。

## 便携压缩包

每个 [Release](https://github.com/LiteLDev/lip/releases/latest) 还为各平台提供便携压缩包：Linux 为 `lip-<version>-<platform>.tar.xz`，macOS 与 Windows 为 `lip-<version>-<platform>.zip`，其中 `<platform>` 为 `linux-x64`、`linux-arm64`、`osx-x64`、`osx-arm64`、`win-x64` 或 `win-arm64`。请先安装 [.NET 10 运行时](https://dotnet.microsoft.com/download/dotnet/10.0)，再将压缩包解压到任意目录，并把该目录加入 `PATH`。
