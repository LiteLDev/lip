# 常见问题

新用户最常问的问题，每个都有简短的回答，并附上详细说明所在的页面。

## 关于 lip

### lip 是做什么的？

lip 在一个文件夹里安装 Minecraft 基岩版服务器，安装和更新它的模组，并运行它。你可以通过终端里的界面使用它，也可以通过命令使用它。见[首页](index.md)。

### 它只能用于 LeviLamina 吗？

不是，不过它是为此而做的。lip 安装的模组是 LeviLamina 模组，LeviLamina 也是 lip 唯一会安装的加载器。你也可以运行不带加载器的原版服务器：Windows 版用 `lip setup --game <version> --no-loader`，Linux 版用 `--build linux`。见[游戏版本与构建](reference/commands/setup.md#游戏版本与构建)。

### lip 能安装客户端模组吗？

不能。lip 只管理服务器。游戏客户端的模组由 LeviLauncher 这样的启动器管理，它们读取的是同样的包。

### 它能在哪些系统上运行？

Windows、x64 的 Linux，以及 macOS。在 Linux 和 macOS 上，lip 通过它自己下载的 Wine 运行 Windows 服务器。没有 arm64 Linux 版的 lip。见 [Linux 与 macOS](guides/linux_macos.md#什么系统能运行什么)。

### 需要 git、Wine 或别的东西吗？

不需要。lip 自己与 git 仓库通信，并在需要时下载游戏、加载器、Wine 和 Microsoft Visual C++ Redistributable。见[内部机制](concepts/internals.md#git-客户端)。

### 有繁体中文吗？

有。界面有英语、简体中文和繁体中文，并跟随系统语言。想自己选择，可以用设置页面，或者 `lip config set language zh-Hant`。这份文档也有这三种语言。命令在终端里输出的内容始终是英语。见 [language](guides/configuration.md#language)。

## 使用 lip

### 可以不用界面吗？

可以。界面做的每一件事都是一条命令：`lip setup`、`lip install`、`lip update`、`lip uninstall`、`lip run`、`lip config`。从[用命令行开服](https://levimc.org/tutorials/zh-Hans/server/command-line/)开始。这些命令也能在没人回答问题的脚本里使用：见 [`lip setup`](reference/commands/setup.md#选项) 的 `--accept-eula`。

### 反过来呢：一定要学命令吗？

不用。只输入 `lip`、后面什么都不加，然后使用界面即可。见[开你的第一个服务器](https://levimc.org/tutorials/zh-Hans/server/first-server/)。

### 关掉终端后服务器会停止吗？

取决于你是怎么启动它的。

- **在界面里启动的：** 不会。退出界面或关掉终端后，它继续运行。
- **用 `lip run --managed` 启动的：** 不会。关掉终端只是让你脱离它。
- **用普通的 `lip run` 启动的：** 会。lip 会请服务器停止，并等它退出。

前两种情况下，服务器运行在 lip 的后台进程里。用界面或 `lip run --managed` 就能回到它那里。见[托管服务器](reference/commands/run.md#托管服务器)。

### 怎么停止在后台运行的服务器？

打开界面，在服务器页面选择“停止”。或者在服务器文件夹里运行 `lip run --managed` 接入它，再按 Ctrl+C。

### 机器重启后服务器会自动启动吗？

不会。lip 不会在系统启动时启动任何东西。需要这样做时，请让系统的服务管理器在服务器文件夹里运行 `lip run`。

### lip 能重启崩溃的服务器吗？

能。在服务器页面打开“自动重启”，或者运行 `lip config set restart on-failure --workspace`。见[重启](reference/commands/run.md#重启)。

### 服务器运行时能安装或更新模组吗？

在界面里不能：它会请你先停止服务器。用命令可以，服务器下次启动时才会用上更改。在 Windows 上，这样的命令可能因为运行中的服务器打开着某个文件而失败；这时 lip 会撤销它做过的更改。见[界面与命令](concepts/how_lip_works.md#界面与命令)。

### 能运行多个服务器吗？

能，每个服务器一个单独的文件夹，并在各自的 `server.properties` 里设置不同的端口。一个文件夹同一时间只运行一个服务器。界面在[工作区](interface/workspaces.md)页面保存着你的服务器文件夹列表。

### 两个人能管理同一个服务器吗？

以这台机器上**同一个用户**的身份登录的两个人共享一切：两人在界面里看到的是同一个运行中的服务器，谁都可以停止它或更改模组。lip 保证两次更改不会同时进行：后来的那次会立刻失败，稍后重试即可。

这台机器上的两个**不同用户**不共享 lip 的后台进程。只要文件权限允许，两人都能更改文件夹里的包。但其中一人启动的服务器，对另一人来说是“在此界面之外运行”：看得到，却不能停止，也不能向它输入。

lip 自己没有网络访问功能。要管理另一台机器上的服务器，请登录那台机器（例如通过 SSH），在那里运行 lip。

### 怎么更新 lip？

运行 `lip self-update`，或者使用设置页面的“检查更新”。`lip self-update --check` 只告诉你有没有新版本。见 [`lip self-update`](reference/commands/self_update.md)。

### 怎么更改服务器的 Minecraft 版本？

在服务器文件夹里再运行一次 `lip setup --game <version>`，或者使用界面的工作区页面。模组会换成能配合新游戏的版本。见 [`lip setup`](reference/commands/setup.md)。

## 文件

### 我的世界和设置在哪里？

在服务器文件夹里，就在游戏放它们的地方。lip 不会移动它们。

| 内容 | 在服务器文件夹里的位置 |
| --- | --- |
| 世界 | `worlds/` |
| 游戏的设置 | `server.properties`、`allowlist.json`、`permissions.json` |
| 模组，通常还有它的设置 | `mods/<名字>/` |

界面在[游戏配置](interface/game_config.md)和[模组配置](interface/mod_config.md)页面编辑这些设置。

### `.lip` 文件夹是什么？

lip 对这个服务器文件夹的记录：安装了哪些包、工作区设置、失败报告，以及 Linux 和 macOS 上的 Wine 前缀。在 Windows 上它是隐藏的。别去动它；移动服务器时，让它跟着文件夹一起走。见[文件和目录](reference/files.md)。

### lip 在服务器文件夹之外还存了什么？

四个属于每个用户的目录：你的设置、下载缓存、lip 的 Wine，以及界面的工作区列表。

| 内容 | Windows | Linux 和 macOS |
| --- | --- | --- |
| 设置 | `%APPDATA%\lip` | `~/.config/lip` |
| 缓存 | `%LOCALAPPDATA%\lip\Cache` | `~/.cache/lip` |
| 数据：lip 为运行服务器而安装的东西，例如它的 Wine | `%LOCALAPPDATA%\lip\Data` | `~/.local/share/lip` |
| 状态：工作区列表 | `%LOCALAPPDATA%\lip\State` | `~/.local/state/lip` |

缓存随时可以用 `lip cache clean` 删除。lip 不往系统的临时目录里写任何东西。见[用户目录](concepts/how_lip_works.md#用户目录)。

### 更改游戏版本会删掉我的世界吗？

不会。`worlds/`、`server.properties`、`allowlist.json` 和 `permissions.json` 都会保留。游戏自身的一些文件夹，例如根目录下的 `resource_packs/` 和 `behavior_packs/`，会被删除后重新写入。见[更换游戏后保留什么](reference/files.md#更换游戏后保留什么)。

### 怎么把服务器搬到另一个文件夹或另一台机器？

停止服务器，把整个文件夹（包括 `.lip`）移动或复制过去。然后在新位置运行 lip。在新机器上，第一次启动时 lip 会重新安装服务器需要的系统组件。

## 包

### 包从哪里来？

来自公开的 git 仓库，多数在 GitHub 上。没有中央仓库。包用它的仓库地址来指定，例如 `github.com/LiteLDev/LegacyScriptEngine`。界面的获取模组页面列出的是 [levimc.org](https://levimc.org) 上的模组。见[指定一个包](guides/managing_packages.md#指定一个包)。

### 怎么安装磁盘上的包？

把根目录下有 `lip.toml` 的 ZIP 文件或文件夹的路径交给 `lip install`，例如 `lip install ./my-mod.zip`。见[磁盘上的 ZIP 文件或文件夹](guides/managing_packages.md#磁盘上的-zip-文件或文件夹)。

### lip 支持私有仓库吗？

不支持。lip 下载时不登录，所以只能用公开仓库。

### 为什么我仓库里的某个标签不算版本？

只有名为 `v` 加完整版本号的标签（例如 `v1.2.3`）才是版本。`v1.2` 或 `1.2.3` 这样的标签不是。不过任何标签、分支或提交都可以安装：`lip install github.com/user/repo@tag:1.2.3`。见 [git 仓库](guides/managing_packages.md#git-仓库)。

### 还能安装为 lip v0.x 制作的、带 `tooth.json` 的包吗？

能。lip 能读取它们，并像其他包一样安装。lip v0.x 的服务器文件夹和配置，会在 lip 第一次处理它们时自动转换。包的作者可以用 `lip pkg migrate` 转换自己的包：见[迁移包](authors/migrating_packages.md)。

### `lip.toml` 是什么？

描述一个包的文件：它的名字、版本、依赖什么，以及安装哪些文件。只有制作包时才需要它。见 [lip.toml](concepts/lip_toml.md)。

### `lip install` 会修改我的 `lip.toml` 吗？

不会。`lip install` 把包安装到服务器文件夹里，并记录在 `.lip` 中。它从不写 `lip.toml`。

## 出问题的时候

### 命令失败了，去哪里找原因？

先看 lip 输出的 `error:` 和 `hint:` 两行。更多细节在服务器文件夹的 `.lip/logs/` 里的失败报告中；lip 会输出它的路径。见[读懂失败信息](guides/troubleshooting.md#读懂失败信息)。

### 安装到一半失败了会怎样？

不会留下做了一半的东西。会更改服务器文件夹的命令，要么做完全部工作，要么什么都不做：失败时，或者你按下 Ctrl+C 时，lip 会撤销它做过的更改。如果 lip 自己被杀掉，那个文件夹里的下一条命令会先把事情理顺。见[操作](concepts/how_lip_works.md#操作)。

### 这里 GitHub 很慢或被屏蔽，怎么办？

设置 GitHub 代理，或者使用你的网络代理。见 [GitHub 很慢或被屏蔽时](guides/configuration.md#github-很慢或被屏蔽时)。

### 能同时运行两条 lip 命令吗？

能，只要是在不同的服务器文件夹里。在同一个文件夹里，同一时间只能有一条命令更改它：第二条会立刻失败，等第一条结束后再运行。只读的命令（例如 `lip list`）随时可用。见[另一个 lip 正在更改工作区](guides/troubleshooting.md#另一个-lip-正在更改工作区)。

### 后台一直在运行的那个 lip 进程是什么？

守护进程。它执行你的命令，并让你的托管服务器持续运行。没有工作 10 分钟后它会自己退出，除非它正在运行服务器。`lip daemon status` 可以查看它，`lip daemon stop` 可以在它空闲时结束它。见[守护进程和客户端](concepts/how_lip_works.md#守护进程和客户端)。
