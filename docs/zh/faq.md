# 常见问题

### 什么是 `lip.yaml`？
`lip.yaml` 是 lip 包的包文件：它给出包 ID、版本，以及针对每个变体、平台和 RID 的依赖、要放置的文件、要安装的 mod 和要运行的命令。参见 [lip.yaml](concepts/lip_yaml.md)。

### 还能安装带有 `tooth.json` 的 lip v0.x 包吗？
**可以。** lip 仍会读取格式版本 1 到 3 的 `tooth.json`：它针对工作区的平台和 RID 把每个文件读取为包文件，并像其他包一样安装，放置的文件与以前相同，因此已有的包仍可使用。lip 不再写入 `tooth.json`；请使用 [`lip migrate`](cli/commands/migrate.md) 把你的包转换为 `lip.yaml`。lip v0.x 的工作区（带有 `tooth_lock.json`）和 `liprc.json` 会被自动迁移。

### `lip install` 会更新 `lip.yaml` 吗？
**不会。** 运行 `lip install <package>` 会把包安装到工作区，并更新[工作区文件](concepts/lockfiles.md)来记录状态，但**不会**修改任何 `lip.yaml`。如果你希望依赖长期保留在包的要求中，需要手动把它加入你的 `lip.yaml` 的 `deps`。

### lip 能安装客户端 mod 吗？
不能。lip 只管理服务端工作区。客户端工作区由 LeviLauncher 等启动器管理，它们读取同样的包。

### 为什么我仓库的某个标签不是版本？
只有名称恰为 `v` 加 [SemVer 2.0.0](https://semver.org/) 版本号的标签（例如 `v1.2.3`）才是版本。`v1.2` 或 `1.2.3` 这样的标签会被忽略。不过，仍可用 git 引用安装任意标签、分支或提交，例如 `lip install github.com/user/repo@tag:1.2.3` 或 `@branch:main`；此时包的版本是其 `lip.yaml` 声明的 `version`。参见 [git 引用](cli/commands/install.md#git-引用)。

### 如何安装磁盘上的包？
用根部带有 `lip.yaml` 的目录或 ZIP 文件的路径运行 `lip install`，例如 `lip install ../MyTool` 或 `lip install ./package.zip`。工作区文件会记录该路径：卸载不需要它，`lip update` 会重新读取它。

### 如何升级 lip 自身？
使用你最初的安装方式重新安装 `lip`，例如从 GitHub Releases 页面下载最新的 Windows 安装包（`setup.exe` 或 `.msi`）或便携压缩包。目前还没有内置的 `lip self-update` 命令。如果你是通过旧的 `futrime.lip` winget 包或 npm 安装的 `lip`，请先卸载，再按[安装](intro/installation.md)中的方式重新安装。

### lip 需要安装 git 吗？
不需要。lip 使用内置客户端通过 HTTPS 访问 git 仓库，无需安装 `git` 命令行工具。详见[内部机制](guides/internal_mechanism.md#包解析)。

### lip 支持私有仓库吗？
目前不支持。lip 通过 HTTPS 匿名获取包，不使用 `git` 命令行工具或其凭据管理器，因此只能从公开仓库安装。

### lip 会把安装的包放在哪里？
lip 会将包文件**直接安装到你的工作区**。mod 安装到 `mods/<name>/`，`<name>` 为其 LeviLamina `manifest.json` 中的 `name`；文件包按其 `lip.yaml` 的规定放置文件。

工作区的[工作区文件](concepts/lockfiles.md)是其中的 `.lip/workspace.yaml`；操作锁（`.lip/lock`）和日志（`.lip/journal`）位于它旁边。lip 自己的其他文件保存在遵循平台惯例的用户目录中：

| 内容 | Windows | Linux / macOS |
| --- | --- | --- |
| 配置（`config.yaml`） | `%APPDATA%\lip` | `$XDG_CONFIG_HOME/lip`（默认 `~/.config/lip`） |
| 缓存 | `%LOCALAPPDATA%\lip\Cache` | `$XDG_CACHE_HOME/lip`（默认 `~/.cache/lip`） |

缓存可以随时删除，例如使用 `lip cache clean`。正在进行的下载会写入缓存中的 `.tmp` 文件，完成后再重命名到位；lip 不向系统临时目录写入任何内容。

### 安装到一半失败会怎样？
什么都不会改变。`lip install`、`lip uninstall` 和 `lip update` 要么全部完成，要么全部不做，其安装或移除的依赖也包含在内：lip 在改动工作区之前先完成所有检查和下载；若文件无法写入、钩子命令失败或按下 Ctrl+C，它会撤销已做的全部修改，工作区文件保持原样。只有包的钩子命令所做的事无法撤销。若 lip 在中途被终止，下一次对该工作区的 `lip install`、`uninstall` 或 `update` 会先处理被中断的操作：若它已保存工作区文件，则将其完成，否则将其撤销，并告知你。参见[内部机制](guides/internal_mechanism.md#工作区状态)。

### 可以同时运行两个 lip 命令吗？
不能作用于同一个工作区：一个 `lip install`、`uninstall` 或 `update` 正在修改某个工作区时，对该工作区的另一个此类命令会立即失败，并提示另一个 lip 命令正在修改该工作区。作用于不同工作区的命令，以及只读取的命令（如 `lip list` 或 `lip view`），随时都可以运行。

### 命令失败了，如何查明原因？
lip 会输出出了什么问题、每一层原因，以及你可以怎么做。除非错误出在命令行本身，它还会在当前目录的 `.lip/logs/` 中写入一份失败报告，并输出其路径。报告包含 lip 关于这次失败所知的一切：lip 版本、命令、系统、工作区状态、包含每一层原因和堆栈跟踪的完整错误，以及本次运行的跟踪记录（网络请求、git 操作、缓存使用、版本解析、文件改动和钩子命令的输出）。URL 中的凭据会被隐去。lip 保留最新的 20 份报告。[报告问题](https://github.com/LiteLDev/lip/issues)时请附上该报告。
