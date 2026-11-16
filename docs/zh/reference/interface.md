# 界面

本页是 lip 交互界面的简明参考：按键、各个页面及其背后的命令，以及每个页面都遵守的规则。想一步一步学习界面，请从[界面导览](../interface/tour.md)开始。

规范性定义见协议的 [lip-tui(1)](https://github.com/LiteLDev/lip/blob/main/protocol/lip-tui.md) 页。

## 启动

```shell
lip
```

标准输入和标准输出都是终端时，不带参数的 `lip` 会打开界面；否则输出帮助（见[命令行](cli.md)）。

- 界面同一时间只操作一个工作区，即**当前工作区**：一开始是启动 `lip` 时所在的文件夹。
- 界面由 lip 的[守护进程](../concepts/how_lip_works.md)执行。可以同时打开多个界面。
- 终端至少要有 60 列、16 行。更小的终端里，lip 会请你放大终端，这时唯一可用的按键是 `Ctrl+C`，它不经询问直接退出。
- 终端不足 80 列时，焦点在页面上的时候，侧边栏只为每个页面显示一个符号。

## 按键

同样的按键在每个页面上作用相同。屏幕最下面一行列出焦点所在位置可用的按键。

| 按键 | 作用 |
| --- | --- |
| `↑` `↓` | 在列表或菜单里移动。在侧边栏里显示上一个或下一个页面。 |
| `←` `→` | 在侧边栏和页面之间、并排的窗格或按钮之间移动；更改有多个取值的选项。 |
| `Enter` | 打开或执行选中的内容。在列表的某一行上，打开它的操作。 |
| `Space` | 在可以多选的地方勾选一行；打开或关闭一个选项。 |
| `Tab`、`Shift+Tab` | 前往下一个或上一个区域，在侧边栏和页面的各个区域之间循环。 |
| `Esc` | 返回上一层：从对话框回到页面，从页面的内层区域回到前一个区域，从页面回到侧边栏。在侧边栏里询问是否退出。 |
| `PgUp` `PgDn` `Home` `End` | 在列表或文本里翻页。在侧边栏里，`Home` 和 `End` 显示第一个和最后一个页面。 |
| `Ctrl+C` | 在“服务器”页且服务器运行时，停止服务器。正在执行更改时，取消它。其他情况下询问是否退出。 |
| 字母和数字 | 焦点在输入框里时用于输入。在其他地方不起作用：界面没有字母快捷键。 |

## 页面

| 页面 | 作用 | 命令 |
| --- | --- | --- |
| [主页](../interface/home.md) | 显示当前工作区的状态和下一步该做什么。 | |
| [服务器](../interface/server.md) | 启动和停止服务器，显示它的输出，把你输入的内容发给它。 | [`lip run --managed --detach`](commands/run.md)、[`lip config set restart … --workspace`](commands/config.md) |
| [模组](../interface/mods.md) | 列出已安装的包；安装、更新和卸载它们。 | [`lip list`](commands/list.md)、[`lip install`](commands/install.md)、[`lip update`](commands/update.md)、[`lip uninstall`](commands/uninstall.md) |
| [获取模组](../interface/get_mods.md) | 搜索 levimc.org 上的模组，并安装其中某个版本。 | [`lip install`](commands/install.md) |
| [游戏配置](../interface/game_config.md) | 编辑 `server.properties`、`allowlist.json` 和 `permissions.json`。 | 无 |
| [模组配置](../interface/mod_config.md) | 编辑已安装模组的配置文件。 | 无 |
| [工作区](../interface/workspaces.md) | 列出工作区；切换、添加和移除工作区；新建服务器；更改某个服务器的游戏或加载器。 | [`lip setup`](commands/setup.md) |
| [工具](../interface/tools.md) | 创建 `lip.toml`、迁移 `tooth.json`、打包 ZIP、查看包信息。 | [`lip pkg init`](commands/pkg_init.md)、[`lip pkg migrate`](commands/pkg_migrate.md)、[`lip pkg pack`](commands/pkg_pack.md)、[`lip pkg view`](commands/pkg_view.md) |
| [设置](../interface/settings.md) | 语言、GitHub 代理、自动重启、缓存、lip 的更新、守护进程、版本。 | [`lip config`](commands/config.md)、[`lip cache clean`](commands/cache_clean.md)、[`lip self-update`](commands/self_update.md)、[`lip daemon status`](commands/daemon.md)、`lip --version` |

界面执行的命令遵守该命令自己的页面的定义，以当前工作区为工作目录，但有以下不同：

- 命令的输出显示在界面里，是英文。
- 命令要询问的内容在对话框里询问。
- 命令失败时，界面显示原因，然后继续运行。

## 预览与应用

对工作区里的包所做的每项更改都走同一个流程：安装、更新、卸载，以及安装游戏或加载器。

| 步骤 | 发生什么 |
| --- | --- |
| 1. 预览 | lip 以[试运行](cli.md)的方式执行命令，显示它将要做什么。此时什么都没有改变。 |
| 2. **应用**或**取消** | **应用**继续。**取消**不作任何更改。预览发现没有可改的内容时，只有**关闭**一个按钮。 |
| 3. 许可协议 | 仅当这次更改要安装游戏时：lip 显示《Minecraft 最终用户许可协议》和《Microsoft 隐私政策》。**接受**继续；**拒绝**不作任何更改。 |
| 4. 进度 | `Esc` 或 `Ctrl+C` 取消；已经做的部分会被撤销，并有一个标题为**已取消**的对话框说明。这不算失败。 |
| 5. 结果 | 命令的输出，以及 **✓ 完成。** |

- 应用不会从头再来：预览时读取和下载的内容会直接使用。
- 安装服务器的预览里点名了某些已安装的模组、说它们没有适合新游戏或新加载器的版本时，选择**应用**会卸载这些模组。
- 失败时显示原因、lip 给出的提示（如果有），以及失败报告的路径（在工作区的 `.lip/logs` 下）。

## 服务器运行期间

只要 lip 在运行某个工作区的服务器，这个服务器就算在运行，不管是谁启动的：界面、`lip run --managed`，还是终端里普通的 `lip run`。这期间，界面对该工作区的行为如下：

| 内容 | 服务器运行期间 |
| --- | --- |
| 安装、更新和卸载包（[模组](../interface/mods.md)、[获取模组](../interface/get_mods.md)） | 不可用。 |
| 在它的文件夹里新建服务器、更改它的游戏或加载器（[工作区](../interface/workspaces.md)） | 不可用。 |
| `server.properties`、`allowlist.json`、`permissions.json`（[游戏配置](../interface/game_config.md)） | 只显示，不能修改或保存。未保存的更改会保留到服务器停止。 |
| 模组的配置文件（[模组配置](../interface/mod_config.md)） | 照常修改和保存。页面会提示更改可能需要重启。 |
| 其他一切：浏览模组、切换工作区、[工具](../interface/tools.md)、[设置](../interface/settings.md) | 照常。 |

不可用的内容仍然显示在屏幕上，是灰色的，并注明原因：**请先停止服务器。**

以上只针对界面。命令 `lip install`、`lip update`、`lip uninstall` 和 `lip setup` 在服务器运行期间不会被拒绝，见[界面与命令](../concepts/how_lip_works.md#界面与命令)。

退出界面不会停止由守护进程运行的服务器。如果 lip 因为无法使用守护进程而自己执行界面，退出时会停止界面启动的服务器。

## 配置文件

| 文件 | 读取 | 写入 |
| --- | --- | --- |
| `server.properties` | “游戏配置”页显示时。 | **保存**时：只写改过的属性的值，不动其他任何字符。文件不存在时不会创建。 |
| `allowlist.json`、`permissions.json` | 同上。不存在的文件读作空列表。 | **保存**时：整个文件，不含注释。文件不存在时会创建。 |
| 模组的 `.json` 文件 | 在“模组配置”页选中该文件时。 | **保存**时：整个文件，不含注释，每个名称留在原位。文件已不存在时不会创建。 |
| 模组的其他文件 | 同上。 | 从不写入。 |

- lip 读不懂的文件只显示，从不写入。
- 自 lip 读取之后在磁盘上被改动过的文件，只有在你同意时才会被覆盖。
- 带着未保存的更改离开页面时，会询问是否保存。
- 保存的更改在服务器下次启动时生效。

模组的文件指 `mods/<mod>/config/` 里的文件；如果一个都没有，则是直接放在 `mods/<mod>/` 里的 `.json`、`.ini`、`.toml`、`.yml`、`.yaml` 文件（`manifest.json` 除外），以及 `mods/<mod>/data/config.json`。

## 语言

| `language` 的值 | 界面 |
| --- | --- |
| `en` | 英文 |
| `zh-Hans` | 简体中文 |
| `zh-Hant` | 繁体中文 |
| 未设置 | 系统的语言：英文，或按区域设置决定的中文。 |

语言在[设置](../interface/settings.md#语言)页选择，或者用 [`lip config`](commands/config.md) 设置；见[配置](configuration.md)。更改立即生效。命令输出的内容在任何语言下都是英文。

## GitHub 检查

界面启动时，如果 `github_proxy` 没有设置，lip 会在后台检查一次能否连上 GitHub，最多等待 10 秒。连不上时，界面会询问怎样连接：

| 回答 | 作用 |
| --- | --- |
| 使用代理 | 询问代理的网址、进行测试，并把 `github_proxy` 设为该网址。 |
| 直接连接 | 把 `github_proxy` 设为空值：lip 不再检查，也不再询问。 |
| 以后再说 | 不作任何更改；下次界面启动时 lip 会再检查。 |

见[设置](../interface/settings.md#github-代理)。

## 退出状态

退出界面时，`lip` 以代码 0 退出，不管在界面里执行的命令结果如何，也不管服务器以什么代码退出。

## 文件

| 文件 | 界面 |
| --- | --- |
| 状态目录里的 `workspaces.json` | 读写工作区列表。 |
| 配置目录里的 `config.toml` | 读取 `language` 和 `github_proxy`；在“设置”页和 GitHub 检查时写入它们。 |
| 工作区里的 `.lip/config.toml` | 读写 `restart`（自动重启）。 |
| 工作区里的 `server.properties`、`allowlist.json`、`permissions.json` | 读取；**保存**时写入。 |
| `mods/` 下模组的配置文件 | 读取；**保存**时写入其中的 `.json` 文件。 |
| 工作区里的 `.lip/logs/` | 命令失败时写入失败报告。 |
| `https://levimc.org/api/v4/mods/` | 读取模组目录。其内容不会写入磁盘，也不经过 `github_proxy`。 |

界面执行的命令会读写它们各自页面所说的文件。lip 的所有文件和目录见[文件](files.md)。

## 另请参阅

- [界面导览](../interface/tour.md)
- [开你的第一个服务器](https://levimc.org/tutorials/zh-Hans/server/first-server/)
- [命令行](cli.md)
- [配置](configuration.md)
