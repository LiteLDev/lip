# lip 是什么？

读完这一页，你会知道 `lip` 能做什么、它的两种用法，以及后面的文档反复用到的五个词。

## lip 能做什么

`lip` 用来运行 [LeviLamina](https://github.com/LiteLDev/LeviLamina) 服务器并管理它的包。LeviLamina 是 Minecraft 基岩版专用服务器（Bedrock Dedicated Server）的模组加载器；基岩版专用服务器则是 Mojang 为 Minecraft 基岩版发布的服务器程序。

用 `lip` 可以：

- **搭建服务器。** `lip` 下载彼此相配的游戏版本和 LeviLamina 版本，放进一个文件夹。
- **安装和移除模组。** `lip` 安装模组时会连同它依赖的东西一起装好，也能更新它、再把它移除。
- **运行服务器。** `lip` 启动服务器，显示它的输出，把你输入的命令交给它，还能在它崩溃后自动重启。
- **修改配置。** `lip` 能显示并修改 `server.properties`、白名单、权限，以及各个模组的配置文件。

`lip` 只管理服务器。游戏客户端的模组由 LeviLauncher 等启动器管理。

## 两种用法

两种用法做的是同一件事，哪种顺手就用哪种。

| | 界面 | 命令 |
| --- | --- | --- |
| 怎么启动 | 输入 `lip`，按 `Enter`。 | 输入 `lip` 加一条命令，例如 `lip install`。 |
| 它是什么 | 终端里的全屏程序，左边的侧边栏有九个页面，用方向键和 `Enter` 操作。 | 一行命令做一件事，`lip` 做完就返回。 |
| 适合 | 入门、浏览模组、盯着服务器、修改配置。 | 脚本、自动化、通过 SSH 连接的服务器。 |
| 接着读 | [第一个服务器](first_server.md)，然后是[界面导览](../interface/tour.md) | [用命令开服](../guides/server_from_commands.md)，然后是[命令行参考](../reference/cli.md) |

![lip 在空文件夹里的「主页」：左边是有九个页面的侧边栏，右边列出三个步骤：创建服务器、安装模组、启动服务器。](/screenshots/zh-Hans/home-01-empty.svg)

## 要认识的五个词

- **工作区**是放一个服务器的文件夹。`lip` 一次只在一个工作区里工作：你启动它时所在的文件夹，或者你在界面里选的那一个。`lip` 为工作区记下的东西都放在其中的 `.lip` 文件夹里。
- **游戏**是某个版本（例如 1.26.60）的 Minecraft 基岩版专用服务器。一个工作区只有一个游戏。
- **加载器**就是 LeviLamina，有了它服务器才能运行模组。没有加载器的服务器是一个纯净的 Minecraft 服务器。
- **模组**为服务器增加功能。在 `lip` 看来，模组是一个**包**：可以从 Git 仓库、网址或你磁盘上的文件安装，有版本，也有它依赖的其他包。游戏和加载器同样是包。
- **守护进程**是在后台替你干活的 `lip` 进程。你在界面和命令里要求的事都由它执行；关掉界面之后，也是它让服务器继续运行。需要时 `lip` 会自己启动它，无事可做时它会自己退出。

[lip 的工作方式](../concepts/how_lip_works.md)对它们逐一做了深入说明。

## lip 能在哪里运行

`lip` 为下列系统提供发行版。带 LeviLamina 的服务器是一个 Windows 程序；在 Linux 和 macOS 上，`lip` 通过 [Wine](https://www.winehq.org/) 运行它，而 Wine 由 `lip` 自己下载并配置好。

| 系统 | 带 LeviLamina 和模组的服务器 | 不带加载器的服务器 |
| --- | --- | --- |
| x64 或 ARM64 的 Windows | 可以 | 可以 |
| x64 的 Linux | 可以，通过 Wine | 可以：通过 Wine 运行 Windows 版服务器，或直接运行 Linux 版服务器 |
| Intel 或 Apple 芯片的 macOS | 可以，通过 Wine | 可以，通过 Wine 运行 Windows 版服务器 |

ARM64 的 Linux 没有发行版，因为 LeviLamina 服务器无法在那里运行。`lip` 在这些系统上会安装什么、有哪些限制，见 [Linux 与 macOS](../guides/linux_macos.md)。

## 下一步

先[安装 lip](installation.md)，再搭建你的[第一个服务器](first_server.md)。
