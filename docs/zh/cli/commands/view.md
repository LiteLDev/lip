# lip view

显示包的包文件（`lip.toml`）。

包参数支持与 [`lip install`](install.md#参数) 相同的形式。对于远程仓库，显示最新版本、通过 `@<version>` 显示指定版本（例如 `github.com/user/repo@1.0.0`），或显示 git 引用所指的提交（例如 `github.com/user/repo@branch:main`）。本地 ZIP 文件或目录，或 URL，则按原样显示。

## 概要

```sh
lip view <PACKAGE>
```

## 参数

| 参数 | 说明 |
| --- | --- |
| `<PACKAGE>` | 要查看的包，例如 `<path>`、`<path>@<version>`、`<path>@branch:<branch>`、`./package.zip`、`./MyTool` 或 `https://` URL。 |

## 示例

查看指定版本：

```sh
lip view github.com/user/repo@1.0.0
```

查看最新版本：

```sh
lip view github.com/user/repo
```

查看分支的头：

```sh
lip view github.com/user/repo@branch:main
```

## 说明

读取参数所指归档根部的 `lip.toml`，并以 JSON 形式输出。对于远程仓库，归档是最新版本标签处、指定版本的标签处，或 git 引用所指提交处的仓库。该命令不做任何修改。

[早期格式](../../concepts/lip_toml.md#早期格式)的 `tooth.json` 输出为 lip 从中读取的包文件，所用环境为该工作区的环境：平台 `server`，以及[状态文件](../../concepts/lockfiles.md)记录的 RID，否则为 lip 运行的 RID。
