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

会按两组显示已安装包：

- **User-Requested**：用户显式安装的包。
- **Dependencies**：作为依赖安装的包。

包会按依赖关系进行拓扑排序。
