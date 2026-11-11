# lip pkg migrate

把当前目录中 lip v0.x 的 `tooth.json` 迁移为 `lip.toml`。

## 概要

```shell
lip pkg migrate
```

## 说明

请在包仓库的根目录中运行。它写入一个由该处格式版本为 1、2 或 3 的 `tooth.json` 转换而来的 `lip.toml`（参见 [lip.toml](../../concepts/lip_toml.md)），然后删除 `tooth.json`：

- `tooth` 变为 `id`，`version` 和 `info` 原样复制；以 `https://` 开头的 `info.avatar_url` 变为 `info.logo`，否则被丢弃。
- 每个标签变为一个变体。对其适用的每个平台（RID），该标签下的变体按 lip v0.37.0 的方式合并，合并后的变体依赖 `github.com/LiteLDev/LeviLamina#client` 时变为一个客户端条目，依赖 `github.com/LiteLDev/LeviLamina` 的其他变体时变为一个服务端条目，不依赖 LeviLamina 时变为两个条目，每个运行端各一个。
- 每个条目的转换方式与 lip 读取[早期格式](../../concepts/lip_toml.md#早期格式)的 `tooth.json` 完全相同：`dependencies` 变为 `deps`；资源保留其 `type` 和 `urls`，其 `placements` 变为 `place`；`preserve_files` 变为 `preserve`（不含 `/` 的模式加上前缀 `**/`），`remove_files` 变为 `purge`（不以 `/` 结尾的模式还会再加上以 `/` 结尾的形式）；scripts 变为 `hooks`。在 `lip.toml` 中无效的放置规则和模式会被略去。
- 合并后的变体含有类型为 `tar`、`tgz` 或 `uncompressed` 的资源的平台不会得到条目，因为 `lip.toml` 没有这些资源类型。
- [模板](../../concepts/lip_toml.md#模板)会被保留：`https://example.com/v{{version}}/tool.zip` 这样的值（格式 1 和 2 中的 `$(version)`）在 `lip.toml` 中写为同样的模板，其中 `{{tooth}}`（格式 1 和 2 中的 `$(path)`）改名为 `{{id}}`。在 `lip.toml` 中会渲染出不同值的模板（例如 `{{format_version}}`）写为其渲染后的值。

`lip.toml` 没有 RID 模式。当结果需要的某个值缺失或无效时，例如 `tooth.json` 中 `win-*` 这样的 `platform` 模式或 `windows/amd64` 这样不是 RID 的 `platform` 所代表的平台（RID），或没有 `platform` 的变体所适用的平台，lip 会在终端上询问，直到回答有效为止。若标准输入或标准错误不是终端，则会失败，指明本应询问的值，且不做任何修改。

若 `lip.toml` 已存在，或 `tooth.json` 不存在或不是 lip 能读取的格式版本 1、2 或 3 的 `tooth.json`，`lip pkg migrate` 会失败，且不做任何修改。

所有被丢弃或以不同含义转换的内容都会以警告形式输出，并指明其来源字段（按格式 3 的形式）。警告不会导致命令失败。输出只是一个起点，并不保证正确：发布前请检查它，然后提交结果。细节与局限请参阅[迁移包](../../guides/migrating_packages.md)。
