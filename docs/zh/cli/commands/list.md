# lip list

列出已安装的包。

## 概要

```shell
lip list [options]
```

## 选项

| 选项 | 说明 |
| --- | --- |
| `--json` | 以 JSON 格式输出。 |

## 说明

读取该工作区的[状态文件](../../concepts/lockfiles.md)，并以 `<身份>@<版本>` 的形式分两组显示已安装的包：

- **User-Requested** — 用户显式安装的包。
- **Dependencies** — 作为依赖安装的包。

包按安装顺序列出，依赖在前。包的身份是其包 ID，变体不为 `""` 时再加上 `#<variant>`（例如 `github.com/LiteLDev/LegacyScriptEngine#lua`）。

使用 `--json` 时，结果为：

```json
{ "ExplicitInstalled": ["<identity>@<version>", ...], "ImplicitInstalled": ["<identity>@<version>", ...] }
```
