# 包清单（Package Manifest）

lip 包由其归档根部的 `manifest.json` 描述，并以 `"format": "bf45b1"` 标记。同一份文档也由
LeviLamina 读取：LeviLamina 加载其描述的 mod，lip 负责安装它们。
规范性定义见[包协议](https://github.com/LiteLDev/lip/blob/main/protocol/package.md)。

该格式由 JSON Schema
[`manifest.schema.json`](https://github.com/LiteLDev/lip/blob/main/protocol/manifest.schema.json)
描述。lip 还可读取不带 `format` 的 LeviLamina 清单和 lip v0.x 的 `tooth.json`，参见[旧格式](#旧格式)。

## 归档

包位于一个**归档**中，归档是一棵根部带有 `manifest.json` 的文件树：

- **git 归档**：git 仓库的一个提交，通常是某个版本标签所指的提交；
- **ZIP 归档**：一个 ZIP 文件。扩展名为 `.mcmod` 的 ZIP 归档称为 **mcmod**。一个文件是否为
  ZIP 归档只看内容，与扩展名无关；
- **目录归档**：另一个归档内的一个目录（以 `/` 结尾的相对引用）。

单独的本地目录不是归档：要从磁盘安装包，请先用 [`lip pack`](../cli/commands/pack.md) 把它打包为
`.mcmod` 文件。

`manifest.json` 必须是 UTF-8 编码的严格 JSON（不允许注释和重复键），顶层必须是对象。

## 示例

一个 mod，位于 `LegacyMoney-server-win-x64.mcmod` 的根部：

```json
{
  "format": "bf45b1",
  "name": "LegacyMoney",
  "version": "0.22.0",
  "platform": "server",
  "runtime": "win-x64",
  "entry": "LegacyMoney.dll",
  "type": "native",
  "author": "LiteLDev",
  "dependencies": [
    { "name": "LeviLamina", "version": ">=26.51.0 <26.52.0", "source": "github.com/LiteLDev/LeviLamina" },
    { "name": "iListenAttentively", "version": "^0.17.0", "source": "github.com/MiracleForest/iListenAttentively" }
  ],
  "preserve": ["config/", "data/"]
}
```

`author` 不是 lip 的键：lip 忽略它，由 LeviLamina 读取。

## 包文档与元文档

每个文档都带有键 `"format": "bf45b1"`。不带 `format` 的 `manifest.json` 是
[LeviLamina 清单](#levilamina-清单)；`format` 为其他任何值时，文档无效。lip 不认识的键会被忽略。

含有 `targets` 键的文档是**元文档**。它本身不安装任何东西，而是根据请求的变体和环境指向其他
归档。

不含 `targets` 的文档是**包文档**，其归档称为**包**。文档带有 `entry` 键的包是 **mod**，否则是
**文件包**。包的 `name` 即其身份；比较身份时不区分 ASCII 大小写。

## 引用

引用（ref）指向一个归档，形式为 `target[#variant]`；spec target 后面还可以跟 git 引用：
`spec[#variant][@git-ref]`。

| target | 示例 | 指向 |
| --- | --- | --- |
| 相对目录 | `./quickjs/` | 当前文档所在归档内的一个目录。 |
| 相对文件 | `./LegacyMoney.mcmod` | 当前文档所在归档内的一个 ZIP 文件。 |
| URL | `https://example.com/pkg.mcmod` | 通过 HTTPS 下载的 ZIP 归档（会跟随重定向）。 |
| spec | `github.com/LiteLDev/LeviLamina` | git 仓库 `https://<spec>.git`。 |
| 带版本的 spec | `github.com/LiteLDev/LeviLamina@1.0.0` | 标签 `v1.0.0` 所指的提交。 |
| 带 git 引用的 spec | `github.com/LiteLDev/LeviLamina@branch:main` | 该仓库的一个提交。 |

- 相对 target 以 `./` 开头，不得包含空段、`.` 段、`..` 段、`#` 或 `\`。
- spec 必须是合法的 [Go module path](https://go.dev/ref/mod#module-path)。其版本是名称恰为
  `v<version>` 的标签，其中 `<version>` 是 [SemVer 2.0.0](https://semver.org/) 版本（例如
  `v1.2.3`）。其他标签（例如 `v1.2`）会被忽略。
- git 引用把 spec 固定到一个提交，而不是其各个版本：`@<version>`（标签 `v<version>`）、
  `@branch:<branch>`（分支的头）、
  `@tag:<tag>`（任意标签，例如 `@tag:nightly`）或 `@rev:<commit>`（完整的 40 位提交 ID）。
  它写在 variant 之后：`github.com/LiteLDev/LegacyScriptEngine#lua@branch:develop`。
- 不允许其他 scheme，例如 `http:` 和 `file:`。
- variant 必须匹配 `^[a-z0-9_]+(/[a-z0-9_]+)?$`。不带 `#` 的 ref，其 variant 为 `""`。

## 包文档

| 字段 | 类型 | 必需 | 说明 |
| --- | --- | --- | --- |
| `format` | `string` | 是 | `"bf45b1"`。 |
| `name` | `string` | 是 | 身份。必须匹配 `^[A-Za-z0-9][A-Za-z0-9._-]*$`。 |
| `version` | `string` | 是 | SemVer 2.0.0。位于版本标签（`v<version>`）处时必须等于标签版本。 |
| `platform` | `string` | 否 | `"server"` 或 `"client"`。缺省表示两者皆可。 |
| `runtime` | `string` | 否 | [.NET RID](https://learn.microsoft.com/zh-cn/dotnet/core/rid-catalog)。缺省表示不限。 |
| `dependencies` | `array` | 否 | [依赖](#依赖)。 |
| `conflicts` | `array` | 否 | [冲突](#冲突)：不得与本包同时安装的包。 |
| `entry` | `string` | 否 | 使该包成为 [mod](#mod)：mod 的入口，例如 `"LegacyMoney.dll"`。不得为空。 |
| `type` | `string` | 否 | mod 的类型，例如 `"native"`。不得为空。 |
| `files` | `array` | 否 | 仅限文件包。参见 [File](#file)。 |
| `archives` | `object` | 否 | 仅限文件包。供 `files` 取文件的下载归档。参见 [Archive](#archive)。 |
| `preserve` | `string[]` | 否 | 已存在时保留、卸载时也保留的文件的 [glob](#glob)。 |
| `remove` | `string[]` | 否 | 卸载时额外删除的路径的 [glob](#glob)。 |
| `hooks` | `object` | 否 | 生命周期命令。参见[钩子](#钩子)。 |

`entry` 和 `type` 必须同时出现，且 `entry` 不得与 `files` 或 `archives` 同时出现。

只有当 ref 不带 variant、`platform` 缺省或等于环境平台、`runtime` 缺省或等于环境的
runtime、并且 `version` 在请求的范围内时，包才是候选。

### 依赖

| 字段 | 类型 | 必需 | 说明 |
| --- | --- | --- | --- |
| `name` | `string` | 是 | 所依赖的包的身份。 |
| `version` | `string` | 否 | 版本范围。缺省表示任意版本。 |
| `source` | `string` 或 `string[]` | 否 | 包的位置：一个 ref，或按顺序尝试的多个备选。不得为空。 |

只有带 `source` 的依赖才由 lip 安装，且为其安装的包必须具有该依赖的 `name`。LeviLamina 也会读取
`dependencies`。

版本范围使用 [node-semver](https://github.com/npm/node-semver#ranges) 的语法和语义，例如
`^1.2.0`、`~1.2`、`>=1.0.0 <2.0.0`、`1.x`、`1.0.0 - 1.4.0` 或 `>=1.0.0 || 0.9.0`。不带运算符
的完整版本号（例如 `1.2.3`）只匹配该版本。预发布版本只匹配写出了同一 `major.minor.patch`
预发布版本的范围。

如需依赖某个分支、标签或提交，而不是正式发布的版本，请在 `source` 中的 spec 上加 git 引用；
`version` 仍是版本范围，因为 LeviLamina 也会读取它：

```json
{
  "dependencies": [
    { "name": "LeviLamina", "version": ">=1.5.0", "source": "github.com/LiteLDev/LeviLamina@branch:main" }
  ]
}
```

git 引用所指提交处的包必须声明在范围内的 `version`，否则该依赖无法满足。反过来，从 git
引用安装的包（作为依赖，或通过 [`lip install`](../cli/commands/install.md#git-引用)），
只要其版本在依赖的范围内，也能满足以同一 spec 和 variant、但不带 git 引用的依赖；因此工作区
可以运行一个库的开发构建，而依赖它的正式发布的模组仍可安装。

### 冲突

| 字段 | 类型 | 必需 | 说明 |
| --- | --- | --- | --- |
| `name` | `string` | 是 | 冲突的包的身份。 |
| `version` | `string` | 否 | 版本范围。缺省表示任意版本。 |

### Mod

带有 `entry` 和 `type` 的包是 mod。这两个键都由 LeviLamina 读取；lip 只根据 `entry` 是否存在来
区分 mod 与文件包。

包文档与 LeviLamina 清单共有的键（`name`、`version`、`platform`、`entry`、`type`、
`dependencies` 和 `conflicts`）在两者中形式相同，因此 mod 的 `manifest.json` 同时也是它的
LeviLamina 清单。LeviLamina 读取的其他键，例如 `description`、`author`、`passive`、
`loadBefore`、`loadAfter` 和 `extraInfo`，按 LeviLamina 的定义写在顶层；lip 忽略它们。

### File

| 字段 | 类型 | 必需 | 说明 |
| --- | --- | --- | --- |
| `from` | `string` | 否 | 取文件的 [archive](#archive) 的名称。缺省表示包自身的归档。 |
| `src` | `string` | 是 | 来源中的路径或 glob。 |
| `dest` | `string` | 是 | 工作区内的路径。 |

两者都不得以 `/` 开头，也不得包含空段、`..`、`\` 或盘符。File 有三种形式：

- **glob 形式**：`src` 含有 `*`、`?` 或 `[`。每个匹配的文件写入 `dest`（`""` 或以 `/` 结尾）
  之下，保留其相对于 `src` 中首个含通配符的段之前部分的路径。不匹配任何文件的 glob 什么也不写。
- **目录形式**：`src` 为 `""` 或以 `/` 结尾。`src` 下的全部文件写入 `dest`（`""` 或以 `/`
  结尾）之下。
- **文件形式**：其他情况。文件 `src` 写入 `dest`，`dest` 不得以 `/` 结尾。

`src` 所指文件或目录不存在时会报错。两个 File 写入同一路径时，后者生效。包根部的
`manifest.json` 永不写入。

### Archive

| 字段 | 类型 | 必需 | 说明 |
| --- | --- | --- | --- |
| `url` | `string` 或 `string[]` | 是 | https URL，按顺序尝试。 |
| `sha256` | `string` | 否 | 内容必须具有的 SHA-256。 |
| `format` | `string` | 否 | `"zip"`、`"tar"` 或 `"tgz"`；`"file"` 表示单个文件，其路径为该 archive 的名称。缺省时须为三种归档格式之一，按内容识别。 |

archive 的名称必须匹配 `^[A-Za-z0-9][A-Za-z0-9._-]*$`。

### Glob

glob 按段使用 POSIX `fnmatch` 的模式匹配：`*` 匹配除 `/` 外的任意字符（包括开头的 `.`），
`?` 匹配除 `/` 外的一个字符，`[...]` 匹配集合中的一个字符（`[!...]` 为补集）。单独构成一段的
`**` 匹配零个或多个段；以 `/` 结尾的模式匹配目录及其下的全部内容。没有转义字符。对 mod，glob
相对于 mod 目录 `mods/<name>/`；对文件包，相对于工作区根目录。

### 钩子

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `preInstall` | `string[]` | 写入文件前执行的命令。 |
| `postInstall` | `string[]` | 写入文件后执行的命令。 |
| `preUninstall` | `string[]` | 删除文件前执行的命令。 |
| `postUninstall` | `string[]` | 删除文件后执行的命令。 |

命令在工作区根目录中执行，Windows 上使用 `cmd /c`，其他平台使用 `sh -c`。任一命令以非零码
退出时，操作失败。

## mod 与文件包

- **mod**（带 `entry`）：整个归档写入 `mods/<name>/`。服务端的 `plugins` 是指向 `mods` 的链接，
  因此也可以在 `plugins/<name>/` 下看到该 mod。lip 只安装到服务端工作区；客户端工作区由
  LeviLauncher 等启动器管理。
- **文件包**（不带 `entry`）：按顺序执行 `files`。

```json
{
  "format": "bf45b1",
  "name": "bedrock-server",
  "version": "1.21.50",
  "platform": "server",
  "runtime": "win-x64",
  "archives": {
    "bds": { "url": "https://www.minecraft.net/bedrockdedicatedserver/bin-win/bedrock-server-1.21.50.01.zip" }
  },
  "files": [
    { "from": "bds", "src": "", "dest": "" },
    { "src": "defaults/*.json", "dest": "" }
  ],
  "preserve": ["server.properties", "allowlist.json", "permissions.json", "worlds/"]
}
```

写入已归其他已安装包所有的文件时会报错。

## 元文档

| 字段 | 类型 | 必需 | 说明 |
| --- | --- | --- | --- |
| `format` | `string` | 是 | `"bf45b1"`。 |
| `targets` | `array` | 是 | 非空。参见 [Target](#target)。 |
| `version` | `string` | 否 | SemVer 2.0.0。用于[模板](#模板)。位于版本标签（`v<version>`）处时必须等于标签版本。 |

元文档本身不描述任何包：其 target 指向的每个包都有自己的包文档。

### Target

| 字段 | 类型 | 必需 | 说明 |
| --- | --- | --- | --- |
| `variant` | `string` | 否 | 默认为 `""`。 |
| `platform` | `string` 或 `string[]` | 否 | `"server"`、`"client"` 或两者。缺省表示不限。 |
| `runtime` | `string` 或 `string[]` | 否 | .NET RID。缺省表示不限。 |
| `source` | `string` 或 `string[]` | 是 | 一个 ref，或按顺序尝试的多个备选。不得为空。 |

lip 保留 `variant` 等于 ref 的 variant、且所有选择器（`platform`、`runtime`）都包含当前
[环境](#环境)的 target，再从中选出写出选择器键最多的那一个。两个 variant 相同的 target 不得
写出相同的选择器键且取值有交集。

### 模板

<span v-pre>`source` 中的 ref 可以包含 `{{version}}`、`{{platform}}` 和 `{{runtime}}`。
`{{version}}` 是包的版本：位于版本标签处的 git 归档取标签版本，否则取文档的 `version`（没有时，
该 ref 不产生候选）；`{{platform}}` 和 `{{runtime}}` 取环境的值。出现其他任何 `{{...}}` 时，
文档无效。</span>

```json
{
  "format": "bf45b1",
  "version": "0.22.0",
  "targets": [
    {
      "platform": ["server", "client"],
      "runtime": "win-x64",
      "source": "https://github.com/LiteLDev/LegacyMoney/releases/download/v{{version}}/LegacyMoney-{{platform}}-{{runtime}}.mcmod"
    }
  ]
}
```

在 `win-x64` 服务端上，它指向 `LegacyMoney-server-win-x64.mcmod`，其 `manifest.json` 即本页开头的
[示例](#示例)。[`lip pack`](../cli/commands/pack.md) 可以生成这样的 `.mcmod` 文件。

## 环境

工作区的环境是一对值 `(platform, runtime)`：

- **platform**：`server` 或 `client`。lip 只管理服务端工作区，因此总是使用 `server`。客户端
  工作区由实现同一规范的启动器（如 LeviLauncher）管理；[锁文件](lockfiles.md)记录为 `client`
  的工作区会被 lip 拒绝。
- **runtime**：`--rid`；否则取锁文件中的值；否则取 lip 自身运行时的 RID（例如 `win-x64`）。

## 旧格式

lip 把下列格式读作包文档，但从不写入它们；请使用 [`lip migrate`](../cli/commands/migrate.md)
转换你的包。确切的映射见[原包格式规范](https://github.com/LiteLDev/lip/blob/1e023e6/specs/package_format.md) §10。

### LeviLamina 清单

不带 `format` 的 `manifest.json` 是 LeviLamina 清单。lip 把它读作一个 mod：身份为其 `name`，
版本为其 `version`（SemVer 2.0.0）；其 `platform` 为 `"server"` 或 `"client"` 时使用该值，并遵守
其 `conflicts`（`{ "name", "version" }` 条目）。lip 不会为它安装其他任何包：其依赖不会被安装，
它也没有 `runtime`、`preserve`、`remove` 和 `hooks`。

### tooth.json

lip v0.x 的包使用 `tooth.json`（格式版本 1 到 3）。lip 在内存中把 `tooth.json` 转换为文件包，
并像其他包一样处理它：

- 归档同时含有 `tooth.json` 和 `manifest.json` 时，以 `tooth.json` 为准。
- ref 的 variant 即变体标签。当平台为 `client` 且 ref 不带 variant 时，若存在标签为
  `client` 的变体，则使用它。具有该标签且与 runtime 匹配的各变体会像 lip v0.x 那样合并。
- 包身份是 tooth 名称，即 `tooth` 路径的最后一个元素（最后一个元素是 `v2` 这样的主版本后缀时，
  取其前一个元素）；带标签的变体再加上 `-<label>`（标签中的 `/` 替换为 `-`）。与 lip v0.x
  一样，每个带标签的变体都是独立的包，因此 `LegacyScriptEngine` 与它所依赖的
  `LegacyScriptEngine-quickjs`、`LegacyScriptEngine-lua` 变体会并存安装。
- 若标签处的 `tooth.json` 声明的版本与标签不同，以标签版本为准，lip 会给出警告。
- 所选变体的依赖按带 `source` 的依赖的方式安装，其版本范围按 lip v0.x 的方式解释；任何身份都能
  满足它们。
- 每个资源都成为包的一部分：`self` 是 `tooth.json` 自身所在的归档（例如标签处的仓库），`zip`、`tar`、`tgz` 和 `uncompressed`
  资源在安装包时下载（并缓存）。放置规则在安装时与资源中的文件匹配（包括 glob），写入的
  文件与 lip v0.x 相同。
- `preserve_files`、`remove_files` 和脚本分别成为 `preserve`、`remove` 和 `hooks`
  （`install` 随 `postInstall` 运行，`uninstall` 随 `preUninstall` 运行）。文件的归属、保留与
  删除遵循 `manifest.json` 的规则。
- 锁以 `"kind": "tooth"` 记录该 `tooth.json`；卸载或更新该包时，lip 会再次转换它。

格式 3 的 schema 为
[`legacy/v3/tooth.schema.yaml`](https://github.com/LiteLDev/lip/blob/1e023e6/specs/schemas/legacy/v3/tooth.schema.yaml)
（格式 1 和 2：[`legacy/v1/tooth.schema.yaml`](https://github.com/LiteLDev/lip/blob/1e023e6/specs/schemas/legacy/v1/tooth.schema.yaml)、[`legacy/v2/tooth.schema.yaml`](https://github.com/LiteLDev/lip/blob/1e023e6/specs/schemas/legacy/v2/tooth.schema.yaml)）：

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

变体的 `platform` 是 .NET RID 模式，`dependencies` 将包路径映射到
[semver 范围](https://github.com/WalkerCodeRanger/semver#version-ranges)，`assets` 的类型为
`self`（仓库中的文件）、`zip`、`tar`、`tgz` 或 `uncompressed`（从 `urls` 下载）。脚本为
`pre_install`、`install`/`post_install`、`pre_uninstall`/`uninstall` 和 `post_uninstall`。
