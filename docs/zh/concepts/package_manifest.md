# 包清单（Package Manifest）

lip 包由其归档根部的 `manifest.json` 描述。它就是 LeviLamina 的 `manifest.json`，
再加上 lip 包格式第 4 版新增的键。LeviLamina 会忽略 lip 新增的键，lip 也不会改变任何
LeviLamina 键的含义。规范性定义见[包格式规范](../spec/package_format.md)。

该格式由 JSON Schema
[`manifest.v4.schema.json`](https://github.com/LiteLDev/lip/blob/main/schemas/manifest.v4.schema.json)
描述。lip 仍可读取旧的 `tooth.json` 格式，参见[旧格式：tooth.json](#旧格式-tooth-json)。

## 归档

包位于一个**归档**中，归档是一棵根部带有 `manifest.json` 的文件树：

- **git 归档**：git 仓库中某个版本标签所指的提交；
- **ZIP 归档**：一个 ZIP 文件。扩展名为 `.mcmod` 的 ZIP 归档称为 **mcmod**。一个文件是否为
  ZIP 归档只看内容，与扩展名无关；
- **目录归档**：一个本地目录。

`manifest.json` 必须是 UTF-8 编码的严格 JSON（不允许注释），顶层必须是对象。

## 示例

一个 mod，位于 `LegacyMoney-server-win-x64.mcmod` 的根部：

```json
{
  "formatVersion": 4,
  "formatUuid": "0c6b977a-64ad-441f-9a29-c7f4b196882f",
  "name": "LegacyMoney",
  "entry": "LegacyMoney.dll",
  "type": "native",
  "version": "0.22.0",
  "platform": "server",
  "rid": "win-x64",
  "refs": ["github.com/LiteLDev/LegacyMoney"],
  "dependencies": [
    {
      "name": "iListenAttentively",
      "version": "^0.17.0",
      "refs": ["github.com/MiracleForest/iListenAttentively"]
    }
  ],
  "installDependencies": [
    { "version": ">=26.51.0 <26.52.0", "refs": ["github.com/LiteLDev/LeviLamina"] }
  ]
}
```

## 格式标记

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `formatVersion` | `int` | 必须为 `4`。 |
| `formatUuid` | `string` | 必须为 `"0c6b977a-64ad-441f-9a29-c7f4b196882f"`。 |

同时带有两个标记的文档是第 4 版文档。两个标记都没有的文档是普通的 LeviLamina 清单：
lip 只把它当作自包含的具体包接受，忽略其依赖中的 `refs` 和 `installDependencies`
（不会安装其他任何包），也从不把它当作路由文档。只带有一个标记或标记值不正确的文档无效。

## 路由文档与具体文档

含有 `artifacts` 键的文档是**路由文档**。它本身不安装任何东西，而是根据请求的变体和环境
指向其他归档。

不含 `artifacts` 的文档是**具体文档**，其归档称为**具体包**。具体包的 `name` 即其身份；
比较名称时不区分 ASCII 大小写。

## 引用

引用（ref）指向一个归档，形式为 `target[#variant]`：

| target | 示例 | 指向 |
| --- | --- | --- |
| 相对目录 | `./quickjs/` | 当前文档所在归档内的一个目录。 |
| 相对文件 | `./LegacyMoney.mcmod` | 当前文档所在归档内的一个 ZIP 文件。 |
| URL | `https://example.com/pkg.mcmod` | 通过 HTTPS 下载的 ZIP 归档（会跟随重定向）。 |
| spec | `github.com/LiteLDev/LeviLamina` | git 仓库 `https://<spec>.git`。 |

- 相对 target 以 `./` 开头，不得包含空段、`.` 段、`..` 段或 `\`。
- spec 必须是合法的 [Go module path](https://go.dev/ref/mod#module-path)。其版本是名称恰为
  `v<version>` 的标签，其中 `<version>` 是 [SemVer 2.0.0](https://semver.org/) 版本（例如
  `v1.2.3`）。其他标签（例如 `v1.2`）会被忽略。
- 不允许其他 scheme，例如 `http:` 和 `file:`。
- variant 必须匹配 `^[a-z0-9_]+(/[a-z0-9_]+)?$`。不带 `#` 的 ref，其 variant 为 `""`。

## 路由文档

| 字段 | 类型 | 必需 | 说明 |
| --- | --- | --- | --- |
| `artifacts` | `array` | 是 | 非空。参见 [Artifact](#artifact)。 |
| `name` | `string` | 否 | 只用于模板。 |
| `version` | `string` | 否 | SemVer 2.0.0。只用于模板。 |
| `refs` | `string[]` | 否 | 上游位置，仅供参考。 |

### Artifact

| 字段 | 类型 | 必需 | 说明 |
| --- | --- | --- | --- |
| `variant` | `string` | 否 | 默认为 `""`。 |
| `platform` | `string` 或 `string[]` | 否 | `"server"`、`"client"` 或两者。缺省表示不限。 |
| `rid` | `string` 或 `string[]` | 否 | [.NET RID](https://learn.microsoft.com/zh-cn/dotnet/core/rid-catalog)。缺省表示不限。 |
| `refs` | `string[]` | 是 | 非空。按顺序作为备选。 |

lip 保留 `variant` 等于 ref 的 variant、且所有选择器（`platform`、`rid`）都包含当前
[环境](#环境)的条目，再从中选出写出选择器键最多的那一个。两个 variant 相同的条目不得写出
相同的选择器键且取值有交集。

### 模板

<span v-pre>`refs` 中的字符串可以包含 `{{name}}`、`{{version}}`、`{{platform}}` 和
`{{rid}}`。`{{name}}` 和 `{{version}}` 取文档中同名键的值（缺少该键时报错）；
`{{platform}}` 和 `{{rid}}` 取环境的值。出现其他任何 `{{...}}` 时，文档无效。</span>

```json
{
  "formatVersion": 4,
  "formatUuid": "0c6b977a-64ad-441f-9a29-c7f4b196882f",
  "name": "LegacyMoney",
  "version": "0.22.0",
  "artifacts": [
    {
      "platform": ["server", "client"],
      "rid": "win-x64",
      "refs": [
        "https://github.com/LiteLDev/LegacyMoney/releases/download/v{{version}}/{{name}}-{{platform}}-{{rid}}.mcmod"
      ]
    }
  ]
}
```

## 具体文档

| 字段 | 类型 | 必需 | 说明 |
| --- | --- | --- | --- |
| `name` | `string` | 是 | 身份。必须匹配 `^[A-Za-z0-9][A-Za-z0-9._-]*$`。 |
| `version` | `string` | 是 | SemVer 2.0.0。位于标签处时必须等于标签版本。 |
| `type` | `string` | 是 | `"files"` 表示[文件包](#文件包与-mod)；其他任何值都是 LeviLamina 的 mod 类型（例如 `"native"`）。 |
| `platform` | `string` | 否 | `"server"`、`"client"` 或 `"universal"`（默认）。 |
| `rid` | `string` | 否 | .NET RID。缺省表示不限。 |
| `refs` | `string[]` | 否 | 上游位置，仅供参考。 |
| `dependencies` | `array` | 否 | LeviLamina 依赖。含 `refs` 的条目也由 lip 安装。参见[依赖](#依赖)。 |
| `optionalDependencies` | `array` | 否 | LeviLamina 可选依赖。lip 不安装。 |
| `conflicts` | `array` | 否 | 不得与本包同时安装的包。忽略其中的 `refs`。 |
| `installDependencies` | `array` | 否 | 只由 lip 安装的包；LeviLamina 不读取。`refs` 必需。 |
| `placements` | `array` | 否 | 仅限文件包。参见 [Placement](#placement)。 |
| `preserveFiles` | `string[]` | 否 | 已存在时保留、卸载时也保留的文件的 glob。 |
| `removeFiles` | `string[]` | 否 | 卸载时额外删除的路径的 glob。 |
| `scripts` | `object` | 否 | 生命周期脚本。参见[脚本](#脚本)。 |

`entry` 等其余 LeviLamina 键，lip 不使用。

只有当 ref 不带 variant、`platform` 等于环境平台或为 `"universal"`、`rid` 缺省或等于环境
RID、并且 `version` 在请求的范围内时，具体包才是候选。

### 依赖

| 字段 | 类型 | 必需 | 说明 |
| --- | --- | --- | --- |
| `name` | `string` | 是（`installDependencies` 中为否） | 被安装的包必须具有的身份。 |
| `version` | `string` | 否 | 版本范围。缺省表示任意版本。 |
| `refs` | `string[]` | 否（`installDependencies` 中为是） | 包的位置。出现时必须非空。 |

版本范围使用 LeviLamina `ll::data::VersionRequirement` 的语法，例如 `^1.2.0`、`~1.2`、
`>=1.0.0 <2.0.0`、`1.x` 或 `>=1.0.0 || =0.9.0`。不带运算符的完整版本号（例如 `1.2.3`）
会被拒绝，请写成 `=1.2.3`。

### Placement

| 字段 | 类型 | 必需 | 说明 |
| --- | --- | --- | --- |
| `src` | `string` | 是 | 归档内的路径。 |
| `dest` | `string` | 是 | 工作区内的路径。 |

两者都不得以 `/` 开头，也不得包含 `..`、`\` 或盘符。值为 `""` 或以 `/` 结尾时表示目录：
`src` 下的全部文件写入 `dest` 下；否则表示文件。`src` 与 `dest` 的形式必须相同。归档根部的
`manifest.json` 永不写入。

### Glob

`*` 匹配除 `/` 外的任意字符；单独构成一段的 `**` 匹配零个或多个段；以 `/` 结尾的模式匹配
目录及其下的全部内容。对 mod，glob 相对于 mod 目录；对文件包，相对于工作区根目录。

### 脚本

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `preInstall` | `string[]` | 写入文件前执行的命令。 |
| `postInstall` | `string[]` | 写入文件后执行的命令。 |
| `preUninstall` | `string[]` | 删除文件前执行的命令。 |
| `postUninstall` | `string[]` | 删除文件后执行的命令。 |

命令在工作区根目录中执行，Windows 上使用 `cmd /c`，其他平台使用 `sh -c`。任一命令以非零码
退出时，操作失败。

## 文件包与 mod

- **mod**（`type` 不是 `"files"`）：整个归档写入服务端的 `plugins/<name>/`，或客户端的
  `mods/<name>/`。lip 只安装到服务端工作区；客户端工作区由 LeviLauncher 等启动器管理。
- **文件包**（`"type": "files"`）：按顺序执行 `placements`。

```json
{
  "formatVersion": 4,
  "formatUuid": "0c6b977a-64ad-441f-9a29-c7f4b196882f",
  "name": "PeEditor",
  "type": "files",
  "version": "3.9.0",
  "rid": "win-x64",
  "placements": [{ "src": "PeEditor.exe", "dest": "PeEditor.exe" }]
}
```

写入已归其他已安装包所有的文件时会报错。

## 环境

工作区的环境是一对值 `(platform, rid)`：

- **platform**：`server` 或 `client`。lip 只管理服务端工作区，因此总是使用 `server`。客户端
  工作区由实现同一规范的启动器（如 LeviLauncher）管理；[锁文件](lockfiles.md)记录为 `client`
  的工作区会被 lip 拒绝。
- **rid**：`--rid`；否则取锁文件中的值；否则取 lip 自身运行时的 RID（例如 `win-x64`）。

## 旧格式：tooth.json

lip v0.x 的包使用 `tooth.json`（格式版本 1 到 3）。lip 仍然读取它们，但不再写入；
请使用 [`lip migrate`](../cli/commands/migrate.md) 转换你的包。

- 归档同时含有 `tooth.json` 和 `manifest.json` 时，以 `tooth.json` 为准。
- 包的身份是 `tooth` 的值，ref 的 variant 即变体标签。当平台为 `client` 且 ref 不带
  variant 时，若存在标签为 `client` 的变体，则使用它。
- 所选变体的依赖按 `installDependencies` 的方式安装。
- 若标签处的 `tooth.json` 声明的版本与标签不同，以标签版本为准，lip 会给出警告。

格式 3 的 schema 为
[`manifest.schema.json`](https://github.com/LiteLDev/lip/blob/main/schemas/manifest.schema.json)
（格式 1 和 2：`manifest.v1.schema.json`、`manifest.v2.schema.json`）：

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
