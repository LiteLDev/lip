# 运行服务器

本指南用 lip 搭建一个 LeviLamina 服务器，并用 [`lip run`](../cli/commands/run.md) 运行它：在 Windows 上，以及在 Linux 和 macOS 上通过 [Wine](https://www.winehq.org/)。除了 lip 本身，无需下载或安装任何东西：服务器需要的系统组件由 lip 自行安装。

## 在 Windows 上

为服务器（即工作区）新建一个空目录，并在其中运行 lip：

```shell
mkdir my-server
cd my-server
lip install github.com/LiteLDev/LeviLamina
lip run
```

`lip install github.com/LiteLDev/LeviLamina` 把 LeviLamina 及其依赖的包（包括 Bedrock Dedicated Server）安装到工作区，并准备好 `bedrock_server_mod.exe`。`lip run` 随后在工作区中运行 `bedrock_server_mod.exe`。

服务器需要 Microsoft Visual C++ 可再发行组件包（x64）。若尚未安装，lip 会在运行第一条钩子命令或服务器之前从 Microsoft 下载其安装程序并运行；Windows 会请你允许它以管理员身份进行更改。参见[系统依赖](internal_mechanism.md#系统依赖)。

## 在 Linux 和 macOS 上通过 Wine 运行

无论 lip 运行在什么系统上，它默认都安装平台 `win-x64` 的包（参见[环境](../concepts/lip_toml.md#环境)）。在 Linux 和 macOS 上，它通过 Wine 运行这些包的钩子命令和服务器，因此同一个 Windows 版服务器可以在那里运行。

### 安装与运行

命令与 Windows 上相同：

```shell
mkdir my-server
cd my-server
lip install github.com/LiteLDev/LeviLamina
lip run
```

lip 第一次需要 Wine 时，会下载一个 Wine 构建，用写在 lip 中的 SHA-256 校验它，并解压到其数据目录（`~/.local/share/lip`，参见[常见问题](../faq.md#lip-会把安装的包放在哪里)），由所有工作区共用。随后它向工作区的 Wine 前缀中安装一次 Microsoft Visual C++ 可再发行组件包。两者都不需要 root，也不需要你自己的 `wine`：lip 从不运行 `PATH` 中的 `wine`。参见[系统依赖](internal_mechanism.md#系统依赖)。

在 Windows 以外的系统上，当工作区的平台以 `win` 开头时，lip 通过它的 Wine 运行所有程序：安装、更新或卸载包时的钩子命令（以 `wine cmd /s /c <命令>` 的形式），以及 `lip run` 运行的服务器（以 `wine` 加 `bedrock_server_mod.exe` 的绝对路径的形式）。Wine 把钩子命令中的每个 `"` 以 `\"` 的形式传给 `cmd`，而 `cmd` 不把它当作转义，因此含有 `"` 的钩子命令通过 Wine 运行时不会按原样运行；面向 Wine 的包应避免在钩子中使用 `"`。对 lip 而言，这样的工作区中的路径不区分大小写，与 Wine 运行的程序看到的一样：写入 `Plugins/a.dll` 的包会写入已有的 `plugins/` 目录，而不是在其旁边新建目录。

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
| x64 上的 Linux | lip 的 Wine。 |
| x64 上的 macOS | lip 的 Wine。 |
| arm64 上的 macOS | 同一个 x64 的 Wine，由 Rosetta 2 运行。 |

在 x64 的 Linux 上，lip 的 Wine 是 [Kron4ek/Wine-Builds](https://github.com/Kron4ek/Wine-Builds) 的构建；在 macOS 上，是 [Gcenx/macOS_Wine_builds](https://github.com/Gcenx/macOS_Wine_builds) 的构建。

在 Apple 芯片的 Mac 上，Wine 需要 Rosetta 2，而安装它需要管理员权限，因此 lip 不会安装它：缺少它时，lip 会失败并请你运行：

```shell
softwareupdate --install-rosetta --agree-to-license
```

lip 不为 arm64 的 Linux 发布，因为试过的办法都无法在那里可靠地运行 LeviLamina 服务器：

- [Hangover](https://github.com/AndreRH/hangover) 能运行原版 Bedrock Dedicated Server，但 LeviLamina 修改过的服务器一启动就失败。
- 由 [box64](https://github.com/ptitSeb/box64) 运行的 x64 Wine 会在 Windows 程序退出时将其杀死，退出码因此丢失。
- 由 [FEX](https://fex-emu.com/) 运行的 x64 Wine 在六次尝试中有两次成功运行了 LeviLamina 服务器。有两次，安装 LeviLamina 时用来修改服务器的 `PeEditor.exe` 被杀死；另有两次，服务器启动成功，但在停止时失败。

lip 自己下载服务器，不借助任何 Windows 程序，因此安装 `github.com/LiteLDev/bds` 不需要 Wine。

### Linux 版服务器

`github.com/LiteLDev/bds` 的变体 `linux` 安装 Minecraft 的 Linux 版服务器，而不是 Windows 版：

```shell
lip install github.com/LiteLDev/bds#linux
```

lip 放置 Linux 版服务器的文件，并自己把 `bedrock_server` 设为可执行，不借助钩子命令，因此没有任何程序通过 Wine 运行。一个工作区只能装服务器的一个变体，因此 `github.com/LiteLDev/bds#linux` 不能与 `github.com/LiteLDev/bds` 或 `github.com/LiteLDev/bds#windows` 同时安装。

`lip run` 仍只运行工作区根目录下的 `bedrock_server_mod.exe`，而 Linux 版服务器没有它，LeviLamina 也只加载到 Windows 版服务器中。请自行启动 Linux 版服务器，例如运行 `./bedrock_server`。

早期 lip 以 `--platform linux-x64` 创建的工作区，若只装有服务器，则仍可使用：lip 把其中的服务器读作 `github.com/LiteLDev/bds#linux`（参见[状态文件](../concepts/lockfiles.md#linux-x64-的工作区)）。

## 服务器运行期间

- **控制台**：服务器输出到你的终端，你输入的所有内容都会转交给它，因此你可以像平常一样输入服务器命令。
- **停止**：按 Ctrl+C。lip 向服务器输入 `stop`，服务器随即保存世界并退出，lip 随后退出。再次按 Ctrl+C 不会强行中断服务器：lip 仍会等待它退出。若服务器卡住，请结束其进程。
- **退出码**：`lip run` 以服务器的退出码退出（通过 Wine 运行时为 `wine` 的退出码）；在 Linux 或 macOS 上服务器被信号 n 结束时为 128 + n（例如 `SIGKILL` 为 137），因此在失败时重启服务器的脚本或服务管理器可以使用它。
- **修改包**：服务器运行期间，lip 不锁定工作区。你可以在另一个终端中对同一工作区运行 `lip install`、`lip update` 或 `lip uninstall`；服务器下次启动时才会看到这些改动。若某个要替换或删除的文件因被运行中的服务器打开而无法替换或删除（在 Windows 上可能发生），该操作会失败并被撤销，工作区保持原样；请停止服务器后再运行该命令。
- **被中断的操作**：与任何操作一样，`lip run` 会先完成或撤销被中断的操作；若此时另一个 lip 命令正在修改该工作区，它会立即失败。
