# 快速开始

本指南将帮助你快速上手 `lip`，涵盖项目中管理包的基础用法。

## 前置条件

开始前请确保你已[安装 lip](installation.md)。lip 不需要安装 `git`。

## 设置服务器

在服务端的根目录（即工作区，例如一个新的空目录）中运行 lip。首先设置服务器：某个游戏版本的 Bedrock Dedicated Server，以 LeviLamina 为加载器：

```shell
lip setup --game 1.21.0
```

这会安装支持游戏 1.21.0 的最新 LeviLamina。如果想改用 LeviLamina 支持的最新游戏，请安装 LeviLamina：

```shell
lip install LeviLamina
```

在下载游戏之前，lip 会给出 Minecraft 最终用户许可协议和 Microsoft 隐私声明的链接，并询问你是否接受：输入 `yes`。在脚本中 lip 无法询问，请加上 `--accept-eula`。参见 [`lip setup`](../cli/commands/setup.md)。

## 安装包

使用 `install` 命令并跟上包 ID 即可安装包（例如 mod），也可用 `@` 指定版本，例如 `github.com/LiteLDev/LegacyScriptEngine#lua@1.0.0`。

```shell
lip install github.com/LiteLDev/LegacyScriptEngine#lua
```

mod 需要服务器：在还没有游戏的工作区中，`lip install` 安装 mod 会失败，并请你先运行 `lip setup`。

`lip` 支持从多种来源安装（包括 Git 仓库、URL，以及磁盘上的 ZIP 文件和目录）。安装时会：
- 下载到本地缓存；
- 解析并安装其依赖；
- 记录到[状态文件](../concepts/lockfiles.md)（工作区中的 `.lip/state.json`）中。你的 `lip.toml` 不会被修改。

lip 只管理服务端工作区。客户端 mod 由 LeviLauncher 等启动器管理。

在任何系统上，lip 都安装平台 `win-x64` 的包。在 Linux 和 macOS 上，它通过 Wine 运行这些包的钩子命令和服务器。不带 LeviLamina 的 Linux 版服务器用 `lip setup --game <version> --build linux` 设置，`lip run` 在 x64 的 Linux 上启动它。参见[运行服务器](../guides/running_server.md#linux-版服务器)。

## 运行服务器

设置好服务器之后，在工作区中启动它：

```shell
lip run
```

像平常一样输入服务器命令即可。按 Ctrl+C 停止服务器：lip 向它发送 `stop`，并在它退出后退出。服务器运行期间，你可以在另一个终端中安装和更新包；这些改动在服务器下次启动时生效。

## 查看已安装包

查看当前项目中已安装的所有包：

```shell
lip list
```

会显示包列表及其已安装版本。

## 查看包信息

你可以在不安装的情况下，以 JSON 形式获取包的包文件及其可用版本：

```shell
lip pkg view github.com/LiteLDev/LeviLamina
```

## 更新包

使用 `update` 命令将包更新到新版本。

```shell
lip update github.com/LiteLDev/LegacyScriptEngine#lua
```

如需切换到某个特定版本（包括更旧的版本），请用 `@` 指定，例如 `lip update github.com/LiteLDev/LegacyScriptEngine#lua@1.0.0`。这会把该包固定在该版本，直到你用不带 `@` 的路径更新它。不带参数运行 `lip update` 会更新所有你显式安装的包，游戏和 LeviLamina 除外：如需改变它们的版本，请再次运行 `lip setup`，例如 `lip setup --game 1.21.50`。

## 卸载包

从项目中移除包及其安装的文件：

```shell
lip uninstall github.com/LiteLDev/LegacyScriptEngine#lua
```

## 配置

`lip` 支持配置全局设置，例如代理。

**查看当前配置：**

```shell
lip config list
```

**设置配置项：**

```shell
lip config set github_proxy https://mirror.ghproxy.com/
```

## 创建包

如果你要创建 mod 或其他包，请先初始化其 `lip.toml`。在项目根目录运行：

```shell
lip pkg init
```

该命令会询问你的包的包 ID（例如 `github.com/user/MyMod`），并在当前目录创建 `lip.toml` 文件（若已存在则失败）。这个文件向 lip 描述你的包。如果只是安装包，则不需要它。参见[创建包](../guides/creating_packages.md)。

## 下一步

- 查看 [CLI 概览](../cli/overview.md) 了解更多高级命令用法。
- 查看 [lip.toml](../concepts/lip_toml.md) 了解结构细节。
