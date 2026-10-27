# 常见问题

### 为什么 lip 要提供守护进程（`lipd`）？
守护进程（`lipd`）是为**外部集成**设计的。它让 IDE、GUI 和其他工具可以通过标准 JSON-RPC 接口，以编程方式与 lip 交互。

**注意**：标准 `lip` CLI 可以独立运行，**不会**使用守护进程。这样可保证 CLI 保持简单，并且日常命令没有额外启动开销。

### 什么是 `manifest.json`？
`manifest.json` 是 lip 包的清单文件。它就是 LeviLamina mod 的清单，再加上 lip 需要的内容：去哪里找包的依赖、在哪个平台上安装哪个构建，以及如何放置文件。参见[包清单](concepts/package_manifest.md)。

### 还能安装 `tooth.json` 包吗？
**可以。** lip 仍会读取 `tooth.json`（格式版本 1 到 3），因此已有的包仍可使用。lip 不再写入 `tooth.json`；请使用 [`lip migrate`](cli/commands/migrate.md) 把你的包转换为 `manifest.json`。

### `lip install` 会更新 `manifest.json` 吗？
**不会。** 运行 `lip install <package>` 会把包安装到工作区，并更新锁文件（`lip_lock.json`）来记录状态，但**不会**修改你的 `manifest.json`。如果你希望依赖长期保留在包的要求中，需要手动把它加入 `manifest.json`。

### `tooth_lock.json` 去哪了？
它已被 `lip_lock.json` 取代。下次安装、卸载或更新包时，lip 会自动迁移已有的 `tooth_lock.json`，然后将其删除。参见[锁文件](concepts/lockfiles.md#旧格式-tooth-lock-json)。

### lip 能安装客户端 mod 吗？
不能。lip 只管理服务端工作区。客户端工作区由 LeviLauncher 等启动器管理，它们读取同样的 `manifest.json` 包。

### 为什么我仓库的某个标签不是版本？
只有名称恰为 `v` 加 [SemVer 2.0.0](https://semver.org/) 版本号的标签（例如 `v1.2.3`）才是版本。`v1.2` 或 `1.2.3` 这样的标签会被忽略。

### 如何升级 lip 自身？
使用你最初的安装方式重新安装 `lip`，例如 `npm install -g @levimc/lip`、`winget upgrade levimc.lip`，或从 GitHub Releases 页面下载最新的 Windows `setup.exe`。目前还没有内置的 `lip self-update` 命令。如果你是通过旧的 `@futrime/lip` npm 包或 `futrime.lip` winget 包安装的 `lip`，请先卸载，再安装 `@levimc/lip` 或 `levimc.lip`。

### lip 需要安装 git 吗？
不需要。lip 使用内置客户端通过 HTTPS 访问 git 仓库，无需安装 `git` 命令行工具。详见[内部机制](guides/internal_mechanism.md#包解析)。

### lip 支持私有仓库吗？
目前不支持。lip 通过 HTTPS 匿名获取包，不使用 `git` 命令行工具或其凭据管理器，因此只能从公开仓库安装。

### lip 会把安装的包放在哪里？
lip 会将包文件**直接安装到你的工作区**。mod 安装到 `plugins/<name>/`；文件包按其 `manifest.json` 的规定放置文件。

全局缓存位于 Windows 的 `%LOCALAPPDATA%\lip\cache`，Linux 和 macOS 的 `$XDG_CACHE_HOME/lip`（默认 `~/.cache/lip`）。它可以随时删除，例如使用 `lip cache clean`。配置位于 `%APPDATA%\lip` 或 `$XDG_CONFIG_HOME/lip`（默认 `~/.config/lip`），临时文件位于 `%TEMP%\lip` 或 `$TMPDIR/lip`（默认 `/tmp/lip`）。
