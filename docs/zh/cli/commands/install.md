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

1. **包路径** — `<path>` 或 `<path>#<variant>`（例如 `github.com/user/repo`）：最新版本。
2. **带版本的包路径** — `<path>@<version>` 或 `<path>#<variant>@<version>`（例如 `github.com/user/repo#lua@1.0.0`）：恰好该版本。版本为不带前导 `v` 的 SemVer 2.0.0 版本。
3. **带 git 引用的包路径** — `<path>[#<variant>]@branch:<branch>`、`@tag:<tag>` 或 `@rev:<commit>`（例如 `github.com/LiteLDev/LeviLamina@branch:main`）：分支头、标签或提交 ID 所指的提交，无论其版本为何。接受任意分支名或标签名，而不仅是 `v<SemVer>` 标签；提交 ID 必须是完整的 40 位十六进制数字。参见 [git 引用](#git-引用)。
4. **远程归档** — `.mcmod` 或 `.zip` 文件的 `https://` URL（例如 `https://example.com/package.mcmod`），后面可跟 `#<variant>`。下载内容按 URL 缓存。
5. **本地归档** — `.mcmod` 或 `.zip` 文件，使用绝对路径或相对于当前目录的路径（例如 `./package.mcmod` 或 `C:\Downloads\package.zip`），后面可跟 `#<variant>`。以 `./`、`../`、`.\` 或 `..\` 开头、为 `.` 或 `..`、为绝对路径，或不含 `@` 且以 `.mcmod` 或 `.zip` 结尾（不区分大小写）或指向已存在的文件或目录的参数，都按本地处理。文件是否为 ZIP 归档只取决于其内容，与扩展名无关。

本地目录不是包：`lip install ./some/dir` 会失败，且不改动工作区。请先把目录打包为 `.mcmod` 文件（根目录含 `manifest.json` 的 ZIP 归档）。包内部的相对引用（`./...`），例如路由文档的路由、或嵌套在归档中的 `.mcmod`，仍然照常工作。

包的依赖也会一并安装。安装一个已被显式安装的包会报错；如需改变其版本，请使用 [`lip update`](update.md)。作为依赖安装的包会变为显式安装。

### git 引用

git 引用把包路径固定到其仓库的某个提交：

- `@branch:<branch>` 取分支当前的头。[`lip update`](update.md) 会跟随分支到新的头。
- `@tag:<tag>` 取标签所指的提交；任意标签名均可，例如 `@tag:nightly`。`lip update` 会重新解析该标签。
- `@rev:<commit>` 恰好取该提交，它永远不会变化。不接受缩写的提交 ID，因为解析缩写需要仓库的全部历史。

以这种方式安装的包，其版本为该提交处 `manifest.json`（或 `tooth.json`）声明的 `version`。只有名为 `v<SemVer>` 的标签（例如 `@tag:v1.2.0`）会让包取标签的版本，与普通版本相同：该处的 `manifest.json` 必须声明该版本，`tooth.json` 则以标签版本为准。[锁](../../concepts/lockfiles.md)会记录 git 引用及其解析到的提交，因此在你更新之前，工作区始终保持该提交。

从 git 引用安装的包，也能满足对其不带 git 引用的包路径的依赖，只要其版本在依赖的范围内：要求 `github.com/LiteLDev/LeviLamina` `>=1.5.0` 的模组，会接受从 `@branch:main` 安装且声明 `1.6.0` 的 LeviLamina；若其声明 `1.4.0` 则失败。

## 选项

| 选项 | 说明 |
| --- | --- |
| `-n, --dry-run` | 仅模拟执行，不做实际更改。 |
| `--ignore-scripts` | 不执行任何包脚本，包括操作所移除或替换的包的卸载脚本（以及 `tooth.json` 包的脚本）。 |
| `--no-dependencies` | 对每个参数恰好安装其第一个候选，替换同一身份的已安装包；不安装任何依赖，也不卸载其他包。这可能导致工作区损坏。 |
| `--rid <RID>` | 工作区的 .NET RID，例如 `win-x64`。 |

## 示例

```shell
lip install github.com/LiteLDev/LeviLamina
lip install github.com/LiteLDev/LegacyMoney@0.22.0 --rid win-x64
lip install github.com/LiteLDev/LeviLamina@branch:main
lip install github.com/LiteLDev/LegacyMoney@tag:v0.22.0
lip install https://github.com/LiteLDev/LegacyMoney/releases/download/v0.22.0/LegacyMoney-server-win-x64.mcmod
lip install ./LegacyMoney-server-win-x64.mcmod
```
