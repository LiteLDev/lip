# 常见问题

### 为什么 lip 要提供守护进程（`lipd`）？
守护进程（`lipd`）是为**外部集成**设计的。它让 IDE、GUI 和其他工具可以通过标准 JSON-RPC 接口，以编程方式与 lip 交互。

**注意**：标准 `lip` CLI 可以独立运行，**不会**使用守护进程。这样可保证 CLI 保持简单，并且日常命令没有额外启动开销。

### 什么是 `manifest.json`？
`manifest.json` 是 lip 包的清单文件。它就是 LeviLamina mod 的清单，再加上 lip 需要的内容：去哪里找包的依赖、在哪个平台上安装哪个构建，以及如何放置文件。参见[包清单](concepts/package_manifest.md)。

### 还能安装 `tooth.json` 包吗？
**可以。** lip 仍会读取 `tooth.json`（格式版本 1 到 3）：它在内存中把每个 `tooth.json` 转换为第 4 版的包，并像其他包一样安装，放置的文件与以前相同，因此已有的包仍可使用。lip 不再写入 `tooth.json`；请使用 [`lip migrate`](cli/commands/migrate.md) 把你的包转换为 `manifest.json`。

### `lip install` 会更新 `manifest.json` 吗？
**不会。** 运行 `lip install <package>` 会把包安装到工作区，并更新[锁文件](concepts/lockfiles.md)来记录状态，但**不会**修改你的 `manifest.json`。如果你希望依赖长期保留在包的要求中，需要手动把它加入 `manifest.json`。

### `tooth_lock.json` 去哪了？
锁现在是工作区中的 `.lip/lock.json`，因此会随工作区一起移动：移动、重命名或复制工作区后，已安装的包仍然保留。下次安装、卸载或更新包时，lip 会自动迁移已有的 `tooth_lock.json`，然后删除它。参见[锁文件](concepts/lockfiles.md#位置)。

### lip 能安装客户端 mod 吗？
不能。lip 只管理服务端工作区。客户端工作区由 LeviLauncher 等启动器管理，它们读取同样的 `manifest.json` 包。

### 为什么我仓库的某个标签不是版本？
只有名称恰为 `v` 加 [SemVer 2.0.0](https://semver.org/) 版本号的标签（例如 `v1.2.3`）才是版本。`v1.2` 或 `1.2.3` 这样的标签会被忽略。不过，仍可用 git 引用安装任意标签、分支或提交，例如 `lip install github.com/user/repo@tag:1.2.3` 或 `@branch:main`；此时包的版本是其 `manifest.json`（或 `tooth.json`）声明的 `version`。参见 [git 引用](cli/commands/install.md#git-引用)。

### 如何安装磁盘上的包？
把它打包为根目录含 `manifest.json` 的 `.mcmod`（或 `.zip`）文件，然后运行 `lip install ./package.mcmod`。lip 不从普通目录安装。锁会记录该文件的路径和 SHA-256：卸载不需要它，`lip update` 会重新读取该文件。

### 如何升级 lip 自身？
使用你最初的安装方式重新安装 `lip`，例如 `npm install -g @levimc/lip`、`winget upgrade levimc.lip`，或从 GitHub Releases 页面下载最新的 Windows 安装包（`setup.exe` 或 `.msi`）。目前还没有内置的 `lip self-update` 命令。如果你是通过旧的 `@futrime/lip` npm 包或 `futrime.lip` winget 包安装的 `lip`，请先卸载，再安装 `@levimc/lip` 或 `levimc.lip`。

### lip 需要安装 git 吗？
不需要。lip 使用内置客户端通过 HTTPS 访问 git 仓库，无需安装 `git` 命令行工具。详见[内部机制](guides/internal_mechanism.md#包解析)。

### lip 支持私有仓库吗？
目前不支持。lip 通过 HTTPS 匿名获取包，不使用 `git` 命令行工具或其凭据管理器，因此只能从公开仓库安装。

### lip 会把安装的包放在哪里？
lip 会将包文件**直接安装到你的工作区**。mod 安装到 `mods/<name>/`，而 `plugins` 是指向 `mods` 的链接（参见[安装依赖](guides/installing_dependencies.md#仅限服务端工作区)）；文件包按其 `manifest.json` 的规定放置文件。

工作区的[锁文件](concepts/lockfiles.md)是其中的 `.lip/lock.json`。lip 自己的其他文件保存在遵循平台惯例的用户目录中：

| 内容 | Windows | Linux / macOS |
| --- | --- | --- |
| 配置（`config.yaml`） | `%APPDATA%\lip` | `$XDG_CONFIG_HOME/lip`（默认 `~/.config/lip`） |
| 操作日志和操作锁（`workspaces/<hash>.journal`、`workspaces/<hash>.lock`） | `%LOCALAPPDATA%\lip` | `$XDG_DATA_HOME/lip`（默认 `~/.local/share/lip`） |
| 缓存 | `%LOCALAPPDATA%\lip\Cache` | `$XDG_CACHE_HOME/lip`（默认 `~/.cache/lip`） |

缓存可以随时删除，例如使用 `lip cache clean`。正在进行的下载会写入缓存中隐藏的 `.staging` 目录；lip 不向系统临时目录写入任何内容。

### 安装到一半失败会怎样？
什么都不会改变。`lip install`、`lip uninstall` 和 `lip update` 要么全部完成，要么全部不做，其安装或移除的依赖也包含在内：lip 在改动工作区之前先完成所有检查和下载；若文件无法写入、脚本失败或按下 Ctrl+C，它会撤销已做的全部修改，锁保持原样。只有包脚本所做的事无法撤销。若 lip 在中途被终止，下一次对该工作区的 `lip install`、`uninstall` 或 `update` 会先处理被中断的操作：若它已保存锁，则将其完成，否则将其撤销，并告知你。参见[内部机制](guides/internal_mechanism.md#工作区状态)。

### 可以同时运行两个 lip 命令吗？
不能作用于同一个工作区：一个 `lip install`、`uninstall` 或 `update` 正在修改某个工作区时，对该工作区的另一个此类命令会立即失败，并提示"Another lip process is changing the workspace"。作用于不同工作区的命令，以及只读取的命令（如 `lip list` 或 `lip view`），随时都可以运行。
