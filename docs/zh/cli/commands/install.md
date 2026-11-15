# lip install

安装包。

## 概要

```shell
lip install <PACKAGES> [options]
```

## 参数

| 参数 | 说明 |
| --- | --- |
| `<PACKAGES>` | 要安装的包。 |

每个包参数可以是以下形式之一：

1. **仓库** — `<path>` 或 `<path>#<variant>`（例如 `github.com/user/repo`）：git 仓库 `https://<path>.git`，最新版本优先。
2. **带版本的仓库** — `<path>@<version>` 或 `<path>#<variant>@<version>`（例如 `github.com/user/repo#lua@1.0.0`）：恰好该版本，即标签 `v<version>` 所指的提交。版本为不带前导 `v` 的 SemVer 2.0.0 版本。它与 git 引用一样会固定该包：参见 [git 引用](#git-引用)。
3. **带 git 引用的仓库** — `<path>[#<variant>]@branch:<branch>`、`@tag:<tag>` 或 `@rev:<commit>`（例如 `github.com/user/repo@branch:main`）：分支头、标签或提交 ID 所指的提交，无论其版本为何。接受任意分支名或标签名，而不仅是 `v<SemVer>` 标签；提交 ID 必须是完整的 40 位十六进制数字。参见 [git 引用](#git-引用)。
4. **URL** — ZIP 文件的 `https://` URL（例如 `https://example.com/package.zip`），后面可跟 `#<variant>`。不接受 `http://` 和 `file://` 等其他协议。
5. **本地路径** — 磁盘上的 ZIP 文件或目录，使用绝对路径或相对于当前目录的路径（例如 `./package.zip`、`C:\Downloads\package.zip` 或 `../MyTool`），后面可跟 `#<variant>`。以 `./`、`../`、`.\` 或 `..\` 开头、为 `.` 或 `..`、为绝对路径，或不含 `@` 且以 `.zip` 结尾（不区分大小写）或指向已存在的文件或目录的参数，都按本地处理。本地文件必须是 ZIP 归档，与文件名无关；本地目录以其内容作为归档。

不含 `/` 的仓库路径表示 LiteLDev 的仓库：`LeviLamina@26.60.6` 是 `github.com/LiteLDev/LeviLamina@26.60.6` 的简写，`LegacyScriptEngine#lua` 是 `github.com/LiteLDev/LegacyScriptEngine#lua` 的简写。

每个包的根部都必须有 `lip.toml`（参见 [lip.toml](../../concepts/lip_toml.md)），且其所请求的变体有对应工作区运行端和平台的条目。

在还没有游戏的工作区中，请先用 [`lip setup`](setup.md) 设置服务器，或者安装 LeviLamina 或服务器本身，这同样会设置服务器（参见[游戏与加载器](#游戏与加载器)）。

包的依赖也会一并安装。安装一个已被显式安装的包会报错；如需改变其版本，请使用 [`lip update`](update.md)。两个参数指向同一身份同样会报错。作为依赖安装的包会变为显式安装；若其版本满足该参数，则保持该版本不变。

### git 引用

git 引用把仓库固定到某个提交：

- `@<version>` 取标签 `v<version>` 所指的提交。`lip update` 会保持该版本。
- `@branch:<branch>` 取分支当前的头。[`lip update`](update.md) 会跟随分支到新的头。
- `@tag:<tag>` 取标签所指的提交；任意标签名均可，例如 `@tag:nightly`。`lip update` 会重新解析该标签。
- `@rev:<commit>` 恰好取该提交，它永远不会变化。不接受缩写的提交 ID，因为解析缩写需要仓库的全部历史。

以这种方式安装的包，其版本为该提交处 `lip.toml` 声明的 `version`。只有名为 `v<SemVer>` 的标签（例如 `@tag:v1.2.0`）会让包取标签的版本，与普通版本相同：该处的 `lip.toml` 必须声明该版本（早期格式的 `tooth.json` 则取标签版本）。[状态文件](../../concepts/lockfiles.md)会记录来源、其解析到的提交及其包文件，因此在你更新之前，工作区始终保持该提交。

从 git 引用安装的包，也能满足对其不带 git 引用的包 ID 和变体的依赖，只要其版本在依赖的范围内，且该仓库有 lip 可为该依赖安装的版本标签：要求 `github.com/user/lib` `>=1.5.0` 的 mod，会接受从 `@branch:main` 安装且声明 `1.6.0` 的 `github.com/user/lib`；若其声明 `1.4.0`，或 `github.com/user/lib` 没有 `1.5.0` 及以上的版本标签，则失败。

### 平台

lip 安装的是运行端 `server` 和平台 `win-x64` 所对应的条目，`win-x64` 是每个工作区[环境](../../concepts/lip_toml.md#环境)的平台，**无论 lip 运行在什么系统上**：在 Linux 和 macOS 上，lip 同样安装 Windows 版服务器的包，并通过 Wine 运行它及其包的钩子，除非你设置的是 Linux 版服务器（参见[运行服务器](../../guides/running_server.md)）。

### 游戏与加载器

工作区的服务器就是它的游戏，即 `github.com/LiteLDev/bds` 的已安装包，带上其加载器，即 `github.com/LiteLDev/LeviLamina` 的已安装包，或不带加载器。由 [`lip setup`](setup.md) 选择它们。

在没有游戏的工作区中，安装 mod，或安装会把 LeviLamina 或服务器作为依赖安装的包，会失败并提示使用 `lip setup`。

指向 LeviLamina（不带 git 引用或带 `@<version>` 的 `github.com/LiteLDev/LeviLamina`）或服务器（不带 git 引用或带 `@<version>` 的 `github.com/LiteLDev/bds` 或 `github.com/LiteLDev/bds#linux`）的参数，会让 `lip install` 作为一次 [`lip setup`](setup.md) 执行，并与其他参数在同一个操作中完成：

- **构建**：以 `github.com/LiteLDev/bds#linux` 指向服务器时为 `linux`，否则为 `windows`。
- **加载器**：有参数指向 LeviLamina 时为 LeviLamina，取该参数给出的版本；不带版本时，取支持服务器参数所给游戏版本的最新 LeviLamina，若没有参数给出游戏版本，则取最新的 LeviLamina。没有参数指向 LeviLamina 时没有加载器。
- **游戏版本**：服务器参数给出的版本；未给出时，取该 LeviLamina 对 `github.com/LiteLDev/bds` 的依赖所接受的最新服务器版本，若没有参数指向 LeviLamina，则取最新的服务器版本。

因此，`lip install LeviLamina` 设置最新的 LeviLamina 及其支持的最新游戏，`lip install LeviLamina@<version>` 设置该版本的 LeviLamina 及其支持的最新游戏，`lip install github.com/LiteLDev/bds#linux@<version>` 设置该版本的 Linux 版服务器，不带加载器。两个参数都指向服务器，或两个参数都指向 LeviLamina，会报错。与 `lip setup` 一样，lip 在下载游戏之前会请你接受 Minecraft EULA（参见 [Minecraft EULA](setup.md#minecraft-eula)）；在脚本中请加上 `--accept-eula`。

除此之外，`lip install` 从不安装、卸载或替换游戏或加载器。若这些包无法在游戏和加载器保持不变的情况下安装，例如某个 mod 需要更新的 LeviLamina，命令会失败，并指明游戏版本、LeviLamina 的版本以及 `lip setup`：请先用 `lip setup` 设置该 mod 支持的游戏和加载器，再安装该 mod。

### 服务器

基岩版专用服务器 `github.com/LiteLDev/bds` 有三个变体：默认变体和 `windows` 安装 Windows 版服务器，`linux` 安装 Linux 版服务器。一个工作区只能装其中一个，即它的游戏，由 `lip setup` 设置（Linux 版服务器用 `--build linux`），或按上文所述由 `lip install` 设置：

```shell
lip install github.com/LiteLDev/bds#linux
```

Linux 版服务器的 `bedrock_server` 由 lip 自己设为可执行。[`lip run`](run.md) 在 x64 的 Linux 上启动 Linux 版服务器；参见[运行服务器](../../guides/running_server.md#linux-版服务器)。

## 选项

| 选项 | 说明 |
| --- | --- |
| `--accept-eula` | 当 `lip install` 设置游戏时，不经询问，接受 Minecraft EULA 和 Microsoft 隐私声明；其他情况下不起作用。 |
| `-n, --dry-run` | 仅模拟执行，不做实际更改。 |

## 示例

```shell
lip install LeviLamina
lip install LeviLamina@26.60.6 --accept-eula
lip install github.com/LiteLDev/bds#linux
lip install github.com/LiteLDev/LegacyScriptEngine#lua
lip install github.com/user/repo@1.0.0
lip install github.com/user/repo@branch:main
lip install github.com/user/repo@tag:v1.0.0
lip install https://example.com/package.zip
lip install ./package.zip
lip install ../MyTool
```
