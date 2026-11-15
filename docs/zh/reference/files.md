# 文件

本页列出 lip 读写的每一个文件和目录，让你知道哪些可以删除、备份或编辑，以及这样做会失去什么。

lip 只向四个地方写入：它操作的工作区、它的用户级目录、你在参数中给出的路径，以及更新自身时的安装目录。它从不向系统临时目录写入。它运行的程序（服务器、钩子命令、安装程序和 Wine）写到哪里不受此限。

## 工作区

**工作区**是一个服务器所在的目录。它是命令的当前目录，或者界面中选中的工作区。

| 路径 | 是什么 | 由谁写入 |
| --- | --- | --- |
| `.lip/` | lip 关于这个工作区的记录。参见 [.lip 目录](#lip-目录)。 | lip |
| `mods/` | LeviLamina 加载模组的目录。每个模组位于 `mods/<模组名>/`。 | lip，以及模组自己 |
| `bedrock_server_mod.exe`、`bedrock_server.exe`、`bedrock_server` | [`lip run`](commands/run.md) 启动的服务器。参见[服务器文件](#服务器文件)。 | lip |
| 其余的一切 | 游戏和文件包的文件，以及服务器运行时创建的内容，例如 `worlds/`。 | lip 和服务器 |
| `tooth_lock.json` | lip v0.x 工作区的状态。参见[状态文件](../concepts/state_file.md#lip-v0-x-的工作区)。 | lip v0.x |

在操作更改工作区之前，lip 会创建 `mods/`。`mods` 必须是真正的目录：如果它是链接，操作失败。

工作区不依赖它之外的任何东西。只要没有 lip 命令和服务器在使用它，你可以移动、重命名或复制这个目录，已安装的包都会保留。

### .lip 目录

在 Windows 上，`.lip` 带有隐藏属性。

| 条目 | 是什么 | 删除的后果 |
| --- | --- | --- |
| `state.json` | [状态文件](../concepts/state_file.md)：已安装的包，以及每个包拥有的文件。 | lip 不再知道安装了什么。文件还在，但 lip 无法卸载或更新它们。不要删除。 |
| `state.json.tmp` | 正在写入的状态文件。 | 命令运行期间不要动它。 |
| `journal` | 进行中的操作的记录，lip 靠它完成或撤销被中断的操作。只在操作进行期间或操作被中断之后存在。 | lip 无法再撤销被中断的操作。不要删除；运行 `install`、`uninstall`、`update`、`setup` 或 `run` 即可恢复。 |
| `lock` | **工作区锁**的文件。进程在执行操作期间持有它，这样两个操作不会同时更改工作区。它的内容没有意义。 | 没有命令运行时删除无妨。有命令运行时，绝不要删除、重命名或替换它。 |
| `server.lock` | **服务器锁**的文件。运行服务器的进程持有它，这样一个工作区最多只有一个服务器在运行。 | 没有服务器运行时删除无妨。有服务器运行时，绝不要删除、重命名或替换它。 |
| `config.toml` | [工作区配置](configuration.md#工作区配置)。 | `restart` 回到 `never`。 |
| `logs/` | [失败报告](#失败报告)。 | 失去这些报告。随时可以删除。 |
| `wine/` | 在 Linux 和 macOS 上：Windows 版服务器和钩子命令运行所用的 Wine 前缀。 | Wine 会重新创建它，lip 会重新向其中安装 Visual C++ 运行库。请先停止服务器。 |

lip 可能在 `.lip` 中保存其他条目。不要依赖它们。

`.lip` 是保留目录：包不能向其中安装文件，`purge` 的 glob 也永远不会匹配它。

两把锁互相独立。操作不取得服务器锁，服务器运行期间也不持有工作区锁，所以 `lip run` 运行时仍然可以执行 `lip install`。界面更严格，会要求你先停止服务器。

### 服务器文件

游戏就是包 `github.com/LiteLDev/bds`。安装它时，lip 把从 `www.minecraft.net` 下载的 Bedrock Dedicated Server 归档的全部内容写到工作区根目录。

| 工作区有 | 服务器文件 |
| --- | --- |
| 加载器（LeviLamina） | `bedrock_server_mod.exe` |
| 没有加载器，构建为 `windows` | `bedrock_server.exe` |
| 没有加载器，构建为 `linux` | `bedrock_server` |

在 Linux 和 macOS 上，lip 写入 `bedrock_server` 时把它的权限设为 `0755`。

安装、替换或卸载游戏时，两个列表决定它的文件如何处理：

| 列表 | 路径 | 作用 |
| --- | --- | --- |
| 保留 | `server.properties`、`allowlist.json`、`permissions.json` | 安装游戏时保留已存在的文件，卸载游戏时也不删除。 |
| 清除 | `bedrock_server.exe.bak`（仅构建 `windows`）、`packet-statistics.txt`、`valid_known_packs.json`、`behavior_packs/`、`config/`、`definitions/`、`development_behavior_packs/`、`development_resource_packs/`、`development_skin_packs/`、`resource_packs/`、`world_templates/` | 卸载游戏时连同其下的所有内容一起删除，包括不是游戏安装的文件。其他已安装的包拥有的路径会保留。 |

### 更换游戏后保留什么

用 [`lip setup`](commands/setup.md) 更换游戏版本时，会卸载旧游戏并安装新游戏：

| 保留 | 被替换或删除 |
| --- | --- |
| `server.properties`、`allowlist.json`、`permissions.json` | 旧游戏安装的其他所有文件 |
| `worlds/`，以及服务器或你在清除路径之外创建的其他文件 | 上面的清除路径，连同你放进去的内容 |
| `mods/`，以及仍然安装着的包的文件 | 这次设置卸载的包的文件 |
| `.lip/` | |

::: warning
你复制到 `behavior_packs/`、`resource_packs/` 或某个 `development_*` 目录中的包会随这些目录一起被删除。更换游戏之前，请在工作区之外留一份副本。
:::

### 文件名

如果路径中某一段包含 `:`，或者以 `.` 或空格结尾，lip 拒绝把这个文件写入工作区，因为 Windows 无法保存这样的名字。操作失败。

在 Linux 和 macOS 上，lip 像 Wine 一样把工作区中的路径视为不区分大小写：`Mods/a.dll` 和 `mods/A.DLL` 是同一个文件。如果已有一个只是大小写不同的文件，新文件会写到它的位置上。如果一个路径这样对应了不止一个已有的文件，操作失败。

## 用户级目录

| 目录 | Windows | Linux 和 macOS | Linux 和 macOS 上的默认值 |
| --- | --- | --- | --- |
| 配置 | `%APPDATA%\lip` | `$XDG_CONFIG_HOME/lip` | `~/.config/lip` |
| 缓存 | `%LOCALAPPDATA%\lip\Cache` | `$XDG_CACHE_HOME/lip` | `~/.cache/lip` |
| 数据 | `%LOCALAPPDATA%\lip\Data` | `$XDG_DATA_HOME/lip` | `~/.local/share/lip` |
| 状态 | `%LOCALAPPDATA%\lip\State` | `$XDG_STATE_HOME/lip` | `~/.local/state/lip` |

未设置、为空或不是绝对路径的 `XDG_*` 变量取其默认值。

### 配置目录

| 条目 | 是什么 |
| --- | --- |
| `config.toml` | [用户配置](configuration.md#用户配置)。 |
| `liprc.json` | lip v0.x 的配置。lip 会把它[转换](configuration.md#liprc-json)为 `config.toml` 并删除它。 |

删除配置目录会重置 `github_proxy` 和 `language`。

### 缓存目录

缓存里是 lip 下载过、并且可以重新下载的内容。[`lip cache clean`](commands/cache_clean.md) 删除整个目录。

| 条目 | 是什么 |
| --- | --- |
| `http-<H>` | 一个文件：对某个 URL 的一次成功的 HTTPS GET 的响应体。`<H>` 是该 URL 的小写十六进制 SHA-256；URL 取包中写的样子，去掉片段，与使用了什么代理无关。 |
| `<name>.tmp` | 正在写入的文件。 |
| `self-update/` | [`lip self-update`](commands/self_update.md) 下载和解压的内容，只在它运行期间存在。 |

lip 可能在这里保存其他条目。不要依赖它们。

其他程序只要遵守同样的规则，也可以读写缓存：

- **写入。** 先把条目写到一个其他进程不会使用的 `.tmp` 文件名下，再重命名。绝不原地修改条目。
- **读取。** 存在的条目就是完整的。知道某个 `http-<H>` 条目应有内容的读取者要检查它，不一致时可以删除它。
- **删除。** `http-<H>` 条目随时可以删除；`.tmp` 文件在没有进程写它时可以删除；`self-update` 在没有 `lip self-update` 运行时可以删除。

当 lip 需要一个 ZIP 归档，而其 URL 对应的缓存条目不是 ZIP 归档时，lip 会删除该条目并重新下载一次。

删除缓存不会失去任何东西：lip 会重新下载它需要的内容。你删除缓存时，正在向缓存写入的 lip 命令可能失败。

### 数据目录

数据目录保存 lip 在 Linux 和 macOS 上安装的 Wine（所有工作区共用），以及 lip 为安装系统依赖而下载的内容。

在没有 lip 进程运行时删除它，不会失去任何东西：lip 在需要时会重新安装系统依赖。在 Linux 和 macOS 上，这意味着重新下载 Wine。

### 状态目录

| 条目 | 是什么 |
| --- | --- |
| `workspaces.json` | 界面的[工作区列表](#workspaces-json)。 |
| `daemon.sock` 和守护进程的其他文件 | 在 Linux 和 macOS 上，仅当 `XDG_RUNTIME_DIR` 没有设为绝对路径时。参见[守护进程端点](#守护进程端点)。 |

在没有 lip 进程运行时删除它，只会失去工作区列表。工作区本身不受影响；在界面中重新添加即可。

### workspaces.json

界面在[工作区](../interface/workspaces.md)页显示的工作区列表：一个由绝对路径组成的 JSON 数组，按添加顺序排列。

```json
["D:\\servers\\survival", "D:\\servers\\creative"]
```

- 列表不存在或不是 JSON 数组时，视为空列表。
- 不是绝对路径的元素，以及与列表中已有元素指向同一目录的元素，会被略去。
- lip 以规范形式写入每个路径：解析掉 `.` 和 `..`，在 Windows 上以 `\` 作分隔符，末尾不带分隔符（根目录除外）。两个路径的规范形式相等时指向同一目录，在 Windows 上不区分大小写。

界面启动时会把它的工作目录加入列表。从列表中移除工作区不会更改其中的任何文件。命令不使用这个列表。

## 守护进程端点

[守护进程](commands/daemon.md)在只有你这个用户能访问的地方监听：

| 系统 | 端点 |
| --- | --- |
| Windows | 命名管道 `\\.\pipe\lip-<SID>`，`<SID>` 是你这个用户的安全标识符。 |
| Linux 和 macOS | **守护进程目录**中的 Unix 套接字 `daemon.sock`：`XDG_RUNTIME_DIR` 是绝对路径时为 `$XDG_RUNTIME_DIR/lip`，否则为状态目录。 |

守护进程目录的权限是 `0700`，套接字的权限是 `0600`。守护进程退出时删除 `daemon.sock`。该目录中还有守护进程的其他文件；不要依赖它们。

## 安装目录

安装目录是 `lip` 可执行文件所在的目录。

| 条目 | 是什么 |
| --- | --- |
| `install.json` | `{"kind": "msi"}`。由 Windows 安装程序写入；它告诉 [`lip self-update`](commands/self_update.md) 用安装程序来更新。没有它时，lip 视为便携版。lip 从不写它。 |
| `<file>.old` | 便携版更新替换掉、但暂时无法删除的文件。lip 下次启动时删除 `<executable>.old`。 |

## 失败报告

因用法错误以外的原因失败的命令，可能会向工作区的 `.lip/logs/` 写一份**失败报告**，并在消息中给出文件名：

```
Full report: .lip\logs\20260115T093012Z-4812.log
```

文件名由 UTC 时间和一个进程 ID 组成。在当前版本中，报告是一个文本文件，包含命令行、lip 和系统的版本、lip 使用的目录、错误及其提示和原因、`.lip` 中的条目，以及 lip 所做事情的跟踪记录。URL 中的用户名、密码和查询参数的值会被替换成 `***`。lip 保留最新的 20 份报告，删除更早的。

求助时请附上这份报告。它的格式并不固定，可能会变化。

失败的[试运行](cli.md#试运行)也可能写一份；这是试运行唯一会写的文件。

## 另请参阅

- [lip 的工作方式](../concepts/how_lip_works.md)：对操作、日志和锁的解释。
- [状态文件](../concepts/state_file.md)
- [故障排除](../guides/troubleshooting.md)
- 界面中的[工作区](../interface/workspaces.md)页。
