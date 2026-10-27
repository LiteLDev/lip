# lip versions

显示某个包的可用版本。

版本来自包仓库（`https://<包路径>.git`）的 git 标签。只有名称恰为 `v<SemVer 2.0.0>`（例如 `v1.2.3`）的标签才是版本；`v1.2` 这样的标签会被忽略。版本从低到高列出，不带前导 `v`。

## 概要

```sh
lip versions <PACKAGE> [options]
```

## 参数

| 参数 | 说明 |
| --- | --- |
| `<PACKAGE>` | 要查看版本的包，格式为 `<path>`（例如 `github.com/user/repo`）。 |

## 选项

| 选项 | 说明 |
| --- | --- |
| `--json` | 以 JSON 格式输出。 |

## 示例

列出包的可用版本：

```sh
lip versions github.com/user/repo
```
