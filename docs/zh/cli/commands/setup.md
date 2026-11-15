# lip setup

选择工作区的游戏和加载器。

## 概要

```shell
lip setup --game <VERSION> [--build <BUILD>] [--loader <LOADER> | --no-loader] [options]
```

## 说明

`lip setup` 把工作区的服务器设为版本 `<VERSION>`、构建 `<BUILD>` 的 Bedrock Dedicated Server，带加载器 `<LOADER>` 或不带加载器，并保留工作区中能与之配合的其他包。请在新工作区中先运行它再安装 mod；以后想换用另一个游戏版本时，再次运行它即可。

### 游戏与加载器

- **游戏**是 `github.com/LiteLDev/bds`（Bedrock Dedicated Server）的已安装包。一个工作区至多有一个；没有它的工作区没有游戏。
- 游戏的**构建**为 `windows` 或 `linux`。`windows` 是 Windows 版服务器 `github.com/LiteLDev/bds`；`linux` 是 Linux 版服务器 `github.com/LiteLDev/bds#linux`。
- **加载器**是 `github.com/LiteLDev/LeviLamina` 的已安装包，或者没有加载器。

`lip setup` 把两者都显式安装，并固定其版本：游戏来自 `github.com/LiteLDev/bds@<VERSION>` 或 `github.com/LiteLDev/bds#linux@<VERSION>`，加载器来自 `github.com/LiteLDev/LeviLamina@<version>`。此后 [`lip install`](install.md) 和 [`lip update`](update.md) 从不改变它们：如需改变游戏或加载器，请再次运行 `lip setup`。[`lip uninstall`](uninstall.md) 像卸载其他包一样卸载它们。

若工作区已有你要求的游戏和加载器，且版本相同，`lip setup` 不会改动它们。

### 加载器

不带 `--loader` 和 `--no-loader` 时，构建 `windows` 的加载器为 LeviLamina，构建 `linux` 没有加载器。LeviLamina 只加载到 Windows 版服务器中，因此 `--build linux` 与 `--loader` 一起使用会失败。

`--loader levilamina` 取对 `github.com/LiteLDev/bds` 的依赖接受该游戏版本的最新 LeviLamina 版本；若没有任何 LeviLamina 版本支持该游戏版本，`lip setup` 会在做任何改动之前失败。`--loader levilamina@<version>` 恰好取该版本的 LeviLamina，它同样必须支持该游戏版本。

### Minecraft EULA

在下载游戏之前，lip 会给出 [Minecraft 最终用户许可协议](https://www.minecraft.net/eula)和 [Microsoft 隐私声明](https://go.microsoft.com/fwlink/?LinkId=521839)的链接，并询问你是否接受：输入 `yes` 继续。其他任何回答都会让命令在做任何改动之前失败。

lip 不会记住你的回答：每当 `lip setup` 安装或改变游戏时都会询问，游戏保持不变时则不询问。带 `--accept-eula` 时，lip 不询问。当 lip 的标准输入或标准错误不是终端时（例如在脚本中），不带 `--accept-eula` 的 `lip setup` 会在做任何改动之前失败。

### 改变装有 mod 的工作区的游戏

改变游戏或加载器时，其他显式安装的包会针对新的游戏和加载器重新解析。若某个包的版本能与它们配合，则保持该版本；否则其版本可能改变。工作区原有的游戏和加载器会被卸载，不再被任何显式安装的包需要的依赖也会一并卸载。

若某个已安装的 mod 没有能与新的游戏和加载器配合的版本，lip 会列出这些 mod，并询问是否卸载它们；你不回答 yes 时，命令会在做任何改动之前失败。当 lip 的标准输入或标准错误不是终端时，`lip setup` 会直接失败，并指明这些 mod：请先用 [`lip uninstall`](uninstall.md) 卸载它们，再运行 `lip setup`。若其他包根本无法保留，命令会失败。

lip 先询问这些 mod，再询问 EULA，时机在它决定好要做什么之后、下载游戏之前。

### 系统依赖

对构建 `windows`，`lip setup` 会安装 Windows 版服务器需要而系统缺少的组件：Linux 和 macOS 上的 Wine，以及 Microsoft Visual C++ 可再发行组件包（参见[系统依赖](../../guides/internal_mechanism.md#系统依赖)）。Linux 版服务器两者都不需要。

## 选项

| 选项 | 说明 |
| --- | --- |
| `--game <VERSION>` | 游戏版本，例如 `1.21.0`。必需。 |
| `--build <BUILD>` | `windows`（默认）或 `linux`。 |
| `--loader <LOADER>` | `levilamina`，即支持该游戏的最新 LeviLamina；或 `levilamina@<version>`。构建 `windows` 的默认值。 |
| `--no-loader` | 不带加载器设置游戏。构建 `linux` 的默认值。不能与 `--loader` 同时使用。 |
| `--accept-eula` | 不经询问，接受 Minecraft EULA 和 Microsoft 隐私声明。 |
| `-n, --dry-run` | 仅模拟执行，不做实际更改。不询问任何问题；说明将安装、卸载或标记为显式安装哪些包，哪些 mod 会被排除，以及 lip 是否会请你接受 EULA。 |

## 示例

设置游戏 1.21.0，并使用支持它的最新 LeviLamina：

```shell
lip setup --game 1.21.0
```

设置指定版本的 LeviLamina，并在脚本中接受 EULA：

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

查看换用另一个游戏版本会对工作区的 mod 造成什么影响：

```shell
lip setup --game 1.21.50 --dry-run
```
