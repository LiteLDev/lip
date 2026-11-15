# 安装

读完这一页，`lip` 已经装在你的电脑上，你确认过它能运行，也知道怎样更新和卸载它。

`lip` 只有一个可执行文件，不需要事先安装 .NET 运行时、Git 或别的任何东西。每个[发行版](https://github.com/LiteLDev/lip/releases/latest)都提供 Windows 安装程序，以及各个系统的便携压缩包。

## Windows：安装程序 <Badge platform="windows" />

1. 打开[最新发行版](https://github.com/LiteLDev/lip/releases/latest)，下载 `lip-<version>-win-x64.msi`。电脑用的是 ARM 处理器的话，下载 `lip-<version>-win-arm64.msi`。
2. 运行这个文件。Windows 会问你是否允许它进行更改：安装程序把 `lip` 放进 `C:\Program Files\lip`，并把这个文件夹加入系统 `PATH`。
3. 打开一个**新的**终端窗口（Windows 终端或 PowerShell）。安装前就开着的终端还不知道新的 `PATH`。
4. [确认它能运行](#确认它能运行)。

想不弹出任何窗口地安装（例如通过组策略、Intune 或 Ansible）：

```shell
msiexec /i lip-<version>-win-x64.msi /qn
```

::: details 从旧的安装程序升级
运行更新版本的安装程序会原地升级 `lip`，也会替换由 lip 0.35.0 及更早版本的 `setup.exe` 所做的安装。

lip 0.36.0 至 0.40.0 的发行版还提供过 `setup.exe`。安装程序同样会升级它装下的 `lip`，但之后**安装的应用**里仍会把那个 `setup.exe` 列为第二个 `lip`。可以把那一项卸载掉：它已经不会再移除 `lip`。
:::

## 任何系统：便携压缩包

每个发行版为每种系统提供一个压缩包，里面只有 `lip` 这一个文件（Windows 上是 `lip.exe`）。

| 系统 | 压缩包 |
| --- | --- |
| x64 的 Linux | `lip-<version>-linux-x64.tar.xz` |
| Apple 芯片的 macOS | `lip-<version>-osx-arm64.zip` |
| Intel 的 macOS | `lip-<version>-osx-x64.zip` |
| x64 的 Windows | `lip-<version>-win-x64.zip` |
| ARM64 的 Windows | `lip-<version>-win-arm64.zip` |

1. 从[最新发行版](https://github.com/LiteLDev/lip/releases/latest)下载你的系统对应的压缩包。
2. 把它解压到你选的文件夹里。
3. 把这个文件夹加入 `PATH`，或者直接解压到已经在 `PATH` 里的文件夹。
4. [确认它能运行](#确认它能运行)。

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

在 Linux 和 macOS 上，`~/.local/bin` 必须在 `PATH` 里。如果 `echo $PATH` 的输出里没有它，就把 `export PATH="$HOME/.local/bin:$PATH"` 这一行加进 `~/.profile`（Linux）或 `~/.zshrc`（macOS），再打开一个新的终端。在 Windows 上，到**设置 > 系统 > 系统信息 > 高级系统设置 > 环境变量**里，把解压到的文件夹加入 `PATH`。

压缩包保留了文件的可执行权限，不需要再 `chmod`。

::: tip macOS 拒绝运行 lip
`lip` 的发行版没有经过 Apple 公证，所以 macOS 可能拒绝运行下载来的 `lip`，并提示无法验证它。遇到这种情况，去掉解压出来的文件上的隔离标记：

```shell
xattr -d com.apple.quarantine ~/.local/bin/lip
```
:::

ARM64 的 Linux 没有压缩包，因为 LeviLamina 服务器无法在那里运行。在 Linux 和 macOS 上，服务器对系统的要求由 `lip` 在第一次用到时自己装好，见 [Linux 与 macOS](../guides/linux_macos.md)。

## 确认它能运行

让 `lip` 报出它的版本：

```shell
lip -v
```

它会回答一个版本号，例如 `0.41.0`。如果终端说不认识 `lip`，说明 `lip` 所在的文件夹不在这个终端的 `PATH` 里：打开一个新的终端，再检查 `PATH`。

然后只输入 `lip` 并按 `Enter`，就打开了界面。[第一个服务器](first_server.md)从这里接着讲。要离开界面，按 `Ctrl+C` 并确认。

## 更新

不管当初用哪种方式安装，`lip` 都能更新自己：

```shell
lip self-update
```

- 用安装程序安装的：`lip` 下载新版安装程序并运行它，Windows 会问你是否允许。
- 用便携压缩包安装的：`lip` 原地替换它自己的文件。

两种情况下，它都会先用发行版的校验和核对下载到的文件。`lip self-update --check` 不做任何更改：有新版本时输出新版本号，`lip` 已是最新时什么都不输出。

在界面里，同样的功能在「设置」页面：先选**检查更新**，再选**安装更新**。见[设置](../interface/settings.md)和 [`lip self-update`](../reference/commands/self_update.md)。

自己运行新版安装程序，或把新版压缩包解压出来覆盖旧文件，也同样可以。

## 卸载

1. 先停止你的服务器，再停止 `lip` 的后台进程：

   ```shell
   lip daemon stop
   ```

2. 移除 `lip`：
   - 用安装程序安装的：在**设置 > 应用 > 安装的应用**里卸载 **lip**。
   - 用便携压缩包安装的：删除 `lip`（`lip.exe`）这个文件；如果你把它的文件夹加进过 `PATH`，也一并去掉。

你的服务器只是普通的文件夹，卸载 `lip` 不会动它们。`lip` 自己还留有几个文件夹，可以删掉：

| 内容 | Windows | Linux 和 macOS |
| --- | --- | --- |
| 你的设置 | `%APPDATA%\lip` | `~/.config/lip` |
| 下载过的文件（缓存） | `%LOCALAPPDATA%\lip\Cache` | `~/.cache/lip` |
| Wine，以及 `lip` 为服务器装的其他东西 | `%LOCALAPPDATA%\lip\Data` | `~/.local/share/lip` |
| 你的工作区列表 | `%LOCALAPPDATA%\lip\State` | `~/.local/state/lip` |

在 Linux 和 macOS 上，这些是默认位置，`XDG_*` 环境变量可以改变它们。[文件](../reference/files.md)列出了 `lip` 读写的所有东西。
