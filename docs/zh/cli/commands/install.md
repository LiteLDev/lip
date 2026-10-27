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
3. **本地目录** — 例如 `./path/to/package/`。
4. **本地归档** — ZIP 归档，例如 `./package.mcmod`。
5. **远程 URL** — ZIP 归档的 `https://` URL（例如 `https://example.com/package.mcmod`）。

任何指向已存在文件或目录的参数都按本地处理，即使不以 `./` 开头。位于工作区内的本地包以相对于工作区的路径记录；位于工作区外的则从其父目录打开。本地包或 URL 后也可以跟 `#<variant>`。

包的依赖也会一并安装。安装一个已被显式安装的包会报错；如需改变其版本，请使用 [`lip update`](update.md)。作为依赖安装的包会变为显式安装。

## 选项

| 选项 | 说明 |
| --- | --- |
| `-n, --dry-run` | 仅模拟执行，不做实际更改。 |
| `--ignore-scripts` | 跳过执行 `preInstall` 和 `postInstall` 脚本（`tooth.json` 包为 `pre_install`、`install` 和 `post_install`）。 |
| `--no-dependencies` | 跳过安装依赖。 |
| `--rid <RID>` | 工作区的 .NET RID，例如 `win-x64`。 |

## 示例

```shell
lip install github.com/LiteLDev/LeviLamina
lip install github.com/LiteLDev/LegacyMoney@0.22.0 --rid win-x64
lip install ./LegacyMoney-server-win-x64.mcmod
```
