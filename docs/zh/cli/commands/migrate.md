# lip migrate

将 `tooth.json` 转换为 `manifest.json`。

## 概要

```shell
lip migrate <FILE> <OUTPUT>
```

## 参数

| 参数 | 说明 |
| --- | --- |
| `<FILE>` | 要转换的 `tooth.json`（格式版本 1、2 或 3）。 |
| `<OUTPUT>` | 要写入的 `manifest.json` 的路径。 |

## 说明

读取 `tooth.json`，并在格式允许的范围内写出等价的第 4 版 `manifest.json`：

- 只有一个不带标签的变体、且其资源全部为 `self` 的 `tooth.json`，转换为文件包（`"type": "files"`）。
- 其他 `tooth.json` 转换为路由文档，每个变体对应一个条目（无标签变体与 `client` 变体分别成为服务端和客户端条目）。你仍需为每个变体发布具体包。

所有无法转换或需要你决定的事项都会以警告形式输出。输出只是一个起点，不保证正确：发布前请检查。详情与局限参见[迁移包](../../guides/migrating_packages.md)。
