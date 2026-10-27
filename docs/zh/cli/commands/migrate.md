# lip migrate

把当前目录下的包仓库从 `tooth.json` 迁移到 `manifest.json`。

## 概要

```shell
lip migrate
```

## 说明

请在包仓库的根目录（即包含 `tooth.json`（格式版本 1、2 或 3）的目录）中运行。它会：

1. 在格式允许的范围内把 `tooth.json` 转换为第 4 版 `manifest.json`：
    - 只有一个不带标签的变体、且其资源全部为 `self` 的 `tooth.json`，转换为文件包（`"type": "files"`）。
    - 其他 `tooth.json` 转换为路由文档，每个变体对应一个条目（无标签变体与 `client` 变体分别成为服务端和客户端条目）。你仍需为每个变体发布具体包。
2. 写入 `manifest.json`。如果仓库中已有 `manifest.json`（例如你的 mod 的 LeviLamina 清单），转换得到的键会合并进去：它还没有的键（包括格式标记）会按该文件自身的缩进追加到末尾；它已有的键绝不会被覆盖，若其值与转换得到的值不同，lip 会对每个这样的键给出警告。文件的其余内容保持不变。
3. 仅在 `manifest.json` 写入成功后删除 `tooth.json`。

如果当前目录中没有 `tooth.json`，或者 `tooth.json` 或已有的 `manifest.json` 无法读取，`lip migrate` 会失败，且不做任何修改。

所有无法用 `manifest.json` 表达或需要你决定的事项都会以警告形式输出，并指明对应的 `tooth.json` 字段（以格式 3 的形式）。警告不会导致命令失败。输出只是一个起点，不保证正确：发布前请检查，并提交结果。详情与局限参见[迁移包](../../guides/migrating_packages.md)。
