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
    - 其他 `tooth.json` 转换为元文档，其 `targets` 承载各变体的内容（无标签变体与 `client` 变体分别成为服务端和客户端 target）。每个 target 指向一个待发布的 `.mcmod` 文件，你可以用 [`lip pack`](pack.md) 生成它。
    - `tooth.json` 必须是唯一的清单：若同时存在 `manifest.json`，命令会失败，且不做任何修改。仅在 `manifest.json` 写入成功后删除 `tooth.json`。
- **从 LeviLamina 清单**，其他情况下：不带 `format` 的 `manifest.json` 会被原地改写为一个 mod。`name` 成为 `id`，`type` 和 `entry` 成为 `mod.loader` 和 `mod.entry`，`description` 和 `author` 移入 `info`，`dependencies` 和 `conflicts` 成为从 id 到范围的映射，`optionalDependencies`、`loadAfter` 和 `loadBefore` 成为 `mod.loadAfter` 和 `mod.loadBefore`。名称不是 id 的条目会被丢弃，不是 node-semver 范围的 `version` 变为 `"*"`。其他键移入 `mod.extra`。

当结果所需的某个值缺失或无效时（例如 `version` 是构建占位符，或 `tooth.json` 的平台模式所代表的 RID），lip 会在终端上询问，直到回答有效为止。若标准输入或标准错误不是终端，则命令失败，指明本应询问的值，且不做任何修改。

如果既没有 `tooth.json` 也没有 `manifest.json`、`manifest.json` 已带有 `format` 键，或者某个文件无法读取，`lip migrate` 会失败，且不做任何修改。

所有被丢弃或以不同含义转换的内容都会以警告形式输出，并指明其来源字段（`tooth.json` 字段以格式 3 的形式给出）。警告不会导致命令失败。输出只是一个起点，不保证正确：发布前请检查，并提交结果。详情与局限参见[迁移包](../../guides/migrating_packages.md)。
