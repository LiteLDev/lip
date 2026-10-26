# lip view

显示包的清单内容。

包参数可以是完整包规格（例如 `github.com/user/repo@1.0.0`），也可以仅提供包 ID（例如 `github.com/user/repo`）。若只提供包 ID，将自动解析最新版本并显示其清单。

## 概要

```sh
lip view <PACKAGE>
```

## 参数

| 参数 | 说明 |
| --- | --- |
| `<PACKAGE>` | 要查看的包，格式为 `<path>@<version>` 或仅 `<path>`。 |

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

读取包清单（包 git 仓库中该版本标签处的 `tooth.json`），并以 JSON 形式展示。
