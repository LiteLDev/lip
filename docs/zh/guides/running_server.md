# 运行服务器

本指南用 lip 搭建一个 LeviLamina 服务器，并用 [`lip run`](../cli/commands/run.md) 运行它：在 Windows 上，以及在 Linux 上通过 [Wine](https://www.winehq.org/)。

## 在 Windows 上

为服务器（即工作区）新建一个空目录，并在其中运行 lip：

```shell
mkdir my-server
cd my-server
lip install github.com/LiteLDev/LeviLamina
lip run
```

`lip install github.com/LiteLDev/LeviLamina` 把 LeviLamina 及其依赖的包（包括 Bedrock Dedicated Server）安装到工作区，并准备好 `bedrock_server_mod.exe`。`lip run` 随后在工作区中运行 `bedrock_server_mod.exe`。

服务器可能需要系统的运行库，例如 Microsoft Visual C++ 可再发行组件包。lip 不会安装它们：若服务器提示缺少运行库，请自行安装。

## 在 Linux 上通过 Wine 运行

无论 lip 运行在什么系统上，它默认都安装平台 `win-x64` 的包（参见[环境](../concepts/lip_toml.md#环境)）。在 Linux 上，它通过 Wine 运行这些包的钩子命令和服务器，因此同一个 Windows 版服务器可以在 Linux 上运行。

### 前置条件

lip 不会安装 Wine，也不会向 Wine 前缀中安装任何东西。安装包之前：

1. 从发行版或 [WineHQ](https://wiki.winehq.org/Download) 安装 Wine，使 `wine` 命令位于 `PATH` 中：

   ```shell
   wine --version
   ```

2. 向工作区的 Wine 前缀中安装服务器需要的 Windows 运行库，例如 Microsoft Visual C++ 可再发行组件包，可以用 [winetricks](https://github.com/Winetricks/winetricks)：

   ```shell
   mkdir my-server
   cd my-server
   WINEPREFIX="$PWD/.lip/wine" winetricks -q vcrun2022
   ```

   `.lip/wine` 是 lip 默认使用的前缀；参见 [Wine 前缀](#wine-前缀)。

### 安装与运行

命令与 Windows 上相同：

```shell
lip install github.com/LiteLDev/LeviLamina
lip run
```

在 Windows 以外的系统上，当工作区的平台以 `win` 开头时，lip 通过 `PATH` 中的 `wine` 运行所有程序：安装、更新或卸载包时的钩子命令（以 `wine cmd /s /c <命令>` 的形式），以及 `lip run` 运行的服务器（以 `wine` 加 `bedrock_server_mod.exe` 的绝对路径的形式）。Wine 把钩子命令中的每个 `"` 以 `\"` 的形式传给 `cmd`，而 `cmd` 不把它当作转义，因此含有 `"` 的钩子命令通过 Wine 运行时不会按原样运行；面向 Wine 的包应避免在钩子中使用 `"`。对 lip 而言，这样的工作区中的路径不区分大小写，与 Wine 运行的程序看到的一样：写入 `Plugins/a.dll` 的包会写入已有的 `plugins/` 目录，而不是在其旁边新建目录。

### Wine 前缀

lip 为 `wine` 设置两个环境变量，除非你自己设置了它们：

| 变量 | 默认值 |
| --- | --- |
| `WINEPREFIX` | 工作区中 `.lip/wine` 的绝对路径，在 `WINEPREFIX` 未设置或为空时使用。每个工作区都有自己的前缀，由 Wine 在该工作区中首次运行时创建，并随工作区一起移动。 |
| `WINEDEBUG` | `-all`，在 `WINEDEBUG` 未设置时使用：不显示 Wine 的调试信息。如需查看，请自行设置，例如设为 `err+all`。 |

如需使用其他前缀，请为该工作区的每条 lip 命令设置 `WINEPREFIX`：

```shell
export WINEPREFIX="$HOME/.wine-bds"
lip install github.com/LiteLDev/LeviLamina
lip run
```

### macOS 与 arm64

服务器是 x64 的 Windows 程序。lip 在它发布的每个系统上都用真实的 LeviLamina 服务器测试过，配置如下：

| 系统 | 由什么运行服务器 |
| --- | --- |
| x64 上的 Windows | Windows。 |
| arm64 上的 Windows | Windows，由它模拟 x64。 |
| x64 上的 Linux | Wine。 |
| x64 上的 macOS | Wine。 |
| arm64 上的 macOS | x64 的 Wine，由 Rosetta 2 运行（`softwareupdate --install-rosetta`）。 |

在 x64 的 macOS 上，Wine 11.0 无法创建前缀，Wine 11.21 可以。macOS 的 Wine 构建见 [Gcenx/macOS_Wine_builds](https://github.com/Gcenx/macOS_Wine_builds)。

lip 不为 arm64 的 Linux 发布，因为试过的办法都无法在那里可靠地运行 LeviLamina 服务器：

- [Hangover](https://github.com/AndreRH/hangover) 能运行原版 Bedrock Dedicated Server，但 LeviLamina 修改过的服务器一启动就失败。
- 由 [box64](https://github.com/ptitSeb/box64) 运行的 x64 Wine 无法运行 `bdsdown`，也就是安装包时下载服务器的程序。
- 由 [FEX](https://fex-emu.com/) 运行的 x64 Wine 成功运行过两次 LeviLamina 服务器，但十次尝试中有九次 `bdsdown` 失败。

### Linux 原生包

如需改为安装其他平台的包，例如 `github.com/LiteLDev/bds` 的 Linux 原生服务器，请在新工作区的第一条命令中指定平台：

```shell
lip install github.com/LiteLDev/bds --platform linux-x64
```

此后工作区保持 `linux-x64`：lip 只安装 `linux-x64` 的条目，用 `sh -c` 运行钩子命令，并且不通过 Wine 运行程序。没有 `linux-x64` 条目的包（例如仅支持 Windows 的 mod）无法安装到其中。

`lip run` 仍只运行工作区根目录下的 `bedrock_server_mod.exe`，而没有任何 `linux-x64` 的包会安装它：LeviLamina 没有 `linux-x64` 的条目，`github.com/LiteLDev/bds` 的 Linux 原生服务器是 `bedrock_server`。请自行启动这样的服务器，例如运行 `./bedrock_server`。

## 服务器运行期间

- **控制台**：服务器输出到你的终端，你输入的所有内容都会转交给它，因此你可以像平常一样输入服务器命令。
- **停止**：按 Ctrl+C。lip 向服务器输入 `stop`，服务器随即保存世界并退出，lip 随后退出。再次按 Ctrl+C 不会强行中断服务器：lip 仍会等待它退出。若服务器卡住，请结束其进程。
- **退出码**：`lip run` 以服务器的退出码退出（通过 Wine 运行时为 `wine` 的退出码）；在 Linux 或 macOS 上服务器被信号 n 结束时为 128 + n（例如 `SIGKILL` 为 137），因此在失败时重启服务器的脚本或服务管理器可以使用它。
- **修改包**：服务器运行期间，lip 不锁定工作区。你可以在另一个终端中对同一工作区运行 `lip install`、`lip update` 或 `lip uninstall`；服务器下次启动时才会看到这些改动。若某个要替换或删除的文件因被运行中的服务器打开而无法替换或删除（在 Windows 上可能发生），该操作会失败并被撤销，工作区保持原样；请停止服务器后再运行该命令。
- **被中断的操作**：与任何操作一样，`lip run` 会先完成或撤销被中断的操作；若此时另一个 lip 命令正在修改该工作区，它会立即失败。
