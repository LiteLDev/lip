# 用命令管理服务器

读完本页，你就能只用 `lip` 命令安装一个 Minecraft 基岩版服务器、给它装上模组、启动它、让它在关掉终端后继续运行，以及停止它。这里的每一件事在界面里也都能做：见[第一个服务器](../intro/first_server.md)。

## 开始之前

- 先安装 lip：见[安装](../intro/installation.md)。除此之外什么都不用装。游戏、加载器以及它们需要的系统组件，都由 lip 下载。
- 给服务器选一个文件夹。lip 把它叫作**工作区**。每条命令都作用于你当前所在的文件夹，所以先 `cd` 进去。

::: code-group

```powershell [Windows]
mkdir D:\servers\survival
cd D:\servers\survival
```

```sh [Linux / macOS]
mkdir -p ~/servers/survival
cd ~/servers/survival
```

:::

## 安装服务器

运行 [`lip setup`](../reference/commands/setup.md)，并给出想要的游戏版本：

```sh
lip setup --game 1.26.50
```

把 `1.26.50` 换成你想要的版本。这条命令会往文件夹里安装三样东西：

- **游戏**：该版本的 Minecraft 基岩版专用服务器（Bedrock Dedicated Server）。
- **加载器**：LeviLamina，负责把模组加载进游戏。lip 会选用支持该游戏版本的最新 LeviLamina。
- **依赖**：LeviLamina 需要的包。

完成后，lip 会输出：

```text
✓ The server is set up. Run it with 'lip run', or install mods with 'lip install'.
```

### 选择构建和加载器

有三个选项可以改变安装的内容。完整列表见 [`lip setup`](../reference/commands/setup.md) 页面。

| 你想要 | 运行 |
| --- | --- |
| Windows 服务器，配上适合它的最新 LeviLamina（默认） | `lip setup --game 1.26.50` |
| 指定的 LeviLamina 版本 | `lip setup --game 1.26.50 --loader levilamina@<version>` |
| 不带加载器的 Windows 服务器，也就没有模组 | `lip setup --game 1.26.50 --no-loader` |
| Linux 服务器，它没有加载器 | `lip setup --game 1.26.50 --build linux` |

Windows 服务器在 Linux 和 macOS 上也能运行，靠的是 Wine。哪种系统能运行什么，见 [Linux 与 macOS](linux_macos.md)。

如果你不在意具体的游戏版本，可以让 LeviLamina 来决定。下面这条命令会安装最新的 LeviLamina，以及它支持的最新游戏：

```sh
lip install LeviLamina
```

在已经有服务器的文件夹里，这同样是一次服务器安装，可能会更改游戏。见[更改游戏版本](#更改游戏版本)。

### 接受 Minecraft EULA

下载游戏之前，lip 会列出游戏所受约束的两份文件，并请你接受：

```text
The Minecraft server is available under the Minecraft End User License Agreement (https://www.minecraft.net/eula) and the Microsoft Privacy Policy (https://go.microsoft.com/fwlink/?LinkId=521839). Read them before you go on.
Enter yes to accept the Minecraft EULA and the Privacy Policy:
```

输入 `yes`（或 `y`）并按回车。其他任何回答都会中止命令，什么都不会改变。

lip 不会记住你的回答。每次安装游戏时它都会再问一遍。

在脚本、系统服务或容器里，lip 没法提问，安装会失败。如果你接受这两份文件，请用 `--accept-eula` 说明：

```sh
lip setup --game 1.26.50 --accept-eula
```

### 先看看会做什么

加上 `--dry-run`（或 `-n`），可以只看计划、不做任何更改：

```sh
lip setup --game 1.26.50 --dry-run
```

lip 会列出它将安装或卸载的每个包，说明它会询问 EULA，最后输出 `Dry run: nothing was changed.`。见[试运行](managing_packages.md#试运行)。

## 安装模组

用 [`lip install`](../reference/commands/install.md) 加上模组的名字来安装：

```sh
lip install LegacyScriptEngine#lua
```

lip 会安装这个模组和它依赖的一切。模组会放在工作区的 `mods/<名字>/` 里。

模组需要服务器。在还没有游戏的文件夹里，`lip install` 一个模组会失败，并提示你先运行 `lip setup`。

[管理包](managing_packages.md)一页讲了指定包的各种写法，以及如何更新、列出和卸载。

## 运行服务器

用 [`lip run`](../reference/commands/run.md) 启动服务器：

```sh
lip run
```

lip 会启动工作区安装时确定的那个服务器文件：

| 工作区里有 | lip 运行 |
| --- | --- |
| 加载器 | `bedrock_server_mod.exe` |
| 没有加载器，且是 Windows 服务器 | `bedrock_server.exe` |
| 没有加载器，且是 Linux 服务器 | `bedrock_server`（仅限 x64 的 Linux） |

启动 Windows 服务器之前，如果系统缺少它需要的组件，lip 会先装上：Microsoft Visual C++ Redistributable，以及在 Linux 和 macOS 上 lip 自带的 Wine。在 Windows 上，系统可能会请你允许安装程序运行。见 [Linux 与 macOS](linux_macos.md)。

### 使用控制台

服务器的输出会显示在你的终端里。你输入的内容会传给服务器，所以可以像平常一样输入服务器命令：

```text
list
```

### 停止服务器

按 Ctrl+C。lip 会向服务器输入 `stop`，服务器保存世界后退出，然后 lip 退出。

再按一次 Ctrl+C 也不会强行切断服务器。lip 总是等服务器自己退出。如果服务器卡死了，请用系统自带的工具结束它的进程。

关闭终端窗口同样会请服务器停止。想让它脱离终端继续运行，请使用[托管服务器](#让服务器持续运行)。

### 退出码

`lip run` 以服务器的退出码退出。在 Linux 和 macOS 上，如果服务器被信号结束，退出码是 128 加上信号编号，例如 `SIGKILL` 对应 137。通过 Wine 运行时，它是 `wine` 的退出码。

只要服务器运行过，无论退出码是多少，lip 都不会再输出自己的错误，也不会写失败报告。脚本可以检查退出码：

::: code-group

```powershell [Windows]
lip run
if ($LASTEXITCODE -ne 0) { echo "The server failed with code $LASTEXITCODE" }
```

```sh [Linux / macOS]
lip run || echo "The server failed with code $?"
```

:::

如果 lip 根本没能启动服务器，它会说明原因，并以非零退出码退出。见[服务器无法启动](troubleshooting.md#服务器无法启动)。

## 让服务器持续运行

普通的 `lip run` 属于它所在的终端：关掉终端，服务器就停了。**托管服务器**则属于 lip 的后台进程，也就是[守护进程](../concepts/how_lip_works.md#守护进程和客户端)。无论你的终端发生什么，它都继续运行。

### 启动

```sh
lip run --managed
```

这会在守护进程里启动服务器，并把你的终端**接入**它：你能看到它的输出、输入它的命令，和普通的 `lip run` 一样。

想只启动而不接入，加上 `--detach`。服务器一启动，命令就返回：

```sh
lip run --managed --detach
```

### 回到服务器

在同一个文件夹里，从任意终端再运行一次同样的命令：

```sh
lip run --managed
```

这个工作区已经有托管服务器在运行，所以 lip 不会再启动什么，而是把你接入它。你会先看到服务器在你到来之前输出的一部分内容。可以同时有多个终端接入。

### 离开但不停止

关闭终端窗口或标签页。你的终端会脱离，服务器继续运行。

::: warning
Ctrl+C 不是脱离。接入期间按 Ctrl+C 会请服务器停止，和普通的 `lip run` 一样。
:::

### 停止

托管服务器只有在有人要求时才会停止：

- **从已接入的终端**：按 Ctrl+C，或输入 `stop`。在服务器退出之前，你的终端一直保持接入。
- **从界面**：打开该工作区的[服务器页面](../interface/server.md)，选择“停止”。

除此之外没有别的办法让它停下。有托管服务器在运行时，[`lip daemon stop`](../reference/commands/daemon.md) 会拒绝执行，守护进程也不会自行退出。

### 需要知道的几点

- **界面用的就是托管服务器。** 你在服务器页面启动的服务器，和 `lip run --managed` 接入的是同一个托管服务器。可以在一边启动，在另一边查看。
- **每个工作区一个。** 守护进程在一个文件夹里最多运行一个托管服务器。
- **它保留启动时的环境。** 服务器的工作目录和环境变量来自启动它的那个终端，而不是之后接入的终端。
- **它需要守护进程。** 如果 lip 用不了守护进程，`lip run --managed` 会失败，并提示你不带 `--managed` 运行服务器。见 [lip 自己执行了命令](troubleshooting.md#lip-自己执行了命令)。
- **机器重启后它不会回来。** 守护进程是你这个用户的一个普通进程。lip 不会在系统启动时启动服务器。需要这样做时，请让系统的服务管理器在工作区里运行普通的 `lip run`。

带 `--managed` 时，命令因 `--detach` 结束或因终端脱离而结束，退出码是 0。如果服务器在你接入期间停止，退出码就是服务器的退出码，和普通的 `lip run` 一样。

## 崩溃后自动重启

默认情况下，服务器退出后就保持停止。想让 lip 在崩溃后重新启动它，请为工作区打开 `restart`：

```sh
lip config set restart on-failure --workspace
```

此后，只要服务器以非 0 的退出码退出，lip 就会等 5 秒再启动它：

```text
⚠ The server exited with code 1, and restart is on-failure: lip restarts it in 5 seconds (restart 1, at most 5 times within 10 minutes).
```

- 以 0 退出的服务器，或者你要求停止的服务器，不会被重启。
- 在这 5 秒内按 Ctrl+C 会取消重启。
- 10 分钟内重启 5 次之后，lip 会放弃并告诉你。请查看服务器的输出，找出它反复失败的原因。
- lip 在服务器退出时才读取这个设置。服务器运行期间也可以修改它。

再关掉它：

```sh
lip config set restart never --workspace
```

这个设置对普通服务器和托管服务器都有效，也就是界面里的“自动重启”开关。见[配置](configuration.md#restart)。

## 一个工作区一个服务器

一个工作区同一时间只运行一个服务器。文件夹里的服务器在运行时，第二个 `lip run` 会立刻失败：

```text
error: A server of the workspace 'D:\servers\survival' is already running: another lip run, the interface of lip or the daemon of lip runs it, and holds the server lock 'D:\servers\survival\.lip\server.lock'. Stop that server first, or attach to it with 'lip run --managed' when it is a managed server.
```

要运行两个服务器，请给每个服务器单独的文件夹，并在各自的 `server.properties` 里设置不同的端口。

## 更改游戏版本

`lip update` 从不更改游戏或加载器。`lip install` 只有在你点名 LeviLamina 或游戏本身时才会更改，这时它就是一次服务器安装。游戏出了新版本，或者某个模组需要别的版本时，请带上想要的版本再运行一次 `lip setup`。先看一下计划：

```sh
lip setup --game 1.26.60 --dry-run
lip setup --game 1.26.60
```

做这件事之前请先停止服务器。

lip 会针对新的游戏和加载器重新安排模组：

- 模组当前的版本能配合它们工作，就保持不变。否则换成能配合的版本。
- 如果某个模组没有任何能配合的版本，lip 会列出这些模组，并询问是否卸载它们。回答 `yes` 继续，其他回答则中止，什么都不改。lip 没法提问时（例如在脚本里），命令会失败并列出它们；请先用 `lip uninstall` 自己卸载，再重新安装服务器。
- 如果 `lip setup` 给出的游戏和加载器与工作区现有的相同，就什么都不会改，也不会询问 EULA。

### 保留什么删除什么

更改游戏会删除旧游戏的文件，写入新游戏的文件。你自己的文件会这样处理：

| 文件 | 结果 |
| --- | --- |
| `server.properties`、`allowlist.json`、`permissions.json` | **保留。** 新游戏不会覆盖它们。 |
| `worlds/`，以及其他既不是 lip 安装的、也不在下面列表里的东西 | **保留。** lip 不碰不是它安装的文件。 |
| 模组的文件夹 `mods/<名字>/`，连同其中的配置 | **保留**，除非该模组被卸载或更换版本。 |
| `behavior_packs/`、`resource_packs/`、`definitions/`、`config/`、`world_templates/`、`development_behavior_packs/`、`development_resource_packs/`、`development_skin_packs/` | **连同里面的所有内容一起删除**，再由新游戏重新写入。 |
| `valid_known_packs.json`、`packet-statistics.txt`，以及 Windows 服务器的 `bedrock_server.exe.bak` | **删除。** |

::: warning
你自己复制到服务器根目录下 `behavior_packs/` 或 `resource_packs/` 里的包，会随这些文件夹一起被删除。请在别处留一份副本，或者把包放在世界文件夹里，也就是 `worlds/` 之下。
:::

卸载游戏时适用同样的规则。做大的改动之前，请先备份整个文件夹：安装失败时 lip 会撤销自己的更改，但成功的安装删掉的东西，它没法找回来。

## 服务器运行时更改包

服务器运行期间，这些命令不会拒绝工作。你可以在另一个终端、同一个文件夹里运行 `lip install`、`lip update`、`lip uninstall` 和 `lip setup`。服务器下次启动时才会用上这些更改。

在 Windows 上，运行中的服务器会一直打开它的一些文件。lip 需要替换或删除这样的文件时，命令会失败，lip 会撤销已做的更改，工作区恢复原样。请停止服务器后再运行命令。见[文件被占用](troubleshooting.md#文件被占用)。

`restart` 设为 `on-failure` 时还可能遇到一种情况。如果服务器恰好在某条命令更改文件夹的那一刻崩溃，重启时会发现文件夹正忙，`lip run` 会以[另一个 lip 正在更改工作区](troubleshooting.md#另一个-lip-正在更改工作区)里的错误结束。重新启动服务器即可。

界面更严格：工作区的服务器运行期间，界面不会更改那里的包。见[界面与命令](../concepts/how_lip_works.md#界面与命令)。

## 接下来

- [管理包](managing_packages.md)：安装、更新、列出和卸载。
- [配置](configuration.md)：lip 的设置，以及 GitHub 很慢或被屏蔽时怎么办。
- [故障排查](troubleshooting.md)：命令失败时怎么办。
- [`lip setup`](../reference/commands/setup.md) 和 [`lip run`](../reference/commands/run.md) 的参考页面。
