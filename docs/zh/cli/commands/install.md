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
3. **带 git 引用的仓库** — `<path>[#<variant>]@branch:<branch>`、`@tag:<tag>` 或 `@rev:<commit>`（例如 `github.com/LiteLDev/LeviLamina@branch:main`）：分支头、标签或提交 ID 所指的提交，无论其版本为何。接受任意分支名或标签名，而不仅是 `v<SemVer>` 标签；提交 ID 必须是完整的 40 位十六进制数字。参见 [git 引用](#git-引用)。
4. **URL** — ZIP 文件的 `https://` URL（例如 `https://example.com/package.zip`），后面可跟 `#<variant>`。不接受 `http://` 和 `file://` 等其他协议。
5. **本地路径** — 磁盘上的 ZIP 文件或目录，使用绝对路径或相对于当前目录的路径（例如 `./package.zip`、`C:\Downloads\package.zip` 或 `../MyTool`），后面可跟 `#<variant>`。以 `./`、`../`、`.\` 或 `..\` 开头、为 `.` 或 `..`、为绝对路径，或不含 `@` 且以 `.zip` 结尾（不区分大小写）或指向已存在的文件或目录的参数，都按本地处理。本地文件必须是 ZIP 归档，与文件名无关；本地目录以其内容作为归档。

每个包的根部都必须有 `lip.toml`（参见 [lip.toml](../../concepts/lip_toml.md)），且其所请求的变体有对应工作区平台和 RID 的条目。

包的依赖也会一并安装。安装一个已被显式安装的包会报错；如需改变其版本，请使用 [`lip update`](update.md)。两个参数指向同一身份同样会报错。作为依赖安装的包会变为显式安装；若其版本满足该参数，则保持该版本不变。

### git 引用

git 引用把仓库固定到某个提交：

- `@<version>` 取标签 `v<version>` 所指的提交。`lip update` 会保持该版本。
- `@branch:<branch>` 取分支当前的头。[`lip update`](update.md) 会跟随分支到新的头。
- `@tag:<tag>` 取标签所指的提交；任意标签名均可，例如 `@tag:nightly`。`lip update` 会重新解析该标签。
- `@rev:<commit>` 恰好取该提交，它永远不会变化。不接受缩写的提交 ID，因为解析缩写需要仓库的全部历史。

以这种方式安装的包，其版本为该提交处 `lip.toml` 声明的 `version`。只有名为 `v<SemVer>` 的标签（例如 `@tag:v1.2.0`）会让包取标签的版本，与普通版本相同：该处的 `lip.toml` 必须声明该版本（早期格式的 `tooth.json` 则取标签版本）。[状态文件](../../concepts/lockfiles.md)会记录来源、其解析到的提交及其包文件，因此在你更新之前，工作区始终保持该提交。

从 git 引用安装的包，也能满足对其不带 git 引用的包 ID 和变体的依赖，只要其版本在依赖的范围内，且该仓库有 lip 可为该依赖安装的版本标签：要求 `github.com/LiteLDev/LeviLamina` `>=1.5.0` 的 mod，会接受从 `@branch:main` 安装且声明 `1.6.0` 的 LeviLamina；若其声明 `1.4.0`，或 LeviLamina 没有 `1.5.0` 及以上的版本标签，则失败。

## 选项

| 选项 | 说明 |
| --- | --- |
| `-n, --dry-run` | 仅模拟执行，不做实际更改。 |
| `--ignore-scripts` | 不执行包的 `hooks`，包括该操作移除或替换的包的卸载钩子。 |
| `--no-dependencies` | 对每个参数只安装其第一个候选，替换已安装的同一身份的包；不安装依赖，也不卸载其他包。身份、版本和提交都相同的已安装包只会被标记为显式安装。这可能导致工作区损坏。 |
| `--rid <RID>` | 工作区的 .NET RID，例如 `win-x64`。 |

## 示例

```shell
lip install github.com/LiteLDev/LeviLamina
lip install github.com/LiteLDev/LegacyScriptEngine#lua
lip install github.com/user/repo@1.0.0 --rid win-x64
lip install github.com/LiteLDev/LeviLamina@branch:main
lip install github.com/user/repo@tag:v1.0.0
lip install https://example.com/package.zip
lip install ./package.zip
lip install ../MyTool
```
