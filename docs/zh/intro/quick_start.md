# 快速开始

本指南将帮助你快速上手 `lip`，涵盖项目中管理包的基础用法。

## 前置条件

开始前请确保你已[安装 lip](installation.md)。lip 不需要安装 `git`。

## 初始化项目

要开始追踪包，请先初始化项目清单。在项目根目录运行：

```shell
lip init
```

该命令会在当前目录创建 `tooth.json` 文件（若已存在则覆盖）。这个文件是清单（manifest），用于保存项目及其依赖的元数据。

## 安装包

使用 `install` 命令并跟上包标识即可安装包，也可用 `@` 指定版本。

```shell
lip install github.com/LiteLDev/LeviLamina@1.0.0
```

`lip` 支持从多种来源安装（包括 Git 仓库）。安装时会：
- 下载到本地缓存；
- 解析并安装其依赖；
- 记录到锁文件（`tooth_lock.json`）中。你的 `tooth.json` 不会被修改。

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

另外，使用 `install` 并指定版本也可完成升级或降级。

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
