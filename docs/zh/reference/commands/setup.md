# lip setup

选择工作区的游戏和加载器。

## 概要

```
lip setup --game <VERSION> [--build <BUILD>] [--loader <LOADER> | --no-loader] [--accept-eula] [-n | --dry-run] [-h | --help]
```

## 说明

`lip setup` 把工作区的服务器设为版本 `<VERSION>`、构建 `<BUILD>` 的游戏，带加载器 `<LOADER>` 或不带加载器，并保留工作区中能与之配合的其他包。在空目录中运行它可以创建一个服务器；以后想换游戏版本或加载器时，再运行一次即可。

### 术语

- **游戏**是已安装的 `github.com/LiteLDev/bds` 包，即 Minecraft Bedrock Dedicated Server。一个工作区最多有一个。没有它的工作区**没有游戏**。
- 游戏的**构建**是 `windows` 或 `linux`：Windows 版服务器或 Linux 版服务器。
- **加载器**是已安装的 `github.com/LiteLDev/LeviLamina` 包。没有它的工作区**没有加载器**。
- 如果某个 LeviLamina 版本对 `github.com/LiteLDev/bds` 的依赖接受某个游戏版本，就说它**适配**这个游戏版本。

### 执行过程

依次进行：

1. 根据选项确定游戏、构建和加载器。参见[加载器](#加载器)。
2. 在下列情况下，它在做出任何更改之前失败：
   - 构建为 `linux` 却要求加载器：LeviLamina 只能加载到 Windows 版服务器中；
   - 该游戏版本没有这种构建（参见[游戏版本与构建](#游戏版本与构建)）；
   - 加载器版本不存在，或者不适配该游戏版本。
3. 规划要安装的包。游戏从 `github.com/LiteLDev/bds@<VERSION>` 请求，构建为 `linux` 时从 `github.com/LiteLDev/bds#linux@<VERSION>` 请求；加载器从 `github.com/LiteLDev/LeviLamina@<version>` 请求。其他显式安装的包按各自安装时的来源重新请求。
   - 包的版本仍然可用时保持不变；否则可能换成其他版本。
   - 工作区原有的游戏和加载器会被卸载，除非它们正是所要求的。
   - 不再被任何显式安装的包需要的依赖会被卸载。
4. 如果有[被排除的包](#被排除的包)，询问如何处理。
5. 如果要安装游戏，请你接受[许可协议](#许可协议)。
6. 构建为 `windows` 时，安装 Windows 版服务器需要而系统缺少的组件：Linux 和 macOS 上的 Wine，以及 Microsoft Visual C++ 可再发行组件包。参见 [Linux 和 macOS](../../guides/linux_macos.md)。
7. 作为一次[工作区操作](../cli.md#工作区操作)安装这些包。

如果工作区已经有所要求的游戏和加载器，构建和版本都相同，`lip setup` 不会为它们安装或卸载任何东西。

游戏和加载器是显式安装的，并固定在各自的版本上。之后 [`lip install`](install.md) 和 [`lip update`](update.md) 都不会改变它们；要更换，请再次运行 `lip setup`。[`lip uninstall`](uninstall.md) 可以像卸载其他包一样卸载它们。

### 加载器

| 选项 | 加载器 |
| --- | --- |
| 既没有 `--loader` 也没有 `--no-loader`，构建为 `windows` | 等同于 `--loader levilamina`。 |
| 既没有 `--loader` 也没有 `--no-loader`，构建为 `linux` | 不带加载器。 |
| `--loader levilamina` | 适配该游戏版本的最新 LeviLamina。没有适配的版本时，命令在做出任何更改之前失败。 |
| `--loader levilamina@<version>` | 恰好是这个 LeviLamina 版本。它必须适配该游戏版本。 |
| `--no-loader` | 不带加载器。 |

`levilamina` 不区分大小写。

### 游戏版本与构建

lip 从 `https://levimc.org/api/v4/mc/versions/` 获取服务器构建的列表。一个构建由四个数字组成，例如 `1.21.80.03`。它对应两个游戏版本：

| 构建 | 游戏版本 | 指的是 |
| --- | --- | --- |
| `1.21.80.03` | `1.21.80` | 该系统的列表中第一个 `1.21.80.*` 构建。 |
| `1.21.80.03` | `1.21.80-patch.3` | 恰好是构建 `1.21.80.03`。 |

所以一般写 `--game 1.21.80`；需要某个特定构建时写 `--game 1.21.80-patch.3`。Windows 构建和 Linux 构建是两个独立的列表：一个版本可能只存在于其中一个。

服务器本身从 `www.minecraft.net` 下载。

| 构建 | 在哪里运行 |
| --- | --- |
| `windows` | 在 Windows 上直接运行。在 Linux 和 macOS 上通过 lip 安装的 Wine 运行。 |
| `linux` | 只能在 x64 的 Linux 上运行。`lip setup --build linux` 在任何系统上都能执行，但在其他系统上 [`lip run`](run.md) 会拒绝启动服务器。 |

::: info
界面只在 lip 运行于 x64 的 Linux 时才提供构建 `linux`。命令没有这个限制。
:::

### 许可协议

游戏受 [Minecraft 最终用户许可协议](https://www.minecraft.net/eula)和 [Microsoft 隐私政策](https://go.microsoft.com/fwlink/?LinkId=521839)约束。

`lip setup` 要安装游戏时，会输出这两个链接，并询问你是否接受。输入 `yes` 继续。其他任何回答都会让命令在做出任何更改之前失败。

- 带 `--accept-eula` 时，lip 不询问。
- 不带它时，如果标准输入或标准错误不是终端（例如在脚本中），命令在做出任何更改之前失败。
- 回答不会被记录。每次安装游戏的设置都会重新询问；游戏保持不变的设置不会询问。

### 被排除的包

除游戏和加载器之外，某个显式安装的包如果没有任何版本能与新的游戏和加载器配合，它就会**被排除**。它可以是模组，也可以是其他任何包。

lip 会列出被排除的包，并询问是否卸载它们。你没有回答 `yes` 时，命令在做出任何更改之前失败。标准输入或标准错误不是终端时，lip 无法询问：命令失败并列出它们。请先用 [`lip uninstall`](uninstall.md) 卸载它们，再运行 `lip setup`。

如果剩下的包根本无法一起安装，命令失败。

lip 先询问被排除的包，再询问许可协议。两个问题都在 lip 决定好要做什么之后、下载游戏之前提出。

## 选项

| 选项 | 说明 |
| --- | --- |
| `--game <VERSION>` | 游戏版本，例如 `1.21.0`：不带前导 `v` 的 SemVer 版本。必需。其他值都是用法错误。 |
| `--build <BUILD>` | `windows` 或 `linux`。省略时为 `windows`。其他值都是用法错误。 |
| `--loader <LOADER>` | `levilamina` 或 `levilamina@<version>`。其他值都是用法错误。 |
| `--no-loader` | 设置游戏而不带加载器。同时给出 `--loader` 和 `--no-loader` 是用法错误。 |
| `--accept-eula` | 接受许可协议，不再询问。 |
| `-n`、`--dry-run` | [试运行](../cli.md#试运行)。不提问。消息会列出将被安装、卸载或标记为显式安装的包、被排除的包，以及 lip 是否会请你接受许可协议。 |
| `-h`、`--help` | 输出帮助并退出。 |

## 输出

`lip setup` 没有结果：它不向标准输出写任何内容。进度、提问和最后一行都是写到标准错误的消息。

## 退出状态

| 退出码 | 含义 |
| --- | --- |
| `0` | 设置已完成，或试运行已结束。 |
| 非 `0` | 用法错误，或设置失败。失败的设置会被撤销。 |

## 示例

创建一个游戏版本为 1.21.0 的服务器，带适配它的最新 LeviLamina：

```shell
lip setup --game 1.21.0
```

设置指定版本的 LeviLamina，并在脚本中接受许可协议：

```shell
lip setup --game 1.21.0 --loader levilamina@1.0.0 --accept-eula
```

设置不带加载器的 Windows 版服务器：

```shell
lip setup --game 1.21.0 --no-loader
```

设置 Linux 版服务器，它没有加载器：

```shell
lip setup --game 1.21.0 --build linux
```

查看换到另一个游戏版本会对已安装的包产生什么影响：

```shell
lip setup --game 1.21.50 --dry-run
```

## 另请参阅

- [用命令行开服](https://levimc.org/tutorials/zh-Hans/server/command-line/)
- [工作区](../../interface/workspaces.md)：界面中的新建服务器向导和更换版本。
- [更换游戏后保留什么](../files.md#更换游戏后保留什么)
- [`lip install`](install.md#用-install-进行设置)：安装 LeviLamina 或服务器本身时，它会设置服务器。
- [`lip run`](run.md)
