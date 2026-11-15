# 安装

`lip` 用于运行 LeviLamina 服务器并管理其包。开始使用前，你需要先在系统中安装 `lip` 可执行文件。`lip` 是一个自包含的单一可执行文件：不需要 .NET 运行时或其他前置条件。

## Windows 安装包 <Badge platform="windows" />

每个 [Release](https://github.com/LiteLDev/lip/releases/latest) 都为 x64（`win-x64`）和 ARM64（`win-arm64`）提供两种 Windows 安装包。两者都会把 `lip` 安装到 `C:\Program Files\lip`，并把该目录加入系统 `PATH`。安装后请打开新的终端再使用 `lip`。

- `lip-<version>-<platform>-setup.exe`：直接运行即可，也可以静默安装：

  ```shell
  lip-<version>-win-x64-setup.exe /quiet
  ```

- `lip-<version>-<platform>.msi` 适合组策略、Intune、Ansible 等部署工具。静默安装：

  ```shell
  msiexec /i lip-<version>-win-x64.msi /qn
  ```

运行更新版本的安装包会原地升级 `lip`，也会替换由 lip 0.35.0 及更早版本的 `setup.exe` 所做的安装。可在 **设置 > 应用 > 安装的应用** 中卸载 `lip`。

## 便携压缩包

每个 [Release](https://github.com/LiteLDev/lip/releases/latest) 还为各平台提供便携压缩包：Linux 为 `lip-<version>-<platform>.tar.xz`，macOS 与 Windows 为 `lip-<version>-<platform>.zip`，其中 `<platform>` 为 `linux-x64`、`osx-x64`、`osx-arm64`、`win-x64` 或 `win-arm64`。将压缩包解压到任意目录，并把该目录加入 `PATH`。ARM64 的 Linux 没有压缩包，因为 LeviLamina 服务器无法在那里运行（见[运行服务器](../guides/running_server.md#macos-与-arm64)）。

## 升级

运行 [`lip self-update`](../cli/commands/self_update.md) 把 `lip` 升级到最新发布的版本，无论你用哪种方式安装：它会运行与安装 `lip` 时同类的新版安装包，或在原地替换便携压缩包的文件。自己运行新版安装包，或把新版压缩包解压覆盖旧版，也同样可以。
