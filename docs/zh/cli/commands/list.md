# lip list

列出已安装包。

## 概要

```shell
lip list [options]
```

## 选项

| 选项 | 说明 |
| --- | --- |
| `--json` | 以 JSON 格式输出。 |

## 说明

读取该工作区的[锁文件](../../concepts/lockfiles.md)（或早期版本留在工作区中的 `lip_lock.json` 或旧的 `tooth_lock.json`），按两组以 `<name>@<version>` 形式显示已安装包：

- **User-Requested**：用户显式安装的包。
- **Dependencies**：作为依赖安装的包。

包按安装顺序列出，依赖在前。名称即包的身份：其 `manifest.json` 的 `name`，或 `tooth.json` 包的 `tooth` 路径。
