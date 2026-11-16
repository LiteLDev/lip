---
layout: home

hero:
  name: "lip"
  text: "运行 LeviLamina 服务器，管理它的模组"
  tagline: 在终端的一个界面里，搭建 Minecraft 基岩版服务器、安装模组并把它运行起来。
  image:
    src: /logo.webp
    alt: lip
  actions:
    - theme: brand
      text: 第一个服务器
      link: https://levimc.org/tutorials/zh-Hans/server/first-server/
    - theme: alt
      text: 指南
      link: /zh-Hans/interface/tour
    - theme: alt
      text: 参考
      link: /zh-Hans/reference/cli

features:
  - title: 三步开服
    details: 选好文件夹、游戏版本和加载器，lip 会下载基岩版专用服务器和 LeviLamina，并替你启动服务器。
    link: https://levimc.org/tutorials/zh-Hans/server/first-server/
  - title: 来自 levimc.org 的模组
    details: 浏览模组，看哪些版本与你的服务器相配，连同它们的依赖一起安装、更新或移除。
    link: /zh-Hans/interface/get_mods
  - title: 一个界面，不用背命令
    details: 输入 lip，用方向键和 Enter 操作。每一次改动，lip 都会先给你看计划，再去执行。
    link: /zh-Hans/interface/tour
  - title: 需要时也有命令
    details: 界面能做的事都有对应的命令，适合写脚本，也适合没有屏幕的服务器。支持 Windows、Linux 和 macOS。
    link: /zh-Hans/reference/cli
---

## lip 是什么

`lip` 用来运行 [LeviLamina](https://levimc.org/LeviLamina/zh-Hans/) 服务器并管理它的包。LeviLamina 是 LeviMC 为 Minecraft 基岩版专用服务器（Bedrock Dedicated Server）做的模组加载器；基岩版专用服务器则是 Mojang 为 Minecraft 基岩版发布的服务器程序。

用 `lip` 可以：

- **搭建服务器。** `lip` 下载彼此相配的游戏版本和 LeviLamina 版本，放进一个文件夹。
- **安装和移除模组。** `lip` 安装模组时会连同它依赖的东西一起装好，也能更新它、再把它移除。[LeviMC 模组](https://levimc.org/mods/zh-Hans/)里的模组都在它的「获取模组」页面上，那里每个模组的页面也给出了安装它的 `lip install` 命令。不在列表里的模组也照样能装：从它的 Git 仓库、网址或文件安装。
- **运行服务器。** `lip` 启动服务器，显示它的输出，把你输入的命令交给它，还能在它崩溃后自动重启。
- **修改配置。** `lip` 能显示并修改 `server.properties`、白名单、权限，以及各个模组的配置文件。

`lip` 只管理服务器。游戏客户端的模组由 [LeviLauncher](https://levimc.org/LeviLauncher/zh-Hans/) 等启动器管理。

## 界面和命令

两种用法做的是同一件事，哪种顺手就用哪种。

| | 界面 | 命令 |
| --- | --- | --- |
| 怎么启动 | 输入 `lip`，按 `Enter`。 | 输入 `lip` 加一条命令，例如 `lip install`。 |
| 它是什么 | 终端里的全屏程序，左边的侧边栏有九个页面，用方向键和 `Enter` 操作。 | 一行命令做一件事，`lip` 做完就返回。 |
| 适合 | 入门、浏览模组、盯着服务器、修改配置。 | 脚本、自动化、通过 SSH 连接的服务器。 |
| 接着读 | [界面导览](interface/tour.md) | [命令行参考](reference/cli.md) |

下图是服务器运行时界面的「服务器」页面：上面是服务器的输出，下面是输入服务器命令的一行。

![服务器运行时 lip 的「服务器」页面：服务器的输出里有“Server started.”，下方的输入行等待输入命令。](/screenshots/zh-Hans/first-server-21-running.svg)

## 从这里开始

LeviMC 的教程一步一步带你从零开始，做成一件事：

- [开你的第一个服务器](https://levimc.org/tutorials/zh-Hans/server/first-server/)：安装 `lip`，在界面里从一个空文件夹做到装好一个模组、运行起来的服务器。
- [用命令行开服](https://levimc.org/tutorials/zh-Hans/server/command-line/)：用 `lip setup`、`lip install` 和 `lip run` 做同样的事。
- [用 lip 发布你的模组](https://levimc.org/tutorials/zh-Hans/develop/publish-a-package/)：把你的模组做成包、发布它，并让它出现在 LeviMC 模组里。

## 要认识的五个词

- **工作区**是放一个服务器的文件夹。`lip` 一次只在一个工作区里工作：你启动它时所在的文件夹，或者你在界面里选的那一个。`lip` 为工作区记下的东西都放在其中的 `.lip` 文件夹里。
- **游戏**是某个版本（例如 1.26.60）的 Minecraft 基岩版专用服务器。一个工作区只有一个游戏。
- **加载器**就是 LeviLamina，有了它服务器才能运行模组。没有加载器的服务器是一个纯净的 Minecraft 服务器。
- **模组**为服务器增加功能。在 `lip` 看来，模组是一个**包**：可以从 Git 仓库、网址或你磁盘上的文件安装，有版本，也有它依赖的其他包。游戏和加载器同样是包。
- **守护进程**是在后台替你干活的 `lip` 进程。你在界面和命令里要求的事都由它执行；关掉界面之后，也是它让服务器继续运行。需要时 `lip` 会自己启动它，无事可做时它会自己退出。

[lip 的工作方式](concepts/how_lip_works.md)对它们逐一做了深入说明。

## lip 能在哪里运行

`lip` 为下列系统提供发行版。带 LeviLamina 的服务器是一个 Windows 程序；在 Linux 和 macOS 上，`lip` 通过 [Wine](https://www.winehq.org/) 运行它，而 Wine 由 `lip` 自己下载并配置好。

| 系统 | 带 LeviLamina 和模组的服务器 | 不带加载器的服务器 |
| --- | --- | --- |
| x64 或 ARM64 的 Windows | 可以 | 可以 |
| x64 的 Linux | 可以，通过 Wine | 可以：通过 Wine 运行 Windows 版服务器，或直接运行 Linux 版服务器 |
| Intel 或 Apple 芯片的 macOS | 可以，通过 Wine | 可以，通过 Wine 运行 Windows 版服务器 |

ARM64 的 Linux 没有发行版，因为 LeviLamina 服务器无法在那里运行。`lip` 在这些系统上会安装什么、有哪些限制，见 [Linux 与 macOS](guides/linux_macos.md)。

## 本站的其他内容

- **指南**：逐页说明[界面的用法](interface/tour.md)；用命令[管理包](guides/managing_packages.md)、[配置](guides/configuration.md)，以及 [Linux 与 macOS](guides/linux_macos.md)；[迁移](authors/migrating_packages.md) lip v0.x 的包；[故障排查](guides/troubleshooting.md)和[常见问题](faq.md)。
- **概念**：[lip 的工作方式](concepts/how_lip_works.md)、[lip.toml](concepts/lip_toml.md)、[状态文件](concepts/state_file.md)和[内部机制](concepts/internals.md)。
- **参考**：[命令行](reference/cli.md)、每一条[命令](reference/commands/setup.md)、[界面](reference/interface.md)、[配置](reference/configuration.md)和[文件](reference/files.md)。
