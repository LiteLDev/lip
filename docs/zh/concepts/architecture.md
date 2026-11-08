# 架构

`lip` 用于管理工作区中已安装包的状态。

## 工作区状态

表示工作区当前状态，追踪安装了哪些包以及安装方式。
- **Packages**：已安装包集合，记录在[工作区文件](lockfiles.md)中；lip 将其保存在工作区中的 `.lip/workspace.yaml`。
- **环境**：工作区的平台（`server` 或 `client`）和 .NET RID。参见[环境](lip_yaml.md#环境)。

## 配置

用于管理 `lip` 工具本身的全局配置（如代理和默认行为）。
- **Properties**：配置项键值对（例如 `github_proxy`）。

## 包来源

包是根部带有 `lip.yaml` 的归档。参见 [lip.yaml](lip_yaml.md)。
- **git 仓库**：像 `github.com/user/repo` 这样的包 ID 即 git 仓库 `https://github.com/user/repo.git`，lip 使用内置的 git smart-HTTP（协议第 2 版）客户端读取；无需安装 `git` 命令行工具。不存在中心化注册表：版本即仓库的 `v<semver>` 标签，git 引用（`@branch:`、`@tag:`、`@rev:`）则改为选取一个提交。
- **URL**：通过 HTTPS 下载的 ZIP 归档。
- **本地文件和目录**：通过路径传给 `lip install`、根部带有 `lip.yaml` 的 ZIP 文件和目录。
- **缓存**：HTTP(S) 下载内容按其 URL 的 SHA-256 存储（`http-<hash>`）。git 归档不会被缓存。

详见[内部机制](../guides/internal_mechanism.md#包解析)。
