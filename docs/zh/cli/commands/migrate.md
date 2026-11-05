# lip migrate

把当前目录下的包仓库从 `tooth.json` 或 LeviLamina 清单迁移到 `manifest.json`。

## 概要

```shell
lip migrate
```

## 说明

请在包仓库的根目录中运行。它会写入一个带有 `"format": "bf45b1"`、以两个空格缩进的 `manifest.json`：

- **从 `tooth.json`**（格式版本 1、2 或 3），当目录中有该文件时：
    - 只有一个不带标签的变体、其资源全部为 `self`、且其 `platform` 不是模式的 `tooth.json`，转换为文件包；其 `platform` 成为 `runtime`。
    - 其他 `tooth.json` 转换为元文档，其 `targets`（无标签变体与 `client` 变体分别成为服务端和客户端 target）各指向一个待发布的 `.mcmod` 文件。下标为 `<i>` 的 target 的包文档写入 `targets/<i>/manifest.json`；你可以用 [`lip pack`](pack.md) 由它生成 `.mcmod` 文件。若 `targets` 已存在，命令会失败，且不做任何修改。
    - `tooth.json` 必须是唯一的清单：若同时存在 `manifest.json`，命令会失败，且不做任何修改。仅在 `manifest.json` 写入成功后删除 `tooth.json`。
- **从 LeviLamina 清单**，其他情况下：不带 `format` 的 `manifest.json` 会被原地改写，加上 `"format": "bf45b1"`。其他键已经是 lip 读取的形式，保持不变；但不是 `"server"` 或 `"client"` 的 `platform`，以及 `dependencies` 和 `conflicts` 中 `name` 不是 id 的条目会被丢弃，其中不是 node-semver 范围的 `version` 会被删除。

当结果所需的某个值缺失或无效时（例如 `version` 是构建占位符，或 `tooth.json` 的平台模式所代表的 RID），lip 会在终端上询问，直到回答有效为止。若标准输入或标准错误不是终端，则命令失败，指明本应询问的值，且不做任何修改。

如果既没有 `tooth.json` 也没有 `manifest.json`、`manifest.json` 已带有 `format` 键、结果是元文档而 `targets` 已存在，或者某个文件无法读取，`lip migrate` 会失败，且不做任何修改。

所有被丢弃或以不同含义转换的内容都会以警告形式输出，并指明其来源字段（`tooth.json` 字段以格式 3 的形式给出）。警告不会导致命令失败。输出只是一个起点，不保证正确：发布前请检查，并提交结果。详情与局限参见[迁移包](../../guides/migrating_packages.md)。
