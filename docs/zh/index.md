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
      link: /zh/intro/first_server
    - theme: alt
      text: 安装 lip
      link: /zh/intro/installation
    - theme: alt
      text: 参考
      link: /zh/reference/cli

features:
  - title: 三步开服
    details: 选好文件夹、游戏版本和加载器，lip 会下载基岩版专用服务器和 LeviLamina，并替你启动服务器。
    link: /zh/intro/first_server
  - title: 来自 levimc.org 的模组
    details: 浏览模组，看哪些版本与你的服务器相配，连同它们的依赖一起安装、更新或移除。
    link: /zh/interface/get_mods
  - title: 一个界面，不用背命令
    details: 输入 lip，用方向键和 Enter 操作。每一次改动，lip 都会先给你看计划，再去执行。
    link: /zh/interface/tour
  - title: 需要时也有命令
    details: 界面能做的事都有对应的命令，适合写脚本，也适合没有屏幕的服务器。支持 Windows、Linux 和 macOS。
    link: /zh/guides/server_from_commands
---

## 它长什么样

在终端里输入 `lip`，就来到了你干活的地方。下图是服务器运行时的「服务器」页面：上面是服务器的输出，下面是输入服务器命令的一行。

![服务器运行时 lip 的「服务器」页面：服务器的输出里有“Server started.”，下方的输入行等待输入命令。](/screenshots/zh-Hans/first-server-21-running.svg)

## 从哪里开始

- 第一次接触 `lip`？先读 [lip 是什么？](intro/what_is_lip.md)，再[安装它](intro/installation.md)。
- 想马上开服？跟着[第一个服务器](intro/first_server.md)做：从一个空文件夹到装好一个模组并运行起来的服务器，每一步都有图。
- 更喜欢命令？请看[用命令开服](guides/server_from_commands.md)。
