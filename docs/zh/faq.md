# 常见问题

### 为什么 lip 要提供守护进程（`lipd`）？
守护进程（`lipd`）是为**外部集成**设计的。它让 IDE、GUI 和其他工具可以通过标准 JSON-RPC 接口，以编程方式与 lip 交互。

**注意**：标准 `lip` CLI 可以独立运行，**不会**使用守护进程。这样可保证 CLI 保持简单，并且日常命令没有额外启动开销。

### 什么是 `tooth.json`？
`tooth.json` 是 lip 包的清单文件。它定义了包的元数据（名称、版本、描述）、依赖关系，以及在不同平台上的安装方式。可以把它理解为 Node.js 的 `package.json` 或 Rust 的 `Cargo.toml`。

### `lip install` 会更新 `tooth.json` 吗？
**不会。** 运行 `lip install <package>` 会把包安装到工作区，并更新锁文件（`tooth_lock.json`）来记录状态，但**不会**修改你的 `tooth.json` 清单。如果你希望依赖长期保留在项目要求中，需要手动把它加入 `tooth.json`。

### 如何升级 lip 自身？
使用你最初的安装方式重新安装 `lip`，例如 `npm install -g @levimc/lip`、`winget upgrade levimc.lip`，或从 GitHub Releases 页面下载最新的 Windows `setup.exe`。目前还没有内置的 `lip self-update` 命令。如果你是通过旧的 `@futrime/lip` npm 包或 `futrime.lip` winget 包安装的 `lip`，请先卸载，再安装 `@levimc/lip` 或 `levimc.lip`。

### lip 需要安装 git 吗？
不需要。lip 使用内置客户端通过 HTTPS 访问 git 仓库，无需安装 `git` 命令行工具。详见[内部机制](guides/internal_mechanism.md#包解析)。

### lip 支持私有仓库吗？
目前不支持。lip 通过 HTTPS 匿名获取包，不使用 `git` 命令行工具或其凭据管理器，因此只能从公开仓库安装。

### lip 会把安装的包放在哪里？
lip 会根据 `tooth.json` 中的规则，将包文件**直接安装到你的工作区**。与某些包管理器不同，它不会默认把依赖隔离到单独目录，除非清单中明确指定。

全局缓存位于系统本地应用数据目录（例如 Windows 的 `%LOCALAPPDATA%\\lip\\cache`，Linux 的 `~/.local/share/lip/cache`）。
