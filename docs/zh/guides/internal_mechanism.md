# 内部机制

本指南说明 lip 的底层工作方式。

## 流程

`lip setup`、`lip install`、`lip uninstall` 和 `lip update` 执行相同的步骤：

1. **状态文件**：从 `.lip/state.json` 加载该工作区的状态。没有该文件的工作区没有已安装的包，除非它有 lip v0.x 的 `tooth_lock.json`，此时改为读取后者（参见 [lip v0.x 的工作区](../concepts/lockfiles.md#lip-v0-x-的工作区)）。
2. **环境**：确定运行端和平台（参见[环境](../concepts/lip_toml.md#环境)）。
3. **解析器**：把每个来源转换为一个候选序列：它所指向的归档，最新版本在前，每个都带有所请求变体在该环境下的条目。
4. **依赖图求解器**：为每个请求和每个依赖各选出一个候选，使每个身份只出现一次，且没有两个 mod 的 mod 名称相同。
5. **规划**：规划按安装顺序的逆序卸载离开集合的包、再按依赖优先的顺序安装进入集合的包，每一步都基于之前各步完成后的工作区。规划会下载所有要写入的归档和资源，并检查一切可能失败的情形：文件所有权、`preserve`、路径、放置规则的 `src`、mod 的 LeviLamina `manifest.json` 中的 mod 名称，以及（当操作写入或删除文件时）`mods` 是否为链接。早期格式的 `tooth.json` 在解析时已被完整地转换为包文件（参见[早期格式](../concepts/lip_toml.md#早期格式)），因此所有包都由同一个安装器处理，且只按 `lip.toml` 的规则处理。
6. **执行**：当操作要为 Windows 平台运行钩子命令，或要设置 Windows 版服务器时，先安装它们需要的系统组件（参见[系统依赖](#系统依赖)）。当操作写入或删除文件时，若 `mods` 不存在则创建它（参见[安装依赖](installing_dependencies.md#仅限服务端工作区)），然后运行规划好的钩子与文件修改。匹配 `preserve` 的文件是否保留，取决于即将写入它时（在此之前的钩子命令运行之后）它是否存在；包只拥有它实际写入的文件。
7. **状态文件**：所有步骤成功之后，保存一次状态文件。

操作要么全部完成，要么全部不做（[lip-workspace(7)](https://github.com/LiteLDev/lip/blob/main/protocol/lip-workspace.md#atomicity) "Atomicity"）。第 6 步之前的任何失败都不会修改工作区。执行时某一步失败（文件无法写入、钩子命令以非零码退出，或按下 Ctrl+C），lip 会撤销该操作的全部修改，状态文件保持原样。钩子命令所做的事无法撤销：`post_install` 或 `pre_uninstall` 命令失败时，操作同样失败并撤销 lip 的文件修改，但已运行命令的效果不会被撤销。

## 包解析

lip 不使用中心化的包注册表或模块代理。像 `github.com/user/repo` 这样的包 ID 直接对应 git 仓库 `https://github.com/user/repo.git`，lip 通过内置的 git smart-HTTP 协议（第 2 版）客户端与其通信。**无需安装 `git` 命令行工具**，lip 也从不调用它。

- **版本**：lip 通过 `ls-refs` 列出仓库标签（只请求 `refs/tags/v*`）。可用版本即名称恰为 `v<SemVer 2.0.0>` 的标签（例如 `v1.2.3`）；其他标签，包括 `v1.2` 或 `v01.2.3` 这类宽松写法，都会被忽略。附注标签会被解析到其指向的提交。
- **git 引用**：带 git 引用的来源只有一个候选。`@branch:<name>` 和 `@tag:<name>` 通过 `ls-refs` 恰好请求 `refs/heads/<name>` 或 `refs/tags/<name>` 来解析（附注标签会被解析到其提交），每条命令只解析一次；`@rev:<commit>` 无需请求，提交按其 ID 获取。不接受缩写的提交 ID：解析缩写需要仓库的全部历史，而 lip 从不下载全部历史。
- **惰性候选**：仓库的候选从最新标签开始按需产出，因此 lip 只会打开求解器实际需要的标签。
- **lip.toml**：读取某个版本的 `lip.toml` 时，lip 只获取该标签提交的树对象（使用 `filter blob:none` 的浅层部分获取，不传输任何文件内容），然后只下载这一个文件（没有 `lip.toml` 时则为 `tooth.json`）。因此无论仓库多大，解析依赖时每个版本只需几 KB 流量。
- **候选**：只有当某个版本的 `lip.toml` 合法、其 `id` 是该仓库的路径、其 `version` 等于标签版本，且所请求的变体有对应工作区运行端和平台的条目时，该版本才是候选。否则 lip 继续查看下一个版本。
- **安装**：只有安装器需要时才下载文件：`self` 资源从包自身归档中放置的文件，以及 `zip` 和 `mcmod` 资源；后者在包安装时下载，与其他 HTTP 下载相同。
- **完整性**：每个下载的 git 对象都会按其 git 对象 ID（SHA-1）校验。ZIP 归档和资源会被检查是否为有效的 ZIP 归档；其内容的校验和既不记录也不检查。
- **缓存**：git 归档不会被缓存：每次只获取操作需要读取的文件。通过 HTTP(S) 下载的内容一律在缓存中存为 `http-<URL 的 SHA-256>`；不是有效 ZIP 归档的缓存文件会被重新下载。下载内容先写入缓存目录中的 `.tmp` 文件，完成后再重命名到位；lip 不向系统临时目录写入任何内容。标签列表和树对象仅在单次命令执行期间缓存在内存中；每次运行都会重新查询标签，因此被移动的标签会立即生效。
- **代理**：若 `github_proxy` 设置为 URL，对 `github.com` 仓库的请求会先经由该代理发送；若代理请求失败，lip 会直接向 `github.com` 重试。从 `github.com` URL 下载发布资源时遵循同样的规则。

只有当某个版本的标签仍存在于仓库中时，该版本才能被安装。目前只支持可通过 HTTPS 访问的公开仓库。比较包 ID 时不区分 ASCII 大小写。

git 引用所给的分支、提交或名称不是 `v<SemVer>` 的标签没有标签版本：使用该处 `lip.toml` 的 `version`。版本标签处早期格式的 `tooth.json` 按标签版本安装，而不论其 `version` 写的是什么。

## 求解

- **固定**：已安装的包按原样进入求解器，从状态文件重建，无需访问网络。若这样无法满足请求，lip 会只固定显式安装的包重新求解，此时依赖可以改变版本；求解时优先尝试它们已安装的版本，因此只有必须改变的依赖才会改变。`lip update` 从一开始就这样求解，因此已安装的依赖不会妨碍被更新的包取得最新版本。
- **优先级**：靠前的候选优先：仓库的最新版本。
- **顺序**：先安装依赖；依赖成环的包按身份排序。
- **清理**：不再被任何显式安装的包需要的依赖会被卸载。
- **同一个包**：只有 lip 解析出的包在身份、版本或提交上不同时，才会替换已安装的包；来自 URL 或本地路径的包没有提交。
- **按状态文件重装**：当 lip 再次需要已安装包的文件时，会用状态文件中记录的包文件安装该包，而不是重新解析该包：对于来自 git 仓库的包，从记录的提交安装；否则从其来源现在指向的归档安装。

## 包文件校验

`lip.toml` 和 `config.toml` 分别由以 TOML 编写的 JSON Schema [`protocol/lip.schema.toml`](https://github.com/LiteLDev/lip/blob/main/protocol/lip.schema.toml) 和 [`protocol/config.schema.toml`](https://github.com/LiteLDev/lip/blob/main/protocol/config.schema.toml) 描述，状态文件由 JSON Schema [`protocol/state.schema.json`](https://github.com/LiteLDev/lip/blob/main/protocol/state.schema.json) 描述。lip v0.x 的文件曾由原 [`specs/schemas/`](https://github.com/LiteLDev/lip/blob/1e023e6/specs/schemas) 目录中的 JSON Schema 描述，`legacy/v<格式>/` 下：tooth.json 格式 1、2、3 为 `tooth.schema.yaml`，tooth_lock.json 为 `tooth_lock.schema.yaml`，liprc.json 为 `liprc.schema.yaml`（均为格式 3）。

- **校验**：除 schema 之外，lip 还检查 [lip.toml(5)](https://github.com/LiteLDev/lip/blob/main/protocol/lip.toml.md) 的要求：合法的包 ID、版本、依赖键和范围、运行端和平台，同一变体中没有两个条目对应相同的运行端和平台，每个条目至多一个 `mcmod` 资源，以及合法的 glob。`lip.toml` 无效的归档不是包，不产出候选，lip 会报告原因。
- **lip v0.x 的文件**：没有 `lip.toml` 的归档从其格式 1、2 或 3 的 `tooth.json` 读取：格式 1 和 2 会在内存中转换为格式 3，格式 3 再针对环境完整地转换为包文件（[lip-compatibility(7)](https://github.com/LiteLDev/lip/blob/main/protocol/lip-compatibility.md#toothjson) "tooth.json"）；如需在包仓库中用 `lip.toml` 取代 `tooth.json`，请在仓库中运行 `lip pkg migrate`（"lip pkg migrate"）。`tooth_lock.json` 会被自动迁移为 `.lip/state.json`（"tooth_lock.json"），`liprc.json` 会被自动迁移为 `config.toml`（"liprc.json"）。

## 工作区状态

lip 在保存于工作区中 `.lip/state.json` 的状态文件里维护已安装包状态（参见[状态文件](../concepts/lockfiles.md#位置)），记录：
- 用户显式请求的包与隐式依赖；
- 安装每个包时所用的包文件（因而包括其精确版本）、其来源，以及来自 git 仓库的包所在的提交；
- 每个包拥有的文件；
- 工作区的运行端和平台。

为保证操作原子性而写入的文件都是隐藏文件，并位于目标旁边：状态文件先写入 `.lip/state.json.tmp`，再重命名覆盖 `.lip/state.json`；操作要覆盖或删除的文件先在同一目录中重命名为 `.<name>.bak`（若该名称已被占用，则为 `.<name>.bak1`、`.<name>.bak2`……）。状态文件保存后删除这些备份（无法删除的备份，例如被某个程序打开的，会保留并在警告中列出），操作失败时则改回原名。卸载后变空的目录也在此时删除。在 Windows 上，这些文件还会被设置隐藏属性。

下面的日志和工作区锁与状态文件一起保存在 `.lip` 中，因此随工作区移动：移动或复制后的工作区会带走被中断的操作，日志中记录的是相对于工作区的路径。它们的格式定义在 [`protocol/journal.md`](https://github.com/LiteLDev/lip/blob/main/protocol/journal.md) 和 [`protocol/lip-workspace.md`](https://github.com/LiteLDev/lip/blob/main/protocol/lip-workspace.md#workspace-lock) 中，其他程序也可以参与。

- **日志**：操作修改工作区期间，lip 在做出每项修改之前，先把它记录到 `.lip/journal` 中。若 lip 被终止，下一次在该工作区中运行的 `lip setup`、`install`、`uninstall`、`update` 或 `run` 会先读取日志：撤销被中断的操作（恢复备份）并报告；若该操作已进行到保存状态文件的阶段，则删除备份以完成它。试运行（dry run）不获取锁，只报告日志的存在。
- **同一时间只有一个操作**：操作期间独占打开 `.lip/lock`（在 Linux 和 macOS 上为独占 `flock`；该文件会保留）。对同一工作区的第二个 `lip setup`、`install`、`uninstall` 或 `update` 会立即失败，并提示"Another process is changing the workspace"；请在第一个操作结束后重新运行。[`lip run`](../cli/commands/run.md) 只在启动服务器之前持有该锁。

## 系统依赖

平台以 `win` 开头的工作区中的程序（钩子命令和 Windows 版服务器，无论是否带 LeviLamina）需要系统提供两样东西，由 lip 自行安装（[lip-workspace(7)](https://github.com/LiteLDev/lip/blob/main/protocol/lip-workspace.md#system-dependencies) "System dependencies"）：

- **Wine**，在 Linux 和 macOS 上：lip 的版本指定的一个 Wine 构建，只下载一次，解压到数据目录的 `wine/` 中，由所有工作区共用：x64 的 Linux 上为 [Kron4ek/Wine-Builds](https://github.com/Kron4ek/Wine-Builds)（`amd64-wow64`），macOS 上为 [Gcenx/macOS_Wine_builds](https://github.com/Gcenx/macOS_Wine_builds)（`wine-devel`），在 arm64 上由 Rosetta 2 运行。lip 从不运行其他的 `wine`，例如 `PATH` 中的。
- **Microsoft Visual C++ 可再发行组件包**（x64）：当注册表中 `HKLM\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64` 下没有值 `Installed` = 1 时，lip 下载 `vc_redist.x64.exe` 并以 `/install /quiet /norestart` 运行它：在 Windows 上以管理员身份运行，可能弹出用户账户控制提示；在其他系统上通过 Wine 在工作区的 Wine 前缀中运行，并用 `wine reg query` 读取该前缀的注册表。

每个下载的 URL 和 SHA-256 都写在 lip 中，使用前会先校验：SHA-256 不符的下载会失败，既不会被解压也不会被运行。下载写入数据目录，用完即删除；Wine 先解压到一个 `.tmp` 目录，再重命名到位，因此不会运行解压到一半的构建。同时进行安装的多个 lip 进程通过数据目录中的 `lock` 文件相互等待。

lip 在会运行钩子命令的操作或构建为 `windows` 的 `lip setup` 开始修改工作区之前安装它们，因此此处的失败会让工作区保持原样；在 `lip run` 启动 Windows 版服务器之前也会这样做。Linux 版服务器两者都不需要。若一切都已安装，这只需查询一次注册表。

## 守护进程

除 [`lip daemon`](../cli/commands/daemon.md) 外，每个命令都由用户的守护进程执行，它是以唯一参数 `--daemon` 启动的 `lip` 进程（[lip-daemon(1)](https://github.com/LiteLDev/lip/blob/main/protocol/lip-daemon.md)）。你启动的 `lip` 是它的客户端：

1.  客户端连接守护进程：在 Windows 上通过只有你的用户能打开的命名管道 `\\.\pipe\lip-<SID>`；在 Linux 和 macOS 上通过 Unix 域套接字 `$XDG_RUNTIME_DIR/lip/daemon.sock`，未设置 `XDG_RUNTIME_DIR` 时为状态目录中的 `daemon.sock`，其模式为 `0600`，所在目录的模式为 `0700`。
2.  没有进程监听时，客户端用自己的可执行文件启动守护进程，使其脱离终端且不带客户端的标准流，并在守护进程开始监听后连接。两个客户端同时启动守护进程时，最终只有一个：第二个守护进程无法创建端点，随即退出。
3.  守护进程属于另一个 lip 版本或运行另一个可执行文件时（例如 [`lip self-update`](../cli/commands/self_update.md) 之后），客户端请求它退出并启动新的守护进程。正在执行命令的守护进程会继续运行；此时客户端自己执行它的命令。
4.  客户端发送命令，以及它的工作目录、环境变量、标准流是否为终端、终端的大小和颜色。守护进程据此执行命令，与 `lip` 自己执行时相同：不同客户端的命令并行执行，各自使用自己的目录、环境、代理和失败报告。
5.  守护进程只在命令需要时读取客户端的标准输入，例如 `lip run` 的服务器的输入，并把命令及其运行的程序输出的内容发回。客户端收到的信号（Ctrl+C、`SIGTERM`、关闭终端）会被转发：Ctrl+C 取消并撤销正在修改工作区的操作，并请求 `lip run` 的服务器停止。服务器运行时客户端退出，服务器会被请求停止。客户端以命令的退出码退出。

守护进程无法启动或无法连接时，客户端会说明这一点并自己执行命令。守护进程在一段时间没有连接后、在 `lip self-update` 替换了 lip 之后，或执行 [`lip daemon stop`](../cli/commands/daemon.md) 时退出。界面（不带参数的 `lip`）在你启动的 `lip` 中运行。

## 关于规范的说明

lip 只管理服务端工作区：它从不从工作区判断运行端，而是总是使用 `server`（[lip(1)](https://github.com/LiteLDev/lip/blob/main/protocol/lip.md#environment) "Environment"）。客户端工作区交由实现同一协议的启动器（如 LeviLauncher）处理。

lip 实现 [lip 协议](https://github.com/LiteLDev/lip/blob/main/protocol/README.md)。
