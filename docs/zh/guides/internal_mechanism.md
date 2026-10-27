# 内部机制

本指南说明 lip 的底层工作方式。

## 架构

lip 提供两个主要入口：

1. **CLI（`lip`）**：独立命令行工具，内置包管理逻辑，**不依赖**守护进程。
2. **Daemon（`lipd`）**：独立进程，供外部应用（IDE、GUI、脚本）以编程方式调用 lip。

CLI 与 Daemon 共享同一套核心逻辑，保证无论通过哪种接口，行为都一致。

Daemon（`lipd`）通过 **stdio 上的 JSON-RPC** 通信，使工具无需为每个命令反复拉起进程，也无需解析文本输出。

## 流程

`lip install`、`lip uninstall` 和 `lip update` 执行相同的步骤：

1. **锁**：加载 `lip_lock.json`，或迁移旧的 `tooth_lock.json`（参见[锁文件](../concepts/lockfiles.md)）。
2. **环境**：确定平台和 RID（参见[环境](../concepts/package_manifest.md#环境)）。
3. **解析器**：把每个 ref 转换为一个具体包候选序列，途中跟随路由文档。
4. **依赖图求解器**：为每个请求和每条安装边各选出一个候选，使每个身份只出现一次，且不违反任何 `conflicts` 条目。
5. **安装器**：按安装顺序的逆序卸载离开集合的包，再按依赖优先的顺序安装进入集合的包。`manifest.json` 包和 `tooth.json` 包各有各的安装器。
6. **锁**：每安装或卸载一个包，就保存一次锁。

解析或求解过程中出现任何失败时，工作区都不会被修改。

## 包解析

lip 不使用中心化的包注册表或模块代理。像 `github.com/user/repo` 这样的包路径直接对应 git 仓库 `https://github.com/user/repo.git`，lip 通过内置的 git smart-HTTP 协议（第 2 版）客户端与其通信。**无需安装 `git` 命令行工具**，lip 也从不调用它。

- **版本**：lip 通过 `ls-refs` 列出仓库标签（只请求 `refs/tags/v*`）。可用版本即名称恰为 `v<SemVer 2.0.0>` 的标签（例如 `v1.2.3`）；其他标签，包括 `v1.2` 或 `v01.2.3` 这类宽松写法，都会被忽略。附注标签会被解析到其指向的提交。
- **惰性候选**：包路径的候选从最新标签开始按需产出，因此 lip 只会打开求解器实际需要的标签。
- **文档**：读取某个版本的 `manifest.json`（或 `tooth.json`）时，lip 只获取该标签提交的树对象（使用 `filter blob:none` 的浅层部分获取，不传输任何文件内容），然后只下载这一个文件。因此无论仓库多大，解析依赖时每个版本只需几 KB 流量。
- **路由**：路由文档按 variant 和环境选出一个条目，并按顺序解析其 refs。超过 8 跳的链，或两次经过同一归档和 variant 的链，不产出候选。
- **安装**：只有安装器需要时才下载文件：mod 下载整棵树，文件包和 `tooth.json` 包只下载要放置的文件。
- **完整性**：每个下载的 git 对象都会按其 git 对象 ID（SHA-1）校验。ZIP 归档以其 SHA-256 标识。
- **缓存**：下载的 git 文件按对象 ID、ZIP 归档按 SHA-256 存入全局缓存，相同的文件只下载一次，可在不同版本和不同包之间复用。同一 URL 只下载一次。标签列表和树对象仅在单次命令执行期间缓存在内存中；每次运行都会重新查询标签，因此被移动的标签会立即生效。
- **重定向**：已重命名或转移的 GitHub 仓库会通过 GitHub 的 HTTP 重定向自动跟随。`github.com` 的所有者和仓库名不区分大小写。
- **代理**：若配置了 `github_proxy`，对 `github.com` 仓库的请求会先经由该代理发送；若代理请求失败，lip 会直接向 `github.com` 重试。从 `github.com` URL 下载发布资源时遵循同样的规则。

只有当某个版本的标签仍存在于仓库中时，该版本才能被安装。目前只支持可通过 HTTPS 访问的公开仓库。

标签处带有 `version` 的 `manifest.json` 必须声明该标签的版本。标签处的旧 `tooth.json` 若声明了其他版本，则按标签版本安装，lip 会给出警告。

## 求解

- **固定**：已安装的包按原样进入求解器，从锁中重建，无需访问网络。若这样无法满足请求，lip 会只固定显式安装的包重新求解，此时依赖可以改变版本。
- **优先级**：靠前的候选优先：包路径的最新版本，以及条目或依赖中的第一个 ref。
- **顺序**：先安装依赖；依赖成环的包按名称排序。
- **清理**：不再被任何显式安装的包需要的依赖会被卸载。
- **按锁重装**：当 lip 再次需要已安装包的文件时，会打开锁中记录的归档并校验其标识（SHA-256 或提交），而不是重新解析该包。

## 清单校验

各格式由仓库 `schemas/` 目录中的 JSON Schema 描述：`manifest.json` 为 `manifest.v4.schema.json`，`lip_lock.json` 为 `lock.v4.schema.json`；旧格式为 `manifest.schema.json`（tooth.json 格式 3）、`manifest.v1.schema.json`、`manifest.v2.schema.json` 和 `workspace_state.schema.json`（tooth_lock.json）。这些 schema 供编辑器和其他工具使用，lip 本身不执行 JSON Schema 校验。

- **校验**：lip 严格解析 `manifest.json`（UTF-8、不允许注释、不允许重复键），并拒绝键类型错误的文档，例如无效的 `name`、`version`、ref、范围或模板。指向无效文档的 ref 不产出候选，lip 会报告原因。
- **版本**：带有 `formatVersion` 4 和格式 UUID 的 `manifest.json` 是第 4 版文档；两者都没有的是普通 LeviLamina 清单。格式版本为 1 或 2 的 `tooth.json` 会在读取时于内存中转换为版本 3；如需把 `tooth.json` 转换为 `manifest.json`，请运行 `lip migrate`。

## 工作区状态

lip 在工作区根目录的锁文件 `lip_lock.json` 中维护已安装包状态（参见[锁文件](../concepts/lockfiles.md)），记录：
- 用户显式请求的包与隐式依赖；
- 精确安装版本，以及其来源归档；
- 每个包拥有的文件；
- 工作区的平台和 RID。

## 与规范的差异

lip 实现 lip 包规范（格式版本 4）时，有以下刻意的差异：

1. **`bedrock_server_mod.exe`**：除 `bedrock_server.exe` 外，工作区根目录中的 `bedrock_server_mod.exe` 也表示 `server`。装有 LeviLamina 的服务端会把 `bedrock_server.exe` 重命名为 `bedrock_server.exe.bak`，改为运行 `bedrock_server_mod.exe`。
2. **平台未知时延迟报错**：平台未知只有在需要用到它时才会报错：带 `platform` 选择器的条目、平台为 `server` 或 `client` 的具体包、<span v-pre>`{{platform}}`</span>，或安装 mod。没有 `client` 变体的 `tooth.json` 包从不需要平台，因此 `lip install github.com/LiteLDev/LeviLamina` 在空目录中也能执行。报错信息会提示你传入 `--platform`。
3. **迁移而来的锁条目**：从 `tooth_lock.json` 迁移而来的条目不记录归档标识。当 lip 需要其归档时，会重新解析标签 `path@version`。
4. **嵌套的 ZIP 归档**：位于另一个归档中的 ZIP 归档只按 SHA-256 锁定，并从缓存中重新找到。若缓存已被清理，lip 会重新解析记录的解析链，并且只接受 SHA-256 一致的结果。
5. **同一 tooth 的两个变体**：`tooth_lock.json` 可能以同一身份记录同一个 `tooth.json` 包的两个变体。两个条目都会保留并列出，并可分别用 `path#variant` 卸载。新的求解要求身份唯一。
