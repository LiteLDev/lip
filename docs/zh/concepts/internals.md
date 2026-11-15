# 内部机制

读完本页，你就知道 lip 如何查找包的版本、如何不借助 `git` 与 git 仓库通信、缓存了什么、如何在版本之间做选择，以及如何为服务器准备系统。本页写给好奇的人和包的作者。[lip 的工作原理](how_lip_works.md)给出了总览，确切的定义则在 [lip 协议](https://github.com/LiteLDev/lip/blob/main/protocol/README.md)里。

## 一次操作的步骤

`lip setup`、`lip install`、`lip uninstall` 和 `lip update` 执行的是同样的步骤：

1. **加锁并恢复。** 占用工作区锁 `.lip/lock`。把被中断的操作做完或撤销。
2. **状态文件。** 从 `.lip/state.json` 读取已安装的包。没有这个文件的工作区就没有包，除非它有 lip v0.x 的 `tooth_lock.json`，这时改为读取后者。
3. **环境。** 在每种系统上，包都是按运行端 `server` 和平台 `win-x64` 来选择的。
4. **解析。** 把每个来源变成一串候选：它指向的各个版本，最新的在前，每个版本带上所请求变体在该环境下的条目。
5. **求解。** 为每个请求和每个依赖选定一个候选，使每个包只出现一次，并且没有两个模组同名。
6. **计划。** 计划卸载离开集合的包（按安装顺序的逆序），再安装进入集合的包（依赖在前）。做计划时会下载所有将要写入的归档和资源，并检查一切可能失败的地方：每个文件归谁所有、`preserve`、路径、每条放置规则的 `src`、每个模组在其 `manifest.json` 里的名字，以及 `mods` 是不是链接。
7. **准备系统**，如果要运行 Windows 程序。见[系统依赖](#系统依赖)。
8. **应用。** `mods` 不存在就创建它，然后执行计划好的钩子命令和文件更改，每一项都先记入日志。
9. **状态文件。** 每一步都成功之后，保存一次状态文件。

第 8 步之前的失败不会动工作区。第 8 步当中的失败，或者 Ctrl+C，会让 lip 撤销这次操作的每一处更改。钩子命令做过的事不会被撤销。

试运行在第 6 步之后停下，第 1 步里不加锁，也不做任何修复。

### 同一时间只有一次操作

一次操作会独占地打开 `.lip/lock`：在 Windows 上是不共享地打开，在 Linux 和 macOS 上是独占的 `flock`。这个文件会一直保留，从不删除。对同一工作区的第二次操作不会等待，而是立刻失败，并给出：

```text
lip cannot take the workspace lock '<workspace>/.lip/lock': most likely another lip command is changing the workspace '<workspace>'. Wait for the other lip command to finish, then try again. If no other lip command is running, a program may be holding the lock file open: close it, then try again.
```

`lip run` 只在启动服务器之前、恢复被中断操作的那一刻占用同一把锁。服务器运行期间，它以同样的方式占用的是服务器锁 `.lip/server.lock`。同一工作区的第二个服务器会失败，并给出：

```text
A server of the workspace '<workspace>' is already running: another lip run, the interface of lip or the daemon of lip runs it, and holds the server lock '<workspace>/.lip/server.lock'. Stop that server first, or attach to it with 'lip run --managed' when it is a managed server.
```

这两把锁互不相干。进程被杀掉时，锁会随它的文件句柄一起释放，所以绝不会有锁遗留下来。

## 包的解析

lip 没有中央仓库，也没有模块代理。像 `github.com/user/repo` 这样的包 ID，就是 git 仓库 `https://github.com/user/repo.git`。

- **版本。** 仓库的版本，是它名字恰好为 `v` 加 [SemVer 2.0.0](https://semver.org/) 版本号的标签，例如 `v1.2.3`。其他标签会被忽略，包括 `v1.2` 或 `v01.2.3` 这样不严格的写法。附注标签按它所指的提交计算。
- **引用。** 带引用的来源只有一个候选。`@branch:<name>` 和 `@tag:<name>` 每条命令查询一次。`@rev:<commit>` 不需要查询：直接按提交 ID 获取。缩写的提交 ID 会被拒绝，因为解析它需要完整的历史，而 lip 从不下载完整历史。
- **惰性候选。** 仓库的候选按从新到旧的顺序、只在需要时才产生。lip 只会打开求解过程真正用到的版本。
- **怎样才算候选。** 一个版本要成为候选，它的 `lip.toml` 必须有效，其中的 `id` 必须是仓库的路径，`version` 必须等于标签的版本，并且所请求的变体在该环境下有条目。否则 lip 继续看下一个版本，并记下原因，以便在没有任何版本合适时告诉你。
- **没有标签版本时。** 在分支、提交，或者不是版本的标签上，包的版本就是它 `lip.toml` 里的 `version`。
- **游戏**是例外。`github.com/LiteLDev/bds` 不是仓库。lip 根据 `https://levimc.org/api/v4/mc/versions/` 上的服务器构建列表自己生成它的包，并从 minecraft.net 下载服务器。
- **其他来源。** URL 指向一个通过 HTTPS 下载的 ZIP 压缩包。本地路径指向一个 ZIP 文件或一个文件夹。它们各有一个候选。

一个版本只有在它的标签还存在于仓库里时才能安装。只支持能通过 HTTPS 访问的公开仓库。比较包 ID 时不区分 ASCII 字母的大小写。

## git 客户端

lip 用自己的客户端与 git 仓库通信，使用的是 git 智能 HTTP 协议的第 2 版。**不需要 `git` 程序，lip 也从不运行它。**

- **列出标签。** lip 用 `ls-refs` 向服务器查询引用，范围限定在 `refs/tags/v`。对于分支或标签引用，它只查询 `refs/heads/<name>` 或 `refs/tags/<name>` 本身。
- **读取 `lip.toml`。** lip 只获取那一个提交的树：一次浅的、部分的获取（`deepen 1`、`filter blob:none`），不传输任何文件内容。然后它只获取那一个文件；没有 `lip.toml` 时获取 `tooth.json`。因此，无论仓库多大，查看一个版本只需要几千字节。
- **安装。** 文件内容只在安装包时才获取，而且只获取这个包从自己的归档里放置的那些文件。
- **完整性。** 每个 git 对象都会对照它的对象 ID 进行校验。
- **代理。** `github_proxy` 设为 URL 后，对 `github.com` 仓库的请求先走代理，失败后再直接发往 `github.com`。从 `github.com` 的 URL 下载的文件遵循同样的规则。见 [GitHub 很慢或被屏蔽时](../guides/configuration.md#github-很慢或被屏蔽时)。

服务器必须像 GitHub 那样支持这些特性。只转发文件下载的代理无法提供仓库访问。

## 缓存

缓存目录存放 lip 通过 HTTPS 下载的东西：URL 来源指向的 ZIP 压缩包、包的资源，以及游戏。

- **条目。** 每个下载保存为 `http-<hash>`，其中的哈希是它的 URL 的 SHA-256。这个 URL 是包里写的那个，与使用了什么代理无关。
- **写入。** 下载先写到缓存目录里的 `.tmp` 文件，完成后再重命名到位。所以存在的条目一定是完整的，多个 lip 进程可以共用缓存。
- **修复。** lip 需要一个 ZIP 压缩包而缓存里的文件不是时，它会删除这个条目并重新下载，只做一次。
- **不缓存的东西。** git 仓库的任何内容都不缓存到磁盘。标签列表和树在一条命令执行期间保存在内存里，下一条命令会重新查询，所以被移动的标签立刻生效。
- **不校验内容。** ZIP 压缩包和资源只检查是不是有效的 ZIP 压缩包。不记录也不比较其内容的校验和。

`lip cache clean` 会删除整个目录。试运行会下载它需要检查的东西，但不往缓存里写任何东西。lip 从不写入系统的临时目录。

## 求解

求解就是为每个请求和每个依赖选定要安装的候选。

- **固定。** 已安装的包按原样进入求解，不联网，直接从状态文件重建。如果这样无法满足请求，lip 会重新求解，这次只固定由你安装的包，依赖的版本可以变动。依赖已安装的版本会被优先尝试，所以只有必须变动的才会变动。
- **更新。** `lip update` 从一开始就按第二种方式求解，这样已安装的依赖绝不会拖住你要更新的包，使它得不到最新版本。
- **偏好。** 靠前的候选优先：也就是仓库的最新版本。
- **游戏和加载器不动。** `lip install` 和 `lip update` 围绕它们求解。找不到合适的组合时，失败信息会给出它们的版本，并提示 `lip setup`。安装服务器时则按新的游戏和加载器求解，只有当某个模组没有任何版本与之相配时才把它排除在外。
- **顺序。** 包按依赖在前的顺序安装。循环依赖里的包按标识排序。
- **清理。** 不再被任何由你安装的包需要的依赖会被卸载。
- **同一个包。** 只有当 lip 解析出的包在标识、版本或提交上与已安装的不同时，才会替换它。来自 URL 或本地路径的包没有提交。
- **根据状态文件重新安装。** lip 再次需要某个已安装包的文件时，会使用状态文件里记录的包文件来安装，而不是重新解析：对仓库来说，从记录的提交安装；否则从其来源现在指向的归档安装。

## 包文件的校验

`lip.toml` 由 [`lip.schema.toml`](https://github.com/LiteLDev/lip/blob/main/protocol/lip.schema.toml) 描述，用户设置由 [`config.schema.toml`](https://github.com/LiteLDev/lip/blob/main/protocol/config.schema.toml) 描述，工作区设置由 [`workspace-config.schema.toml`](https://github.com/LiteLDev/lip/blob/main/protocol/workspace-config.schema.toml) 描述，状态文件由 [`state.schema.json`](https://github.com/LiteLDev/lip/blob/main/protocol/state.schema.json) 描述。

- **在 schema 之外**，lip 还会检查 [lip.toml(5)](https://github.com/LiteLDev/lip/blob/main/protocol/lip.toml.md) 的要求：有效的包 ID 和版本，有效的依赖键和范围，运行端和平台，同一变体里没有两个条目针对相同的运行端和平台，每个条目最多一个 `mcmod` 资源，以及有效的 glob。`lip.toml` 无效的归档不是包。它不产生候选，lip 会报告原因。
- **lip v0.x 的文件。** 没有 `lip.toml` 的归档会从它格式 1、2 或 3 的 `tooth.json` 读取，解析时就把它完整地转换成包文件。因此所有的包都由同一个安装器处理。位于版本标签上的 `tooth.json` 按标签的版本安装，不管它自己的 `version` 写的是什么。`tooth_lock.json` 会变成 `.lip/state.json`，`liprc.json` 会变成 `config.toml`，都在 lip 第一次处理它们时自动完成。见 [lip-compatibility(7)](https://github.com/LiteLDev/lip/blob/main/protocol/lip-compatibility.md)。

包的作者可以在 [lip.toml](lip_toml.md) 一页找到格式说明。

## 工作区状态和日志

[状态文件](state_file.md) `.lip/state.json` 为每个已安装的包记录：是否由你安装、安装时使用的包文件、来源、来自仓库时的提交，以及它拥有的文件。

操作之所以能“全有或全无”，靠的是三类文件：

- **日志** `.lip/journal`。每次更改在执行之前都先记在这里，并刷写到磁盘。其中的路径是相对于工作区的，所以被移动或复制的工作区会带着被中断的操作一起走。
- **备份。** 操作要替换或删除的文件，会先在同一目录里重命名为 `.<名字>.bak`；这个名字已被占用时，依次用 `.<名字>.bak1`、`.<名字>.bak2`，以此类推。在 Windows 上它还会带上隐藏属性。
- **暂存的状态文件。** 新的状态先写到 `.lip/state.json.tmp`，再重命名覆盖 `.lip/state.json`。

操作成功时，lip 把日志标记为已提交，把状态文件重命名到位，删除备份和被操作清空的目录，再删除日志。因为被某个程序打开而删不掉的备份会被留下，并在警告里指出。

操作失败或被取消时，lip 倒着走一遍日志：删除它创建的东西，把每个备份重命名回去。

lip 被杀掉时，日志会留下。下一次操作，或者下一次 `lip run`，会在占用工作区锁之后读取它。带提交标记的日志会被做完。不带的会被撤销。如果有些东西暂时还撤销不了，日志会被改写成只含剩余的部分，命令会一直失败，直到能撤销为止。

这些格式在 [journal(5)](https://github.com/LiteLDev/lip/blob/main/protocol/journal.md) 和 [lip-workspace(7)](https://github.com/LiteLDev/lip/blob/main/protocol/lip-workspace.md) 里固定下来，以便其他程序也能参与。

## 系统依赖

工作区里的程序，也就是包的钩子命令和 Windows 服务器，需要系统提供两样东西。两样都由 lip 自己安装。

- **Wine**，在 Linux 和 macOS 上。每个版本的 lip 都指定了一个用于 x64 Linux 的 Wine 构建（来自 [Kron4ek/Wine-Builds](https://github.com/Kron4ek/Wine-Builds)）和一个用于 macOS 的构建（来自 [Gcenx/macOS_Wine_builds](https://github.com/Gcenx/macOS_Wine_builds)），后者在 arm64 上由 Rosetta 2 运行。lip 只下载一次，解压到数据目录的 `wine/` 里，所有工作区共用。lip 从不运行别的 `wine`。
- **Microsoft Visual C++ Redistributable**（x64）。注册表的 `HKLM\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64` 下有值 `Installed` = 1 时，视为已安装。否则 lip 会下载 `vc_redist.x64.exe`，并以 `/install /quiet /norestart` 运行它：在 Windows 上以管理员身份运行，可能会弹出用户帐户控制提示；在其他系统上通过 Wine、在工作区的 Wine 前缀里运行，前缀的注册表用 `wine reg query` 读取。

lip 这样保证安全：

- **固定的下载。** 每个文件的 URL 和 SHA-256 都写在 lip 里。SHA-256 不一致的下载会被删除，既不解压也不运行。
- **绝不半途而废。** Wine 先解压到 `.tmp` 目录，再重命名到位。被中途打断的构建绝不会被运行。
- **一次一个。** 同时进行安装的多个 lip 进程，通过数据目录里的 `lock` 文件互相等待。
- **在更改工作区之前。** lip 在会运行钩子命令的操作开始更改工作区之前、在 `lip setup` 安装 Windows 构建期间，以及在 `lip run` 启动 Windows 服务器之前准备系统。在这一步失败，工作区保持原样。

Linux 服务器两样都不需要。一切都已安装时，准备系统只需要查一次注册表。

通过 Wine 时，lip 以 `wine cmd /s /c <command>` 运行钩子命令，以 `wine` 加服务器文件的绝对路径运行服务器，并把 `WINEPREFIX` 设为工作区里的 `.lip/wine`、把 `WINEDEBUG` 设为 `-all`，除非你自己设置了它们。见 [Linux 与 macOS](../guides/linux_macos.md)。

## 守护进程

除了 `lip daemon` 的命令之外，每条命令以及界面，都由用户的守护进程执行：一个只带 `--daemon` 这一个参数启动的 `lip` 进程。你启动的 `lip` 是它的客户端。

1. **连接。** 在 Windows 上通过命名管道 `\\.\pipe\lip-<SID>`，只有你的用户能打开它。在 Linux 和 macOS 上通过套接字 `$XDG_RUNTIME_DIR/lip/daemon.sock`；`XDG_RUNTIME_DIR` 未设置时，是状态目录里的 `daemon.sock`。套接字的权限是 `0600`，所在目录的权限是 `0700`。
2. **启动。** 没有进程在监听时，客户端用自己的可执行文件启动守护进程（脱离终端），等它开始监听后再连接。两个客户端同时这样做，最后也只有一个守护进程：第二个创建不了端点，会自己退出。
3. **替换。** 守护进程是另一个版本或另一个可执行文件时（例如 `lip self-update` 之后），客户端会请它退出，并启动一个新的。正在执行命令或运行托管服务器的守护进程会留下。这时客户端自己执行命令，只有 `lip run --managed` 例外，它由旧的守护进程执行。
4. **发送。** 客户端发送命令，连同它的文件夹、环境变量、标准流是不是终端，以及终端的大小和颜色。守护进程据此执行命令。不同客户端的命令并行执行，每条都有自己的文件夹、环境、代理和失败报告。
5. **转发。** 守护进程只在命令需要时才读取客户端的输入，并把命令及其程序的输出发回去。客户端收到的信号会被传过去。客户端以命令的退出码退出。

界面也是这样执行的。守护进程负责绘制，客户端把绘制的内容写到终端上，并把你按的键和终端的大小传过去。

守护进程运行的程序，包括服务器，都不在你终端的会话里。它们的输出通过管道而不是终端到达 lip。

守护进程在没有连接、也没有托管服务器持续 10 分钟后退出，在 `lip self-update` 替换了 lip 之后退出，或者在 `lip daemon stop` 时退出。确切的规则见 [lip-daemon(1)](https://github.com/LiteLDev/lip/blob/main/protocol/lip-daemon.md)。

## 关于规范的说明

lip 只管理服务器工作区：运行端始终是 `server`。客户端工作区留给实现同一协议的启动器，例如 LeviLauncher。

lip 实现的是 [lip 协议](https://github.com/LiteLDev/lip/blob/main/protocol/README.md)。本页与协议不一致时，以协议为准。
