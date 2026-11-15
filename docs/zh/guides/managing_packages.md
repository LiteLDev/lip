# 管理包

读完本页，你就能用 `lip` 命令安装、列出、更新和卸载包，用 lip 认识的每一种写法指定一个包，并在命令真正动手之前先看看它会做什么。在界面里，同样的事在[模组](../interface/mods.md)和[获取模组](../interface/get_mods.md)页面完成。

## 什么是包

**包**是 lip 往工作区里安装的任何东西：模组、加载器、游戏，或者别的包需要的一组文件。本页的每条命令都要在服务器的文件夹（工作区）里运行。

lip 把已安装的包分成两组：

- **由你安装的包。** 你在 `lip setup` 或 `lip install` 里点名的包。在你卸载之前，lip 会一直保留它们。
- **依赖。** 因为别的包需要而被 lip 装上的包。没有包再需要它们时，lip 会自动移除。

模组需要服务器。请先安装一个：见[用命令管理服务器](server_from_commands.md)。

## 指定一个包

包是用它的出处来指定的，也就是它的**来源**。来源有四种：

| 来源 | 例子 |
| --- | --- |
| git 仓库 | `github.com/LiteLDev/LegacyScriptEngine` |
| 网上的 ZIP 文件 | `https://example.com/downloads/my-mod.zip` |
| 磁盘上的 ZIP 文件 | `./my-mod.zip` |
| 磁盘上的文件夹 | `../MyMod` |

### git 仓库

大多数包都放在 git 仓库里，多数在 GitHub 上。用仓库地址去掉 `https://` 来指定它：

```sh
lip install github.com/LiteLDev/LegacyScriptEngine#lua
```

地址后面还可以跟三样东西：

- **简称。** 不含 `/` 的名字表示 `github.com/LiteLDev/` 下的仓库。`LeviLamina` 就是 `github.com/LiteLDev/LeviLamina`。
- **变体**，写在 `#` 后面。有些包有几种不同的版本形式，并给每一种起了名字。`LegacyScriptEngine#lua` 是 LegacyScriptEngine 的 Lua 变体。包的页面会说明它有哪些变体。
- **引用**，写在 `@` 后面。它决定安装仓库的哪个状态：

| 引用 | 安装的是 | 例子 |
| --- | --- | --- |
| 不写 | 能在你的工作区里使用的最新正式版本 | `lip install LegacyScriptEngine#lua` |
| `@<version>` | 恰好那个版本 | `lip install LegacyScriptEngine#lua@0.13.1` |
| `@branch:<name>` | 某个分支的最新提交 | `lip install github.com/user/repo@branch:main` |
| `@tag:<name>` | 任意标签所指的提交 | `lip install github.com/user/repo@tag:nightly` |
| `@rev:<commit>` | 某一个提交，用完整的 40 位 ID 指定 | `lip install github.com/user/repo@rev:0123456789abcdef0123456789abcdef01234567` |

仓库的正式版本，是它名为 `v` 加版本号的那些标签，例如 `v1.2.3`。写版本时不带 `v`。

只支持公开仓库。lip 不需要安装 `git`。

### 网上的 ZIP 文件

给出以 `https://` 开头的完整地址。文件必须是根目录下有 `lip.toml` 的 ZIP 压缩包：

```sh
lip install https://example.com/downloads/my-mod.zip
```

以 `http://` 或其他协议开头的地址会被拒绝。

### 磁盘上的 ZIP 文件或文件夹

给出根目录下有 `lip.toml` 的 ZIP 文件或文件夹的路径：

::: code-group

```powershell [Windows]
lip install .\my-mod.zip
lip install D:\downloads\my-mod.zip
lip install ..\MyMod
```

```sh [Linux / macOS]
lip install ./my-mod.zip
lip install ~/downloads/my-mod.zip
lip install ../MyMod
```

:::

想确保 lip 把一个名字当作路径而不是仓库，请以 `./` 或 `../`（Windows 上是 `.\` 或 `..\`）开头，或者写绝对路径。URL 和路径后面同样可以跟变体，例如 `./my-mod.zip#lua`。

确切的规则见[命令行](../reference/cli.md)。

## 安装

[`lip install`](../reference/commands/install.md) 接受一个或多个包：

```sh
lip install LegacyScriptEngine#lua LegacyMoney
```

lip 会找出一组彼此兼容、也与你的游戏和加载器兼容的版本，下载它们，并连同依赖一起安装。最后输出：

```text
✓ Packages installed successfully.
```

需要知道的几点：

- **模组需要游戏。** 在没有游戏的工作区里安装模组会失败，并提示 `lip setup`。
- **重复安装已由你安装的包会失败。** lip 会提示改用 `lip update`，更换版本用的是那条命令。
- **依赖可以变成由你安装的包。** 点名安装它即可。lip 会记下你想要它，这样即使没有别的包需要，它也会留下。
- **游戏和加载器保持不变。** 如果某个包需要的游戏或加载器与工作区现有的不同，命令会失败，并提示 `lip setup`。例外：`lip install LeviLamina`，或者点名游戏 `github.com/LiteLDev/bds` 的安装，本身就是一次服务器安装。见[用命令管理服务器](server_from_commands.md#安装服务器)。

## 列出

[`lip list`](../reference/commands/list.md) 按这两组显示已安装的包：

```sh
lip list
```

```text
User-Requested
├── github.com/LiteLDev/bds@<version>
├── github.com/LiteLDev/LeviLamina@<version>
└── github.com/LiteLDev/LegacyScriptEngine#lua@<version>
Dependencies
├── github.com/LiteLDev/LegacyRemoteCall@<version>
└── …
```

每一行是一个包的名字，`#` 后面是变体，`@` 后面是版本。游戏是 `github.com/LiteLDev/bds`。

给脚本用时，可以要求输出 JSON：

```sh
lip list --json
```

`lip list` 只读不写。别的命令正在更改工作区时、服务器运行时，它都能用。

## 更新

不带名字的 [`lip update`](../reference/commands/update.md) 会更新由你安装的每个包：

```sh
lip update
```

带上名字，就只更新这些包：

```sh
lip update LegacyScriptEngine#lua
```

“更新”的含义取决于包当初是怎么安装的：

| 安装自 | 更新后得到 |
| --- | --- |
| 仓库，没有引用 | 能在你的工作区里使用的最新正式版本 |
| 仓库 `@<version>` | 同一个版本。这个包被固定了。 |
| 仓库 `@rev:<commit>` | 同一个提交。这个包被固定了。 |
| 仓库 `@branch:<name>` | 分支现在所指的提交 |
| 仓库 `@tag:<name>` | 标签现在所指的提交 |
| URL | 重新下载的文件 |
| 磁盘上的路径 | 重新读取的文件或文件夹 |

只有当 lip 找到的东西与已安装的不同时，才会替换这个包。否则不动它。

还需要知道：

- **更换或固定版本**：点名版本即可，例如 `lip update LegacyScriptEngine#lua@0.13.1`。这样也能回到旧版本。之后这个包就停在该版本上。
- **取消固定**：点名这个包但不写引用，例如 `lip update LegacyScriptEngine#lua`。它会重新跟随正式版本。
- **依赖只做必要的变动。** 除非由你安装的某个包需要，否则 lip 不会把依赖升到更新的版本。
- **`lip update` 从不更新游戏和加载器。** 点名它们会失败。请用 `lip setup` 更改：见[更改游戏版本](server_from_commands.md#更改游戏版本)。
- **来自某个路径、而该路径已不存在的包会被保留。** 不带名字的 `lip update` 会给出警告，并让它保持原样。
- **点名的包必须已经安装。** 它们会变成由你安装的包。

## 卸载

[`lip uninstall`](../reference/commands/uninstall.md) 接受已安装包的名字：

```sh
lip uninstall LegacyMoney
```

可以按 `lip list` 显示的写法（去掉版本）来指定一个包，也可以用安装时的来源。不带变体的名字会匹配这个包所有已安装的变体。

lip 会删除这个包安装的文件，然后删除所有不再被需要的依赖。

需要知道的几点：

- **只能卸载由你安装的包。** 依赖会在需要它的包都卸载后自行消失。
- **仍被别的包需要的包会留下**，变成依赖。lip 会说明：`<name> is still required by other packages; keeping it as a dependency.`
- **你可能改过的文件会被保留**，前提是包这样声明了。例如，卸载游戏会保留 `server.properties`。见[保留什么删除什么](server_from_commands.md#保留什么删除什么)。
- **游戏和加载器可以像其他包一样卸载。** 没有游戏的工作区就没有服务器可运行。

## 试运行

每条会更改工作区的命令都接受 `--dry-run`（简写 `-n`）：`lip setup`、`lip install`、`lip update` 和 `lip uninstall`。

```sh
lip update --dry-run
```

试运行会做真实命令在“开始改动”之前所做的一切。它读取工作区，向网络查询版本，选定要安装的包，并检查它们的文件能否放置。然后它把计划告诉你，就此停下：

```text
Dry run: would uninstall LegacyMoney 0.16.1 (1 file)
Dry run: would uninstall LegacyRemoteCall 0.13.0 (1 file)
Dry run: nothing was changed.
```

试运行保证：

- **什么都不改。** 工作区、缓存和配置里的任何文件都不会被创建、修改或删除。唯一的例外是试运行自己失败时写下的失败报告。
- **什么都不运行。** 不运行包里的任何程序，也不往系统里安装任何东西。
- **什么都不问。** 真实命令会向你提问的地方，例如 Minecraft EULA，或者安装服务器前要卸载模组时，试运行只会说明它会问。
- **不等待其他命令。** 它不加锁，所以别的 lip 命令正在更改工作区时它也能运行。
- **仍然需要网络**，耗时可能和真实命令走到那一步时一样长。

试运行成功，并不保证真实命令也会成功：网络、磁盘或工作区在这期间都可能变化。试运行失败，则说明了真实命令会失败的原因。

如果之前有命令被中断，试运行会告诉你，并按工作区现在的样子做计划。见[崩溃或断电之后](troubleshooting.md#崩溃或断电之后)。

界面在每次更改之前都会替你做一次试运行：你在选择“应用”之前看到的计划，就是这个计划。

## 命令失败或被取消时

会更改工作区的命令，要么做完全部工作，要么什么都不做。

- **在改动之前就失败**：问题能提前发现时就是这样，例如包不存在、版本彼此不兼容、下载失败、某个文件归别的包所有。工作区原封不动。
- **中途失败**，例如某个文件写不进去。lip 会撤销它做过的每一处更改，工作区回到命令执行之前的样子。
- **你按了 Ctrl+C。** lip 还在准备时，会立刻停下。lip 正在更改工作区时，它会取消操作、撤销更改，并输出 `Cancelled. Any changes to the workspace were undone.`。再按一次 Ctrl+C 会立刻结束 lip，由下一条命令来收拾。

有一样东西无法撤销：包在安装过程中运行的程序已经做过的事。

失败之后，lip 会说明出了什么问题、你可以怎么办，并保存一份失败报告。见[故障排查](troubleshooting.md)。

## 接下来

- [lip 的工作原理](../concepts/how_lip_works.md)：一次操作内部发生了什么。
- [内部机制](../concepts/internals.md)：lip 如何查找版本、如何在版本之间做选择。
- [创建包](../authors/creating_packages.md)：制作你自己的包。
