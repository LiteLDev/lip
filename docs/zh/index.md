---
layout: home

hero:
  name: "lip"
  text: "通用包安装器"
  tagline: 从公开 Git 仓库安装包
  actions:
    - theme: brand
      text: 快速开始
      link: /zh/intro/quick_start
    - theme: alt
      text: 核心概念
      link: /zh/concepts/architecture
    - theme: alt
      text: 查看命令
      link: /zh/cli/commands/install

features:
  - title: 基于 Git 的包
    details: 使用 Go module 路径，直接从 Git 仓库安装包。
  - title: 依赖解析
    details: 自动依赖求解并按拓扑顺序安装。
  - title: 面向集成
    details: 提供 JSON-RPC 守护进程（lipd），可无缝集成到 IDE 与工具。
  - title: 跨平台
    details: 支持 Windows、Linux 和 macOS，并可根据平台选择变体。
---
