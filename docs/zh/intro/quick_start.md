# 快速开始

本指南将帮助你快速上手 `lip`，涵盖项目中管理包的基础用法。

## 前置条件

开始前请确保你已[安装 lip](installation.md)。lip 不需要安装 `git`。

## 初始化项目

如果你要创建 mod 或其他包，请先初始化其清单。在项目根目录运行：

```shell
lip init
```

该命令会在当前目录创建 `manifest.json` 文件（若已存在则失败）。这个文件既是你的 mod 的 LeviLamina 清单，也是它的 lip 包清单。如果只是安装包，则不需要它。

## 安装包

在服务端的根目录（即工作区）中运行 lip。使用 `install` 命令并跟上包路径即可安装包，也可用 `@` 指定版本。

```shell
lip install github.com/LiteLDev/LeviLamina@1.0.0
```

`lip` 支持从多种来源安装（包括 Git 仓库、`.mcmod` 文件和 URL）。安装时会：
- 下载到本地缓存；
- 解析并安装其依赖；
- 记录到[锁文件](../concepts/lockfiles.md)中，lip 将其保存在工作区之外。你的 `manifest.json` 不会被修改。

lip 只管理服务端工作区。客户端 mod 由 LeviLauncher 等启动器管理。

## 查看已安装包

查看当前项目中已安装的所有包：

```shell
lip list
```

会显示包列表及其已安装版本。

## 查看包信息

你可以在不安装的情况下获取包详情（如可用版本、元数据）。

**查看包元数据：**

```shell
lip view github.com/LiteLDev/LeviLamina
```

**查看可用版本：**

```shell
lip versions github.com/LiteLDev/LeviLamina
```

## 更新包

使用 `update` 命令将包更新到新版本。

```shell
lip update github.com/LiteLDev/LeviLamina
```

如需切换到某个特定版本（包括更旧的版本），请用 `@` 指定，例如 `lip update github.com/LiteLDev/LeviLamina@1.0.0`。不带参数运行 `lip update` 会更新所有你显式安装的包。

## 卸载包

从项目中移除包及其安装的文件：

```shell
lip uninstall github.com/LiteLDev/LeviLamina
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

## 下一步

- 查看 [CLI 概览](../cli/overview.md) 了解更多高级命令用法。
- 查看 [包清单](../concepts/package_manifest.md) 了解结构细节。
