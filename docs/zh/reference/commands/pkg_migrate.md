# lip pkg migrate

把当前目录中 lip v0.x 的 `tooth.json` 迁移为 `lip.toml`。

## 概要

```
lip pkg migrate [-h | --help]
```

## 说明

`lip pkg migrate` 根据当前工作目录中的 `tooth.json` 转换出一个[包文件](../../concepts/lip_toml.md)，写为那里的 `lip.toml`，然后删除 `tooth.json`。请在包仓库的根目录中运行它。

下列情况下它失败，并且不做任何更改：

- `lip.toml` 已经存在；
- `tooth.json` 不存在；或者
- `tooth.json` 不是 lip 能读取的格式版本 1、2 或 3。

转换的内容：

- `tooth` 变为 `id`。`version` 和 `info` 照抄；`info.avatar_url` 以 `https://` 开头时变为 `info.logo`，否则丢弃。
- 每个标签变为一个变体。对它适用的每个平台（RID），该标签下的变体按 lip v0.37.0 的方式合并：合并后依赖 `github.com/LiteLDev/LeviLamina#client` 时得到一个客户端条目，依赖 `github.com/LiteLDev/LeviLamina` 的其他变体时得到一个服务端条目，不依赖 LeviLamina 时每个运行端各得到一个条目。
- 每个条目的转换方式与 lip 读取[早期格式](../../concepts/lip_toml.md#早期格式)的 `tooth.json` 相同：`dependencies` 变为 `deps`；资源保留 `type` 和 `urls`，其 `placements` 变为 `place`；`preserve_files` 变为 `preserve`；`remove_files` 变为 `purge`；脚本变为 `hooks`。在 `lip.toml` 中无效的放置规则和模式会被略去。
- 如果某个平台合并后的变体含有 `tar`、`tgz` 或 `uncompressed` 类型的资源，该平台不会得到条目：`lip.toml` 没有这些资源类型。
- [模板](../../concepts/lip_toml.md#模板)会保留。像 `https://example.com/v{{version}}/tool.zip` 这样的值（格式 1 和 2 中是 `$(version)`）会写成同样的模板，其中 `{{tooth}}`（格式 1 和 2 中是 `$(path)`）改名为 `{{id}}`。在 `lip.toml` 中会渲染成另一个值的模板，则写成它渲染后的值。

### 询问

`lip.toml` 没有平台模式：每个条目只指明一个平台。当结果需要的某个值缺失或无效时，lip 会在终端上询问，直到回答有效为止。它会询问：

- `win-*` 这类模式所代表的平台（RID）；
- 不是 RID 的 `platform`（如 `windows/amd64`）所代表的平台；
- 没有 `platform` 的变体适用于哪些平台；以及
- 不是有效范围的依赖范围。

标准输入或标准错误不是终端时，命令会失败，指出它本来要询问的值，并且不做任何更改。

### 警告

所有被丢弃的内容，以及转换后含义有变的内容，都会以警告的形式输出，并指明它来自哪个字段（按格式 3 的写法）。警告不会让命令失败。

转换结果只是一个起点。发布之前请检查它；[迁移包](../../authors/migrating_packages.md)列出了需要检查的地方。

`lip pkg migrate` 不是工作区操作。

## 选项

| 选项 | 说明 |
| --- | --- |
| `-h`、`--help` | 输出帮助并退出。 |

## 输出

`lip pkg migrate` 没有结果：它不向标准输出写任何内容。提问、警告和最后一行都是写到标准错误的消息。

## 退出状态

| 退出码 | 含义 |
| --- | --- |
| `0` | `lip.toml` 已写入，`tooth.json` 已删除，无论有没有警告。 |
| 非 `0` | 用法错误；`lip.toml` 已存在；`tooth.json` 不存在或无法读取；或 lip 无法询问它需要的值。没有任何更改。 |

## 示例

```shell
cd MyTool
lip pkg migrate
```

## 另请参阅

- [迁移包](../../authors/migrating_packages.md)
- [工具](../../interface/tools.md)：界面中的**迁移 tooth.json**。
- [lip.toml](../../concepts/lip_toml.md#早期格式)
