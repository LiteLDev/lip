# 内部机制

本指南说明 lip 的底层工作方式。

## 架构

`lip` 可执行文件提供两种接口：

1. **CLI**：`lip install` 等命令，供终端中的用户和脚本使用。它们独立运行，**不会**经过 `lip rpc`。
2. **JSON-RPC（`lip rpc`）**：[`lip rpc`](../cli/commands/rpc.md) 命令，供外部应用（IDE、GUI、脚本）以编程方式调用 lip。

两种接口共享同一套核心逻辑，保证无论通过哪种接口，行为都一致。

`lip rpc` 通过 **stdio 上的 JSON-RPC** 通信，使工具无需为每个命令反复拉起进程，也无需解析文本输出。

## 流程

`lip install`、`lip uninstall` 和 `lip update` 执行相同的步骤：

1. **工作区文件**：从 `.lip/workspace.json` 加载该工作区的状态。早期 lip 版本的工作区会被拒绝（参见[工作区文件](../concepts/lockfiles.md#早期格式)）。
2. **环境**：确定平台和 RID（参见[环境](../concepts/tooth_json.md#环境)）。
3. **解析器**：把每个来源转换为一个候选序列：它所指向的归档，最新版本在前，每个都带有所请求变体在该环境下的条目。
4. **依赖图求解器**：为每个请求和每个依赖各选出一个候选，使每个身份只出现一次，且没有两个 mod 的 mod 名称相同。
5. **规划**：规划按安装顺序的逆序卸载离开集合的包、再按依赖优先的顺序安装进入集合的包，每一步都基于之前各步完成后的工作区。规划会下载所有要写入的归档和资源，并检查一切可能失败的情形：文件所有权、`preserve_files`、路径、放置规则的 `src`、mod 的 LeviLamina `manifest.json` 及其 `dependencies`，以及 `plugins`/`mods` 目录布局。早期格式的 `tooth.json` 在解析时已被读取为格式 4（参见[早期格式](../concepts/tooth_json.md#早期格式)），因此所有包都由同一个安装器处理。
6. **执行**：必要时让 `plugins` 成为指向 `mods` 的链接（参见[安装依赖](installing_dependencies.md#仅限服务端工作区)），然后运行规划好的钩子与文件修改。放到 `plugins/` 下的文件会写入 `mods/` 并按 `mods/` 记录。
7. **工作区文件**：所有步骤成功之后，保存一次工作区文件。

操作要么全部完成，要么全部不做（[工作区协议](https://github.com/LiteLDev/lip/blob/main/protocol/workspace.md#6-atomicity) §6"原子性"）。第 6 步之前的任何失败都不会修改工作区。执行时某一步失败（文件无法写入、钩子命令以非零码退出，或按下 Ctrl+C），lip 会撤销该操作的全部修改（包括目录布局），工作区文件保持原样。钩子命令所做的事无法撤销：`post_install` 或 `pre_uninstall` 命令失败时，操作同样失败并撤销 lip 的文件修改，但已运行命令的效果不会被撤销。

## 包解析

lip 不使用中心化的包注册表或模块代理。像 `github.com/user/repo` 这样的 tooth 路径直接对应 git 仓库 `https://github.com/user/repo.git`，lip 通过内置的 git smart-HTTP 协议（第 2 版）客户端与其通信。**无需安装 `git` 命令行工具**，lip 也从不调用它。

- **版本**：lip 通过 `ls-refs` 列出仓库标签（只请求 `refs/tags/v*`）。可用版本即名称恰为 `v<SemVer 2.0.0>` 的标签（例如 `v1.2.3`）；其他标签，包括 `v1.2` 或 `v01.2.3` 这类宽松写法，都会被忽略。附注标签会被解析到其指向的提交。
- **git 引用**：带 git 引用的 tooth 路径只有一个候选。`@branch:<name>` 和 `@tag:<name>` 通过 `ls-refs` 恰好请求 `refs/heads/<name>` 或 `refs/tags/<name>` 来解析（附注标签会被解析到其提交），每条命令只解析一次；`@rev:<commit>` 无需请求，提交按其 ID 获取。不接受缩写的提交 ID：解析缩写需要仓库的全部历史，而 lip 从不下载全部历史。
- **惰性候选**：tooth 路径的候选从最新标签开始按需产出，因此 lip 只会打开求解器实际需要的标签。
- **tooth.json**：读取某个版本的 `tooth.json` 时，lip 只获取该标签提交的树对象（使用 `filter blob:none` 的浅层部分获取，不传输任何文件内容），然后只下载这一个文件。因此无论仓库多大，解析依赖时每个版本只需几 KB 流量。
- **候选**：只有当某个版本的 `tooth.json` 合法、其 `tooth` 是该仓库的路径、其 `version` 等于标签版本，且所请求的变体有对应工作区平台和 RID 的条目时，该版本才是候选。否则 lip 继续查看下一个版本。
- **安装**：只有安装器需要时才下载文件：`self` 资源从包自身归档中放置的文件，以及 `zip` 和 `mcmod` 资源；后者在包安装时下载，与其他 HTTP 下载相同。
- **完整性**：每个下载的 git 对象都会按其 git 对象 ID（SHA-1）校验。ZIP 归档和资源以其 SHA-256 标识。
- **缓存**：全局缓存按数据的传输方式命名每个条目。下载的 git 文件存为 `git-<对象 ID>`，相同的文件只下载一次，可在不同版本和不同包之间复用。通过 HTTP(S) 下载的内容一律存为 `http-<URL 的 SHA-256>`；重新打开工作区文件中记录的 ZIP 归档或资源时，lip 会用工作区文件中的 SHA-256 校验缓存文件，缺失或不一致时重新下载。下载内容先写入缓存中隐藏的 `.staging` 目录，完成后再移动到位；lip 不向系统临时目录写入任何内容。标签列表和树对象仅在单次命令执行期间缓存在内存中；每次运行都会重新查询标签，因此被移动的标签会立即生效。
- **代理**：若配置了 `github_proxy`，对 `github.com` 仓库的请求会先经由该代理发送；若代理请求失败，lip 会直接向 `github.com` 重试。从 `github.com` URL 下载发布资源时遵循同样的规则。

只有当某个版本的标签仍存在于仓库中时，该版本才能被安装。目前只支持可通过 HTTPS 访问的公开仓库。比较 tooth 路径时不区分 ASCII 大小写。

git 引用所给的分支、提交或名称不是 `v<SemVer>` 的标签没有标签版本：使用该处 `tooth.json` 的 `version`。版本标签处早期格式的 `tooth.json` 按标签版本安装，而不论其 `version` 写的是什么。

## 求解

- **固定**：已安装的包按原样进入求解器，从工作区文件重建，无需访问网络。若这样无法满足请求，lip 会只固定显式安装的包重新求解，此时依赖可以改变版本。
- **优先级**：靠前的候选优先：tooth 路径的最新版本。
- **顺序**：先安装依赖；依赖成环的包按身份排序。
- **清理**：不再被任何显式安装的包需要的依赖会被卸载。
- **按工作区文件重装**：当 lip 再次需要已安装包的文件时，会打开工作区文件中记录的归档和资源，使用其中记录的条目，并校验其标识（SHA-256 或提交），而不是重新解析该包。

## tooth.json 校验

`tooth.json` 由 JSON Schema [`protocol/tooth.schema.json`](https://github.com/LiteLDev/lip/blob/main/protocol/tooth.schema.json) 描述，工作区文件由 [`protocol/workspace.schema.json`](https://github.com/LiteLDev/lip/blob/main/protocol/workspace.schema.json) 描述。早期格式曾由原 [`specs/schemas/`](https://github.com/LiteLDev/lip/blob/1e023e6/specs/schemas) 目录中以 YAML 编写的 JSON Schema 描述，`legacy/v<格式>/` 下：tooth.json 格式 1、2、3 为 `tooth.schema.yaml`，tooth_lock.json 为 `tooth_lock.schema.yaml`，liprc.json 为 `liprc.schema.yaml`（均为格式 3）。

- **校验**：除 schema 之外，lip 还检查[包协议](https://github.com/LiteLDev/lip/blob/main/protocol/package.md#3-tooth-files)的要求：合法的 tooth 路径、版本、依赖键和范围、平台和 RID，同一变体中没有两个条目对应相同的平台和 RID，每个条目至多一个 `mcmod` 资源，以及合法的 glob。`tooth.json` 无效的归档不是包，不产出候选，lip 会报告原因。
- **版本**：`format_version` 为 4 的 `tooth.json` 是当前格式。格式 1 和 2 会在读取时于内存中转换为格式 3，格式 3 再针对环境读取为格式 4（[包协议](https://github.com/LiteLDev/lip/blob/main/protocol/package.md#5-earlier-formats) §5）；如需把包仓库的 `tooth.json` 改写为格式 4，请在仓库中运行 `lip migrate`。

## 工作区状态

lip 在保存于工作区中 `.lip/workspace.json` 的工作区文件里维护已安装包状态（参见[工作区文件](../concepts/lockfiles.md#位置)），记录：
- 用户显式请求的包与隐式依赖；
- 精确安装版本，以及其来源归档和资源；
- 每个包拥有的文件；
- 工作区的平台和 RID。

为保证操作原子性而写入的文件都是隐藏文件，并位于目标旁边：工作区文件先写入 `.lip/workspace.json.new`，再重命名覆盖 `.lip/workspace.json`；操作要覆盖或删除的文件先在同一目录中重命名为 `.<name>.bak`（若该名称已被占用，则为 `.<name>.bak1`、`.<name>.bak2`……）。工作区文件保存后删除这些备份（无法删除的备份，例如被某个程序打开的，会保留并在警告中列出），操作失败时则改回原名。卸载后变空的目录也在此时删除。在 Windows 上，这些文件还会被设置隐藏属性。

下面的日志和工作区锁与工作区文件一起保存在 `.lip` 中，因此随工作区移动：移动或复制后的工作区会带走被中断的操作，日志中记录的是相对于工作区的路径。它们的格式定义在 [`protocol/workspace.md`](https://github.com/LiteLDev/lip/blob/main/protocol/workspace.md#7-the-lip-directory) 中，其他程序也可以参与。

- **日志**：操作修改工作区期间，lip 在做出每项修改之前，先把它记录到 `.lip/journal` 中。若 lip 被终止，下一次对该工作区的 `lip install`、`uninstall` 或 `update` 会先读取日志：撤销被中断的操作（恢复备份）并报告；若该操作已进行到保存工作区文件的阶段，则删除备份以完成它。试运行（dry run）不获取锁，只报告日志的存在。
- **同一时间只有一个操作**：操作期间独占打开 `.lip/lock`（在 Linux 和 macOS 上为独占 `flock`；该文件会保留）。对同一工作区的第二个 `lip install`、`uninstall` 或 `update` 会立即失败，并提示"Another process is changing the workspace"；请在第一个操作结束后重新运行。

## 关于规范的说明

lip 只管理服务端工作区：它从不从工作区判断平台，而是总是使用 `server`（[CLI 协议](https://github.com/LiteLDev/lip/blob/main/protocol/cli.md#4-environment-and-workspace-operations) §4）。客户端工作区交由实现同一协议的启动器（如 LeviLauncher）处理。

lip 实现 [lip 协议](https://github.com/LiteLDev/lip/blob/main/protocol/README.md)。协议没有规定如何接管早期 lip 版本的工作区（[工作区协议](https://github.com/LiteLDev/lip/blob/main/protocol/workspace.md#76-earlier-formats) §7.6）；lip 不接管它们，并在这样的工作区上失败（参见[工作区文件](../concepts/lockfiles.md#早期格式)）。
