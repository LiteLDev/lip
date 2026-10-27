# lip view

显示包的清单内容。

包参数支持与 [`lip install`](install.md#参数) 相同的形式。对于包路径，显示最新版本，或通过 `@<version>` 显示指定版本（例如 `github.com/user/repo@1.0.0`）。本地目录、本地归档或 URL 则按原样显示。

## 概要

```sh
lip view <PACKAGE>
```

## 参数

| 参数 | 说明 |
| --- | --- |
| `<PACKAGE>` | 要查看的包，例如 `<path>`、`<path>@<version>`、`./package.mcmod` 或 `https://` URL。 |

## 示例

查看指定版本：

```sh
lip view github.com/user/repo@1.0.0
```

查看最新版本：

```sh
lip view github.com/user/repo
```

## 说明

读取参数所指归档的根文档，并以 JSON 形式展示：归档中有 `tooth.json` 时显示它，否则显示 `manifest.json`。对于包路径，归档是该版本标签处的仓库。文档按原样显示，不会跟随路由文档。
