# lip pkg view

显示包的包文件（`lip.toml`）及其仓库的版本。

## 概要

```sh
lip pkg view <PACKAGE>
```

## 参数

| 参数 | 说明 |
| --- | --- |
| `<PACKAGE>` | 要查看的包，形式与 [`lip install`](install.md#参数) 相同，例如 `<path>`、`<path>@<version>`、`<path>@branch:<branch>`、`./package.zip`、`./MyTool` 或 `https://` URL。 |

## 说明

以 JSON 形式输出一个含两个成员的对象：

```json
{ "manifest": { ... }, "versions": ["<version>", ...] }
```

- **`manifest`**：参数所指归档根部的 `lip.toml`，其中的[模板](../../concepts/lip_toml.md#模板)已渲染。对于远程仓库，归档是最新版本标签处、通过 `@<version>` 指定的版本的标签处，或 git 引用所指提交处的仓库。本地 ZIP 文件或目录，或 URL，则按原样读取。若无法获取该归档，或它不是包，命令会失败。
- **`versions`**：对于远程仓库，为其版本：名称恰为 `v<SemVer 2.0.0>`（例如 `v1.2.3`）的标签，不带前导 `v`，从低到高排列。`v1.2` 这样的标签不是版本。参数的变体和 git 引用不影响该列表。对于 URL 或本地路径，为空数组。

[早期格式](../../concepts/lip_toml.md#早期格式)的 `tooth.json` 显示为 lip 针对当前目录中工作区的[环境](../../concepts/lip_toml.md#环境)从中读取的包文件：运行端 `server`，以及其[状态文件](../../concepts/lockfiles.md)记录的平台，没有状态文件时为 `win-x64`。lip 无法读取的状态文件会使该命令失败，与 `lip install` 一样。

输出始终是 JSON；没有 `--json` 选项。该命令不做任何修改。

## 示例

查看最新版本，并列出各个版本：

```sh
lip pkg view github.com/LiteLDev/LeviLamina
```

查看指定版本：

```sh
lip pkg view github.com/user/repo@1.0.0
```

查看分支的头：

```sh
lip pkg view github.com/user/repo@branch:main
```
