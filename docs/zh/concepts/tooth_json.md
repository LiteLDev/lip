# tooth.json

lip 包是根部带有 `tooth.json` 的归档。`tooth.json` 给出包的名称和版本，并针对每个变体、平台和运行时
描述 lip 要安装的内容：依赖、要放置的文件、要安装的 mod 以及要运行的命令。
规范性定义见[包协议](https://github.com/LiteLDev/lip/blob/main/protocol/package.md)。

当前格式为格式版本 4，由 JSON Schema
[`tooth.schema.json`](https://github.com/LiteLDev/lip/blob/main/protocol/tooth.schema.json)
描述。lip 也可读取格式版本 1 到 3 的 `tooth.json`，参见[早期格式](#早期格式)。

## 归档

包位于一个**归档**中，归档是一棵根部带有 `tooth.json` 的文件和目录树：

- **git 归档**：git 仓库的一个提交，通常是某个版本标签所指的提交。只有其中的普通文件和目录属于
  该归档（不含符号链接和子模块）；
- **ZIP 归档**：一个 ZIP 文件，可通过 HTTPS 下载，也可位于你的磁盘上。一个文件是否为 ZIP 归档只看
  内容，与文件名无关。其条目必须是普通文件或目录，路径以 `/` 分隔，不得以 `/` 开头，也不得包含
  `..` 或 `\`；
- **目录归档**：你磁盘上的一个目录，以其内容作为归档。

## 示例

一个 mod，以附加到每个 GitHub Release 的 ZIP 文件发布，支持 Windows x64 上的服务端和客户端：

```json
{
  "format_version": 4,
  "format_uuid": "f74a5fda-cb2a-4de8-959f-a1a76bad3102",
  "tooth": "github.com/example/MyMod",
  "version": "1.0.0",
  "info": {
    "name": "My Mod",
    "description": "A short description",
    "tags": ["levilamina", "mod"]
  },
  "variants": {
    "": [
      {
        "platform": "server",
        "runtime": "win-x64",
        "dependencies": {
          "github.com/LiteLDev/LeviLamina": ">=1.0.0 <2.0.0",
          "github.com/example/MyLib": "^0.3.0"
        },
        "assets": [
          {
            "type": "mcmod",
            "urls": ["https://github.com/example/MyMod/releases/download/v1.0.0/MyMod-server-win-x64.zip"]
          }
        ],
        "preserve_files": ["mods/MyMod/config/"]
      },
      {
        "platform": "client",
        "runtime": "win-x64",
        "dependencies": {
          "github.com/LiteLDev/LeviLamina": ">=1.0.0 <2.0.0",
          "github.com/example/MyLib": "^0.3.0"
        },
        "assets": [
          {
            "type": "mcmod",
            "urls": ["https://github.com/example/MyMod/releases/download/v1.0.0/MyMod-client-win-x64.zip"]
          }
        ],
        "preserve_files": ["mods/MyMod/config/"]
      }
    ]
  }
}
```

## 字段

`tooth.json` 必须是一个 JSON 对象。此处未描述的键会被忽略。

### 顶层

| 字段 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `format_version` | `int` | 是 | 必须为 `4`。 |
| `format_uuid` | `string` | 是 | 必须为 `"f74a5fda-cb2a-4de8-959f-a1a76bad3102"`。 |
| `tooth` | `string` | 是 | 包的 **tooth 路径**，必须是合法的 [Go 模块路径](https://go.dev/ref/mod#module-path)，例如 `github.com/user/repo`。比较时不区分 ASCII 大小写。 |
| `version` | `string` | 是 | 包的版本，不带前导 `v` 的 [SemVer 2.0.0](https://semver.org/) 版本。 |
| `info` | `object` | 否 | 供人阅读的信息。参见 [Info](#info)。 |
| `variants` | `object` | 是 | 把每个变体名映射到其条目。参见[变体](#变体)。 |

对于 git 仓库中的包，`tooth` 必须是该仓库的路径：`https://github.com/user/repo.git` 中的
`tooth.json` 必须写 `"tooth": "github.com/user/repo"`。在版本标签 `v<version>` 处，`version` 必须
等于标签版本。否则该提交不是包，lip 不会安装它。

### Info

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `name` | `string` | 显示名称。 |
| `description` | `string` | 简短描述。 |
| `tags` | `string[]` | 标签。 |
| `avatar_url` | `string` | 头像图片 URL。 |

lip 不使用 `info`。

### 变体

`variants` 把每个**变体**名映射到一个非空的**条目**数组。变体 `""` 是默认变体；其他名称须匹配
`^[a-z0-9_]+(/[a-z0-9_]+)?$`，例如 `lua` 或 `quickjs`。用户在来源后加上 `#<variant>` 来选择变体，例如
`lip install github.com/LiteLDev/LegacyScriptEngine#lua`；不带 `#` 的来源选择 `""`。

每个条目恰好适用于一个**平台**和一个**运行时**。lip 解析包时，取所选变体中 `platform` 和 `runtime`
与工作区[环境](#环境)完全相同的那个条目。没有通配符：支持多个运行时的包要为每个运行时各写一个条目。
若该变体没有这样的条目，则该包不是该环境下的候选。同一变体中的任意两个条目不得具有相同的
`platform` 和 `runtime`。

### 条目

| 字段 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `platform` | `string` | 是 | `"server"` 或 `"client"`。 |
| `runtime` | `string` | 是 | [.NET RID](https://learn.microsoft.com/dotnet/core/rid-catalog)，例如 `win-x64`。 |
| `dependencies` | `object` | 否 | 参见[依赖](#依赖)。 |
| `assets` | `array` | 否 | 参见[资源](#资源)。 |
| `preserve_files` | `string[]` | 否 | 需保留文件的 [glob](#glob)：安装时不覆盖已存在的文件，卸载时也不删除它。 |
| `remove_files` | `string[]` | 否 | 卸载时额外删除的路径的 [glob](#glob)。匹配到的目录会连同其下所有内容一起删除。 |
| `hooks` | `object` | 否 | 参见[钩子](#钩子)。 |

### 依赖

`dependencies` 把 `<tooth 路径>` 或 `<tooth 路径>#<variant>` 映射到版本范围：

```json
{
  "dependencies": {
    "github.com/LiteLDev/LeviLamina": ">=1.0.0 <2.0.0",
    "github.com/LiteLDev/LegacyScriptEngine#lua": "^0.9.0"
  }
}
```

对每个依赖，lip 从该仓库的版本标签中安装一个版本在范围内、且有同一环境条目的版本（及变体）。从 git
引用（例如 `@branch:main`）安装的包，只要版本在范围内，也能满足对同一路径和变体的依赖。

版本范围采用 [Semver for .NET 3.0.0](https://github.com/WalkerCodeRanger/semver/tree/v3.0.0) 中
`SemVersionRange.Parse` 在默认选项下的语法和语义，例如 `^1.2.0`、`~1.2.0`、`>=1.0.0 <2.0.0`、
`1.2.*` 或 `>=1.0.0 || 0.9.0`。不带运算符的完整版本（如 `1.2.3`）只匹配该版本。

### 资源

`assets` 列出包的文件从哪里来。每个资源都有一个 `type`：

| 类型 | 字段 | 说明 |
| --- | --- | --- |
| `self` | `placements`（可选） | `tooth.json` 自身所在的归档，例如标签处的仓库。 |
| `zip` | `urls`、`placements`（可选） | 通过 HTTPS 下载的 ZIP 归档。 |
| `mcmod` | `urls` | 通过 HTTPS 下载、包含一个 LeviLamina mod 的 ZIP 归档；它没有 `placements`。参见 [Mod](#mod)。 |

`urls` 是同一文件的 HTTPS URL 组成的非空数组，按顺序尝试。一个条目最多有一个 `mcmod` 资源。资源按
顺序写入；两个资源或放置规则写入同一路径时，以后者为准。

### 放置规则

放置规则 `{ "src": ..., "dest": ... }` 把资源归档中的文件写入工作区。`src` 是归档中的路径，`dest` 是
相对于工作区的路径。两者都以 `/` 分隔，不得以 `/` 或盘符开头，也不得包含 `//`、`..` 段或 `\`。
放置规则的形式由 `src` 决定：

- **glob 形式**：`src` 含有 `*`、`?` 或 `[`。所有与 glob 匹配的文件都写到 `dest`（`""` 或以 `/`
  结尾）之下，并保留其相对于 `src` 中开头那些不含通配符的段的路径。不匹配任何文件的 glob 不写入任何
  内容。
- **目录形式**：`src` 为 `""` 或以 `/` 结尾。`src` 之下的所有文件都写到 `dest`（`""` 或以 `/` 结尾）
  之下，保留其相对于 `src` 的路径。`src` 之下必须至少有一个文件。
- **文件形式**：其他情况。文件 `src` 写入 `dest`，`dest` 不得以 `/` 结尾。该文件必须存在。

```json
{
  "type": "zip",
  "urls": ["https://example.com/tool-1.0.0-win-x64.zip"],
  "placements": [
    { "src": "bin/", "dest": "tools/" },
    { "src": "README.md", "dest": "tools/README.md" },
    { "src": "plugins/*.dll", "dest": "tools/plugins/" }
  ]
}
```

包根部的 `tooth.json` 永远不会被写入。

### glob

`preserve_files`、`remove_files` 以及 glob 形式的放置规则使用 POSIX `fnmatch` 模式，按段逐一匹配：
`*` 匹配除 `/` 以外的任意字符（包括开头的 `.`），`?` 匹配除 `/` 以外的单个字符，`[...]` 匹配集合中的
单个字符。`**` 段匹配零个或多个段；以 `/` 结尾的模式匹配一个目录及其下的所有内容。没有转义字符。
glob 不得为空、不得以 `/` 开头，也不得包含 `//` 或 `\`。

`preserve_files` 和 `remove_files` 匹配的是相对于工作区的路径，对 mod 也是如此：应写
`mods/MyMod/config/`，而不是 `config/`。`remove_files` 永远不会匹配工作区根目录下的 `mods`、`plugins`
或 `.lip`，也不会匹配 `.lip` 之下的任何内容，且永远不会通过 `mods` 或 `plugins` 删除其下的任何内容。

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

## Mod

带有 `mcmod` 资源的条目是 **mod**；否则该包是**文件包**，只放置文件。

`mcmod` 资源的归档根部必须有该 mod 的 LeviLamina `manifest.json`，它是一个 JSON 对象，其 `name` 须匹配
`^[A-Za-z0-9][A-Za-z0-9._-]*$`。这个 `name` 就是 **mod 名称**。lip 把整个归档按原路径写入
`mods/<mod 名称>/`。在服务端，`plugins` 是指向 `mods` 的链接，因此也能在 `plugins/<mod 名称>/` 下找到该
mod。

lip 会对照条目的 `dependencies` 检查 mod 的 `manifest.json` 中的 `dependencies`：设 D 为
`[{ "name": N }, ...]`，按条目依赖的顺序，为条目中每个由已安装 mod 满足的依赖各含一个成员，N 为该 mod
的名称。

- 若 `manifest.json` 有 `dependencies`，它必须恰好等于 D，否则安装失败。
- 若没有，lip 会在 mod 目录中写入 `dependencies` 设为 D 的 `manifest.json`。

对文件包的依赖不会出现在 D 中。两个 mod 名称相同（不区分 ASCII 大小写）的 mod 不能安装在同一个工作区
中。

条目在 `mcmod` 资源之外还可以有其他资源，例如一个把文件放到 `mods/` 之外的 `zip` 资源。

## 身份

已安装包的**身份**是其 tooth 路径，当变体不为 `""` 时再加上 `#<variant>`，例如
`github.com/LiteLDev/LegacyScriptEngine#lua`。一个工作区中每个身份至多有一个包；
[`lip list`](../cli/commands/list.md) 显示身份，[`lip uninstall`](../cli/commands/uninstall.md) 也接受
身份。

身份来自包的 `tooth`，而不是安装来源：`tooth.json` 写有 `"tooth": "github.com/user/repo"` 的 ZIP 文件，
与来自该仓库的包身份相同，因此两者不能同时安装。

## 环境

工作区的环境是一个二元组 `(platform, runtime)`：

- **platform**：`server` 或 `client`。lip 只管理服务端工作区，因此始终使用 `server`。客户端工作区由
  LeviLauncher 等启动器管理；若工作区的[工作区文件](lockfiles.md)记录的是 `client`，lip 会拒绝处理。
- **runtime**：`--rid`，否则为工作区文件记录的运行时，否则为 lip 自身运行的 RID（例如 `win-x64`）。

## 早期格式

lip 读取格式版本 1、2 和 3 的 `tooth.json`（为 lip v0.37.0 及更早版本编写），但从不写入它们；请使用
[`lip migrate`](../cli/commands/migrate.md) 转换你的包。格式 1 和 2 先像 lip v0.37.0 那样读取为格式 3。
格式 3 的 `tooth.json` 随后针对工作区的环境读取为格式 4 的 `tooth.json`：

- 保留 `tooth`、`version` 和 `info`。
- 对每个标签，将该标签下 `platform` 为空或与运行时匹配（作为 RID 模式，例如 `win-*`）的变体，按 lip
  v0.37.0 的方式合并为一个对应该环境平台和运行时的条目。没有这类变体的标签不构成变体。
- 在客户端上，标签为 `client` 的变体被用作变体 `""`。
- scripts 变为 `hooks`：`pre_install` 仍为 `pre_install`；`install` 和随后的 `post_install` 变为
  `post_install`；`pre_uninstall` 和随后的 `uninstall` 变为 `pre_uninstall`；`post_uninstall` 仍为
  `post_uninstall`。
- 资源、`preserve_files` 和 `remove_files` 保持其在 lip v0.37.0 中的含义，包括 `tar`、`tgz` 和
  `uncompressed` 资源类型以及带 `type` 的放置规则。
- 在版本标签处，包的版本为标签版本，而不论其 `version` 写的是什么。
- 对某个格式 4 包的 `client` 变体的依赖，指向该包的变体 `""`。

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
