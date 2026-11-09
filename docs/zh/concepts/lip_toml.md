# lip.toml

lip 包是根部带有**包文件** `lip.toml` 的归档。`lip.toml` 给出包的名称和版本，并针对每个变体、运行端和平台
描述 lip 要安装的内容：依赖、要放置的文件、要安装的 mod 以及要运行的命令。
规范性定义见[包协议](https://github.com/LiteLDev/lip/blob/main/protocol/package.md)。

`lip.toml` 是格式 4 的 [TOML 1.0](https://toml.io/cn/v1.0.0) 文件，由以 TOML 编写的 JSON Schema
[`lip.schema.toml`](https://github.com/LiteLDev/lip/blob/main/protocol/lip.schema.toml)
描述。lip 也可读取 lip v0.x 包的 `tooth.json`（格式 1 到 3），参见[早期格式](#早期格式)。

## 归档

包位于一个**归档**中，归档是一棵根部带有 `lip.toml` 的文件和目录树：

- **git 归档**：git 仓库的一个提交，通常是某个版本标签所指的提交。只有其中的普通文件和目录属于
  该归档（不含符号链接和子模块）；
- **ZIP 归档**：一个 ZIP 文件，可通过 HTTPS 下载，也可位于你的磁盘上。一个文件是否为 ZIP 归档只看
  内容，与文件名无关。其条目必须是普通文件或目录，路径以 `/` 分隔，不得以 `/` 开头，也不得包含
  `..` 段或 `\`；
- **目录归档**：你磁盘上的一个目录，以其内容作为归档。

## 示例

一个 mod，以附加到每个 GitHub Release 的 ZIP 文件发布，支持 Windows x64 上的服务端和客户端：

```toml
format = 4
id = "github.com/example/MyMod"
version = "1.0.0"

[info]
name = "My Mod"
description = "A short description"
tags = ["utility"]

[[variants.""]]
side = "server"
platform = "win-x64"
preserve = ["mods/MyMod/config/"]

[variants."".deps]
"github.com/LiteLDev/LeviLamina" = ">=1.0.0 <2.0.0"
"github.com/example/MyLib" = "^0.3.0"

[[variants."".assets]]
type = "mcmod"
urls = ["https://github.com/example/MyMod/releases/download/v{{version}}/MyMod-server-win-x64.zip"]

[[variants.""]]
side = "client"
platform = "win-x64"
preserve = ["mods/MyMod/config/"]

[variants."".deps]
"github.com/LiteLDev/LeviLamina" = ">=1.0.0 <2.0.0"
"github.com/example/MyLib" = "^0.3.0"

[[variants."".assets]]
type = "mcmod"
urls = ["https://github.com/example/MyMod/releases/download/v{{version}}/MyMod-client-win-x64.zip"]
```

版本和版本范围都是字符串，因此总要加引号。含有 ASCII 字母、数字、`_` 和 `-` 以外字符的键也要加引号，例如 `deps` 中的包 id 或默认变体 `""`：`[[variants.""]]` 为默认变体添加一个条目。

## 字段

`lip.toml` 不得包含日期时间、日期、时间，或浮点数 `inf`、`nan`。此处未描述的键会被忽略。

### 顶层

| 字段 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `format` | `int` | 是 | 必须为 `4`。 |
| `id` | `string` | 是 | **包 ID**，必须是合法的 [Go 模块路径](https://go.dev/ref/mod#module-path)，例如 `github.com/user/repo`。比较时不区分 ASCII 大小写。 |
| `version` | `string` | 是 | 包的版本，不带前导 `v` 的 [SemVer 2.0.0](https://semver.org/) 版本。 |
| `info` | `object` | 否 | 供人阅读的信息。参见 [Info](#info)。 |
| `variants` | `object` | 是 | 把每个变体名映射到其条目。参见[变体](#变体)。 |

对于 git 仓库中的包，`id` 必须是该仓库的路径：`https://github.com/user/repo.git` 中的
`lip.toml` 必须写 `id = "github.com/user/repo"`。在版本标签 `v<version>` 处，`version` 必须
等于标签版本。否则该提交不是包，lip 不会安装它。

### Info

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `name` | `string` | 显示名称。 |
| `description` | `string` | 简短描述。 |
| `tags` | `string[]` | 标签。参见[推荐标签](../guides/creating_packages.md#推荐标签)。 |
| `logo` | `string` | 徽标图片的 URL，必须以 `https://` 开头；其响应（直接或经重定向后）即为该图片。 |

lip 不使用 `info`。

### 变体

`variants` 把每个**变体**名映射到一个非空的**条目**数组。变体 `""` 是默认变体；其他名称须匹配
`^[a-z0-9_]+(/[a-z0-9_]+)?$`，例如 `lua` 或 `quickjs`。用户在来源后加上 `#<variant>` 来选择变体，例如
`lip install github.com/LiteLDev/LegacyScriptEngine#lua`；不带 `#` 的来源选择 `""`。

每个条目恰好适用于一个**运行端**和一个**平台**。lip 解析包时，取所选变体中 `side` 和 `platform`
与工作区[环境](#环境)完全相同的那个条目。没有通配符：支持多个平台的包要为每个平台各写一个条目。
若该变体没有这样的条目，则该包不是该环境下的候选。同一变体中的任意两个条目不得具有相同的
`side` 和 `platform`。

### 条目

| 字段 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `side` | `string` | 是 | `"server"` 或 `"client"`。 |
| `platform` | `string` | 是 | [.NET RID](https://learn.microsoft.com/dotnet/core/rid-catalog)，例如 `win-x64`。 |
| `deps` | `object` | 否 | 参见[依赖](#依赖)。 |
| `assets` | `array` | 否 | 参见[资源](#资源)。 |
| `preserve` | `string[]` | 否 | 需保留文件的 [glob](#glob)：安装时不覆盖已存在的文件，卸载时也不删除它。 |
| `purge` | `string[]` | 否 | 卸载时额外删除的路径的 [glob](#glob)。匹配到的目录会连同其下所有内容一起删除。 |
| `hooks` | `object` | 否 | 参见[钩子](#钩子)。 |

### 依赖

`deps` 把 `<包 ID>` 或 `<包 ID>#<variant>` 映射到版本范围：

```toml
[variants."".deps]
"github.com/LiteLDev/LeviLamina" = ">=1.0.0 <2.0.0"
"github.com/LiteLDev/LegacyScriptEngine#lua" = "^0.9.0"
```

对每个依赖，lip 从该仓库的版本标签中安装一个版本在范围内、且有同一环境条目的版本（及变体）。从 git
引用（例如 `@branch:main`）安装的包，只要版本在范围内，且该仓库有 lip 可为该依赖安装的版本，也能满足对同一包
ID 和变体的依赖。

版本范围采用 [Semver for .NET 3.0.0](https://github.com/WalkerCodeRanger/semver/tree/v3.0.0) 中
`SemVersionRange.Parse` 在默认选项下的语法和语义，例如 `^1.2.0`、`~1.2.0`、`>=1.0.0 <2.0.0`、
`1.2.*` 或 `>=1.0.0 || 0.9.0`。不带运算符的完整版本（如 `1.2.3`）只匹配该版本。

### 资源

`assets` 列出包的文件从哪里来。每个资源都有一个 `type`：

| 类型 | 字段 | 说明 |
| --- | --- | --- |
| `self` | `place`（可选） | `lip.toml` 自身所在的归档，例如标签处的仓库。 |
| `zip` | `urls`、`place`（可选） | 通过 HTTPS 下载的 ZIP 归档。 |
| `mcmod` | `urls` | 通过 HTTPS 下载、包含一个 LeviLamina mod 的 ZIP 归档；它没有 `place`。参见 [Mod](#mod)。 |

`urls` 是同一文件的 HTTPS URL 组成的非空数组，按顺序尝试。一个条目最多有一个 `mcmod` 资源。资源按
顺序写入；两个资源或放置规则写入同一路径时，以后者为准。

### 放置规则

`place` 的每个成员都是一条带有 `src` 和 `dest` 的放置规则，把资源归档中的文件写入工作区。`src` 是归档中的路径，`dest` 是
相对于工作区的路径。两者都以 `/` 分隔，不得以 `/` 或盘符开头，也不得包含 `//`、`..` 段或 `\`。
放置规则的形式由 `src` 决定：

- **glob 形式**：`src` 含有 `*`、`?` 或 `[`。所有与 glob 匹配的文件都写到 `dest`（`""` 或以 `/`
  结尾）之下，并保留其相对于 `src` 中开头那些不含通配符的段的路径。不匹配任何文件的 glob 不写入任何
  内容。
- **目录形式**：`src` 为 `""` 或以 `/` 结尾。`src` 之下的所有文件都写到 `dest`（`""` 或以 `/` 结尾）
  之下，保留其相对于 `src` 的路径。`src` 之下必须至少有一个文件。
- **文件形式**：其他情况。文件 `src` 写入 `dest`，`dest` 不得以 `/` 结尾。该文件必须存在。

```toml
[[variants."".assets]]
type = "zip"
urls = ["https://example.com/tool-1.0.0-win-x64.zip"]
place = [
  { src = "bin/", dest = "tools/" },
  { src = "README.md", dest = "tools/README.md" },
  { src = "plugins/*.dll", dest = "tools/plugins/" },
]
```

包根部的 `lip.toml` 永远不会被写入。

### glob

`preserve`、`purge` 以及 glob 形式的放置规则使用 POSIX `fnmatch` 模式，按段逐一匹配：
`*` 匹配除 `/` 以外的任意字符（包括开头的 `.`），`?` 匹配除 `/` 以外的单个字符，`[...]` 匹配集合中的
单个字符。`**` 段匹配零个或多个段；以 `/` 结尾的模式匹配一个目录及其下的所有内容。没有转义字符。
glob 不得为空、不得以 `/` 开头，也不得包含 `//` 或 `\`。

`preserve` 和 `purge` 匹配的是相对于工作区的路径，对 mod 也是如此：应写
`mods/MyMod/config/`，而不是 `config/`。`purge` 永远不会匹配工作区根目录下的 `mods`
或 `.lip`，也不会匹配 `.lip` 之下的任何内容；`mods` 之下的路径不会仅因 `mods` 匹配而匹配：
`mods/` 和 `*/` 不会删除 `mods` 之下的任何内容。

### 钩子

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `pre_install` | `string[]` | 写入文件之前运行的命令。 |
| `post_install` | `string[]` | 写入文件之后运行的命令。 |
| `pre_uninstall` | `string[]` | 删除文件之前运行的命令。 |
| `post_uninstall` | `string[]` | 删除文件之后运行的命令。 |

命令在工作区根目录中运行，Windows 上使用 `cmd /c`，其他系统上使用 `sh -c`。以非零退出码退出的命令会
使操作失败。操作失败时，lip 会撤销其文件改动，但不会撤销钩子命令已经做过的事，因此钩子命令应当可以
安全地再次运行。

## 模板

与 lip v0.37.0 的 `tooth.json` 一样，`lip.toml` 中除键以外的每个字符串都是
[Scriban 语言](https://github.com/scriban/scriban/blob/7.5.0/site/docs/language.md)的模板。lip 在读取文件之前
先渲染每个模板，因此上文所述的一切，例如版本检查以及 URL 和 glob 的校验，都作用于渲染后的值。变量是按原样书写的
顶层字段：`{{version}}`、`{{id}}`、`{{format}}`、`{{info.name}}`、`{{variants}}`，以及你添加的任何其他顶层键。
变量保存的是字段的原样内容，其中的模板不会先被渲染。

```toml
version = "1.2.3"

[[variants."".assets]]
type = "mcmod"
urls = ["https://github.com/example/MyMod/releases/download/v{{version}}/MyMod-{{version}}.zip"]
```

模板中可以使用任何 Scriban 表达式，例如 `{{version | string.replace "." "_"}}`。未定义的变量渲染为空。
无法解析或渲染失败的模板（例如 `{{ if }}`）会使该归档不是包。要原样写出 `{{`，请写成 `{{ "{{" }}`。
[`lip view`](../cli/commands/view.md) 显示渲染后的包文件，[状态文件](lockfiles.md)中记录的也是渲染后的包文件。

## Mod

带有 `mcmod` 资源的条目是 **mod**；否则该包是**文件包**，只放置文件。

`mcmod` 资源的归档根部必须有该 mod 的 LeviLamina `manifest.json`，它是一个 JSON 对象，其 `name` 须匹配
`^[A-Za-z0-9][A-Za-z0-9._-]*$`。这个 `name` 就是 **mod 名称**。lip 把整个归档按原路径写入
`mods/<mod 名称>/`。

lip 只读取 `manifest.json` 的 `name`，用来命名 mod 目录。它不检查也不修改其中的任何其他内容：
`manifest.json` 与其他文件一样，按归档中的原样写入 mod 目录。让其 `dependencies` 与条目的
`deps` 保持一致由你自己负责。缺少 `manifest.json`，或其中没有有效的 `name`，会在任何修改之前
使安装失败。

两个 mod 名称相同（不区分 ASCII 大小写）的 mod 不能安装在同一个工作区中。

条目在 `mcmod` 资源之外还可以有其他资源，例如一个把文件放到 `mods/` 之外的 `zip` 资源。

## 身份

已安装包的**身份**是其包 ID，当变体不为 `""` 时再加上 `#<variant>`，例如
`github.com/LiteLDev/LegacyScriptEngine#lua`。一个工作区中每个身份至多有一个包；
[`lip list`](../cli/commands/list.md) 显示身份，[`lip uninstall`](../cli/commands/uninstall.md) 也接受
身份。

身份来自包的 `id`，而不是安装来源：`lip.toml` 写有 `id = "github.com/user/repo"` 的 ZIP 文件，
与来自该仓库的包身份相同，因此两者不能同时安装。

## 环境

工作区的环境是一个二元组 `(side, platform)`：

- **运行端**（side）：`server` 或 `client`，即工作区是游戏的哪一端。lip 只管理服务端工作区，因此始终使用
  `server`。客户端工作区由 LeviLauncher 等启动器管理；若工作区的[状态文件](lockfiles.md)记录的是
  `client`，lip 会拒绝处理。
- **平台**（platform）：一个 [.NET RID](https://learn.microsoft.com/dotnet/core/rid-catalog)：`--platform`，
  否则为状态文件记录的平台，否则为 lip 自身运行的平台（例如 `win-x64`）。

## 早期格式

lip v0.39 的 `lip.toml` 同样是格式 4，但把运行端写作 `platform`、把平台写作 `runtime`，例如
`platform = "server"` 和 `runtime = "win-x64"`。lip 不读取这样的条目：含有 `runtime`、或 `platform` 为
`"server"` 或 `"client"` 的条目会使该归档不是包，并给出消息，提示把 `platform` 改名为 `side`、把
`runtime` 改名为 `platform`。把每个条目的这两个键改名即可，其余内容保持不变。

lip v0.37.0 及更早版本的包没有 `lip.toml`，而是有格式版本 1、2 或 3 的 `tooth.json`。lip 仍会安装它们，
但从不写入它们；请使用 [`lip migrate`](../cli/commands/migrate.md) 把你的包转换为 `lip.toml`。带有
`lip.toml` 的归档从 `lip.toml` 读取，不论它是否还有 `tooth.json`；其他格式的 `tooth.json` 不会被读取。

格式 1 和 2 先像 lip v0.37.0 那样读取为格式 3。格式 3 的 `tooth.json` 随后针对工作区的环境被完整地
转换为一个包文件，lip 只使用转换结果：
lip 解析、安装并记录到[状态文件](lockfiles.md)中的都是它。安装时不再保留任何格式 3 的语义。

- `tooth` 变为 `id`。`version` 为包的版本：在版本标签处为标签版本，而不论 `tooth.json` 的 `version` 写的是
  什么；否则为该 `version`。
- 保留 `info`，但其 `avatar_url` 以 `https://` 开头时变为 `logo`，否则被丢弃。
- 对每个标签，将该标签下 `platform` 为空或与环境的平台匹配（`tooth.json` 的 `platform` 是 RID 模式，
  例如 `win-*`）的变体，按 lip v0.37.0 的方式合并为一个对应该环境运行端和平台的条目。
  没有这类变体的标签不构成变体。
- `dependencies` 原样变为 `deps`。
- 每个资源保留其 `type` 和 `urls`，其 `placements` 变为 `place`（见下文）。类型为 `tar`、`tgz` 或
  `uncompressed` 的资源在 `lip.toml` 中不存在，会使该标签在该环境下没有变体。
- `preserve_files` 变为 `preserve`：`\` 替换为 `/`，不含 `/` 的模式加上前缀 `**/`，因此 `config.json`
  变为 `**/config.json`。
- `remove_files` 变为 `purge`：`\` 替换为 `/`，不以 `/` 结尾的模式还会再加上一个以 `/` 结尾的形式，因此
  `logs` 变为 `logs` 和 `logs/`。
- scripts 变为 `hooks`：`pre_install` 仍为 `pre_install`；`install` 和随后的 `post_install` 变为
  `post_install`；`pre_uninstall` 和随后的 `uninstall` 变为 `pre_uninstall`；`post_uninstall` 仍为
  `post_uninstall`。

放置规则 `{ "type": ..., "src": ..., "dest": ... }` 在 `src` 和 `dest` 中的 `\` 替换为 `/`、空段和 `.` 段被移除
后，变为 `place` 的一个成员：

| 放置规则 | `place` 成员 |
| --- | --- |
| `type` 为 `dir` | `src` 和 `dest` 各自加上 `/`（为空时除外）：目录形式。 |
| `type` 为 `file`，`src` 含 `*`、`?` 或 `[` | `src`，以及加上 `/` 的 `dest`（为空时除外）：glob 形式。 |
| `type` 为 `file`，其他 `src` | `src` 和 `dest`：文件形式。 |

转换后在包文件中无效的 glob 或放置规则会被略去。随后放置规则按 `lip.toml` 的方式执行：例如，缺失的文件现在会使安装
失败，包的 `tooth.json` 也永远不会被写入。
早期格式的包始终是文件包。其身份遵循同样的规则，例如 `github.com/LiteLDev/LegacyScriptEngine#lua`。

格式 3 的 schema 为
[`legacy/v3/tooth.schema.yaml`](https://github.com/LiteLDev/lip/blob/1e023e6/specs/schemas/legacy/v3/tooth.schema.yaml)
（格式 1 和 2：[`legacy/v1/tooth.schema.yaml`](https://github.com/LiteLDev/lip/blob/1e023e6/specs/schemas/legacy/v1/tooth.schema.yaml)、[`legacy/v2/tooth.schema.yaml`](https://github.com/LiteLDev/lip/blob/1e023e6/specs/schemas/legacy/v2/tooth.schema.yaml)），
lip v0.37.0 在其[包清单](https://github.com/LiteLDev/lip/blob/v0.37.0/docs/concepts/package_manifest.md)页面
中对其做了说明：

```json
{
  "format_version": 3,
  "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
  "tooth": "github.com/user/repo",
  "version": "1.0.0",
  "info": { "name": "My Package", "description": "A short description" },
  "variants": [
    {
      "label": "",
      "platform": "win-x64",
      "dependencies": { "github.com/other/package": ">=1.0.0" },
      "assets": [
        {
          "type": "self",
          "placements": [{ "type": "dir", "src": "bin/", "dest": "bin/" }]
        }
      ],
      "preserve_files": ["config/*.json"],
      "remove_files": ["temp/"],
      "scripts": { "post_install": ["echo installed"] }
    }
  ]
}
```
