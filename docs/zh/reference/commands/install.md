# lip install

向工作区安装包。

## 概要

```
lip install <PACKAGES>... [--accept-eula] [-n | --dry-run] [-h | --help]
```

## 说明

`lip install` 把 `<PACKAGES>` 中的每个包连同它的依赖安装到工作区，作为一次[工作区操作](../cli.md#工作区操作)。`<PACKAGES>` 中的每一项都是[包参数](../cli.md#包参数)：

| 形式 | 示例 | 安装的内容 |
| --- | --- | --- |
| `<repo>` | `github.com/user/repo` | git 仓库 `https://<repo>.git` 的最高版本标签。 |
| `<repo>#<variant>` | `github.com/LiteLDev/LegacyScriptEngine#lua` | 该包的这个变体。下面的每种形式也都可以带 `#<variant>`，写在 `@` 之前。 |
| `<repo>@<version>` | `github.com/user/repo@1.0.0` | 标签 `v<version>` 的提交。 |
| `<repo>@branch:<name>` | `github.com/user/repo@branch:main` | 该分支当前指向的提交。 |
| `<repo>@tag:<name>` | `github.com/user/repo@tag:nightly` | 该标签当前指向的提交。任何标签名都可以。 |
| `<repo>@rev:<commit>` | `github.com/user/repo@rev:0123456789abcdef0123456789abcdef01234567` | 这个提交。必须写全 40 位十六进制数字。 |
| `https://` URL | `https://example.com/package.zip` | 该 URL 上的 ZIP 归档。后面可以带 `#<variant>`。 |
| 本地路径 | `./package.zip`、`../MyTool`、`C:\Downloads\package.zip` | 磁盘上的 ZIP 文件或目录。后面可以带 `#<variant>`。 |

不含 `/` 的 `<repo>` 是 LiteLDev 的仓库：`LeviLamina` 就是 `github.com/LiteLDev/LeviLamina`。

它做的事：

- 你指名的包成为**显式安装**的包。原先作为依赖安装的包也会变成显式安装。
- 这些包的依赖作为**依赖包**安装。
- 已安装的包在版本仍然可用时保持不变。否则依赖包可能换成其他版本。
- 不再被任何显式安装的包需要的依赖包会被卸载。

它在做出任何更改之前拒绝的情况：

- 包参数无效，或者没有适用于运行端 `server` 和平台 `win-x64` 的包。
- 包已经是显式安装的。要更换它的版本，请用 [`lip update`](update.md)。
- 两个参数指名了同一个包。
- 这些包无法一起安装，例如两个模组的模组名相同，或者某个依赖没有任何版本能满足。
- 任何会改变游戏或加载器的情况。见下文。

从 `@branch:`、`@tag:` 或 `@rev:` 安装的包，版本取该提交上 `lip.toml` 中的 `version`。只要这个版本落在范围内，这样的包也能满足对同一仓库和变体的依赖。

### 游戏与加载器

**游戏**和**加载器**是已安装的 `github.com/LiteLDev/bds` 包和 `github.com/LiteLDev/LeviLamina` 包；参见 [`lip setup`](setup.md#术语)。

除了[用 install 进行设置](#用-install-进行设置)所说的情况，`lip install` 从不安装、卸载或替换游戏和加载器。如果在它们保持不变的前提下无法安装这些包（例如某个模组需要更新的 LeviLamina），命令失败，并给出游戏版本、加载器版本和 `lip setup`。请先用 [`lip setup`](setup.md) 设置该模组支持的游戏和加载器，再安装模组。

在没有游戏的工作区中，如果你指名的包或它将要安装的包是模组、服务器或 LeviLamina，`lip install` 失败并指向 `lip setup`。不需要它们的文件包可以在没有游戏的情况下安装。

### 用 install 进行设置

参数是 `github.com/LiteLDev/bds`（带不带变体或引用都算）时，它**指名了服务器**。参数是不带变体的 `github.com/LiteLDev/LeviLamina`，并且没有引用或引用为 `@<version>` 时，它**指名了加载器**。两者都不区分大小写。

有参数指名了服务器或加载器时，`lip install` 会作为一次 [`lip setup`](setup.md) 执行，连同其他参数一起，在同一次操作中完成：

| 设置项 | 取值 |
| --- | --- |
| 构建 | 服务器写成 `github.com/LiteLDev/bds#linux` 时为 `linux`；否则为 `windows`。 |
| 加载器 | 没有参数指名 LeviLamina 时，不带加载器。否则为 LeviLamina：有 `@<version>` 时取该版本；没有时，取适配服务器参数给出的游戏版本的最新版本；没有任何参数给出游戏版本时，取最新的 LeviLamina。 |
| 游戏版本 | 服务器参数的 `@<version>`。没有时：取上述 LeviLamina 接受的最新游戏版本；没有参数指名 LeviLamina 时，取最新的游戏版本。 |

| 命令 | 设置的内容 |
| --- | --- |
| `lip install LeviLamina` | 最新的 LeviLamina，以及它支持的最新游戏。 |
| `lip install LeviLamina@1.0.0` | LeviLamina 1.0.0，以及它支持的最新游戏。 |
| `lip install github.com/LiteLDev/bds@1.21.0 LeviLamina` | 游戏 1.21.0，以及适配它的最新 LeviLamina。 |
| `lip install github.com/LiteLDev/bds@1.21.0` | Windows 版服务器 1.21.0，不带加载器。 |
| `lip install github.com/LiteLDev/bds#linux` | 最新的 Linux 版服务器，不带加载器。 |

两个参数都指名服务器，或都指名加载器时，命令失败。[`lip setup`](setup.md) 所说的一切都适用：许可协议的询问、`--accept-eula`、被排除的包和系统依赖。

## 选项

| 选项 | 说明 |
| --- | --- |
| `--accept-eula` | `lip install` 作为设置执行时，接受[游戏的许可协议](setup.md#许可协议)，不再询问。其他情况下没有作用。 |
| `-n`、`--dry-run` | [试运行](../cli.md#试运行)。消息会列出将被安装、卸载或标记为显式安装的包。不做任何更改。 |
| `-h`、`--help` | 输出帮助并退出。 |

## 输出

`lip install` 没有结果：它不向标准输出写任何内容。进度和最后一行都是写到标准错误的消息。包的钩子命令自己向标准输出和标准错误写入。

## 退出状态

| 退出码 | 含义 |
| --- | --- |
| `0` | 包已安装，或试运行已结束。 |
| 非 `0` | 用法错误，或安装失败。失败的安装会被撤销。 |

## 示例

从仓库安装一个模组的最新版本：

```shell
lip install github.com/LiteLDev/LegacyScriptEngine#lua
```

安装指定版本，并先看看它会做什么：

```shell
lip install github.com/user/repo@1.0.0 --dry-run
lip install github.com/user/repo@1.0.0
```

在一次操作中安装多个包：

```shell
lip install github.com/user/repo github.com/user/other@2.1.0
```

从分支、URL、ZIP 文件和目录安装：

```shell
lip install github.com/user/repo@branch:main
lip install https://example.com/package.zip
lip install ./package.zip
lip install ../MyTool
```

在脚本中一步完成设置服务器和安装模组：

```shell
lip install LeviLamina github.com/user/repo --accept-eula
```

## 另请参阅

- [管理包](../../guides/managing_packages.md)
- [获取模组](../../interface/get_mods.md)：在界面中查找和安装模组。
- [`lip setup`](setup.md)、[`lip update`](update.md)、[`lip uninstall`](uninstall.md)、[`lip list`](list.md)
- [lip.toml](../../concepts/lip_toml.md)：什么是包。
