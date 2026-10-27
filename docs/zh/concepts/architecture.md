# 架构

`lip` 用于管理工作区中已安装包的状态。

## 工作区状态

表示工作区当前状态，追踪安装了哪些包以及安装方式。
- **Packages**：已安装包集合，记录在[锁文件](lockfiles.md)中；lip 将锁文件保存在用户数据目录中。
- **环境**：工作区的平台（`server` 或 `client`）和 .NET RID。参见[环境](package_manifest.md#环境)。

## 配置

用于管理 `lip` 工具本身的全局配置（如代理和默认行为）。
- **Properties**：配置项键值对（例如 `github_proxy`）。

## 包来源

包是位于某个归档根部的 `manifest.json`（或旧格式的 `tooth.json`）。参见[包清单](package_manifest.md)。
- **git 仓库**：像 `github.com/user/repo` 这样的包路径即 git 仓库 `https://github.com/user/repo.git`，lip 使用内置的 git smart-HTTP（协议第 2 版）客户端读取；无需安装 `git` 命令行工具。不存在中心化注册表：版本即仓库的 `v<semver>` 标签。
- **ZIP 归档**：`.mcmod` 文件和其他 ZIP 文件，通过 HTTPS 下载，或位于另一个归档之内。
- **目录**：本地目录，包括归档内的目录。
- **缓存**：git 对象按对象 ID 存储（`git-<id>`），在不同版本和不同包之间共享；HTTP(S) 下载内容按其 URL 的 SHA-256 存储（`http-<hash>`）。

详见[内部机制](../guides/internal_mechanism.md#包解析)。
