# 迁移包

lip 包过去由 `tooth.json`（格式版本 1 到 3）描述，LeviLamina mod 则由不带 `format` 键的 `manifest.json` 描述。现在包由带有 `"format": "bf45b1"` 的 `manifest.json` 描述（参见[包清单](../concepts/package_manifest.md)）。lip 仍可安装这两种旧式的包，但你的包的新版本应使用新的 `manifest.json`。`lip migrate` 命令可帮助你完成转换。

## 使用 `lip migrate`

在包仓库的根目录（即包含 `tooth.json`（格式 1、2 或 3）或 LeviLamina `manifest.json` 的目录）中不带参数运行 `lip migrate`：

```sh
cd MyTool
lip migrate
git add -A
git commit -m "Migrate to manifest.json"
```

它按下文所述转换该文件，并写入以两个空格缩进的 `manifest.json`。随后删除 `tooth.json`（归档同时含有两者时，lip 读取的是 `tooth.json`，所以必须删除它），且只有在 `manifest.json` 写入成功后才会删除。如果目录中同时有 `tooth.json` 和 `manifest.json`，命令会失败，且不做任何修改：请先删除你不打算转换的那个。两个文件都没有，或 `manifest.json` 已带有 `format` 键时，命令同样会失败。

### 询问

当结果需要某个旧文件未以有效形式提供的值时，lip 会在终端上询问，直到回答有效为止。例如：

- `id`：当 tooth 名称或 LeviLamina 清单的 `name` 不是有效的 id 时；
- `version`：当它不是 SemVer 版本时，例如 LeviLamina mod 模板中 `"${modVersion}"` 这样的构建占位符；
- LeviLamina 清单的 `mod.loader` 和 `mod.entry`：当它们缺失时；
- `tooth.json` 中 `win-*` 这样的平台模式所代表的 RID，以逗号分隔；
- 对于不在 GitHub 上的 tooth 路径，包将要发布到的 URL 前缀。

若标准输入或标准错误不是终端（例如在 CI 中），`lip migrate` 会失败，指明本应询问的值，且不做任何修改。守护进程的 `Migrate` 方法从不询问：它会以同样的方式失败。

### 警告

所有被丢弃或以不同含义转换的内容都会以警告形式输出，并指明其来源字段，例如 `variants[0].preserve_files`。`tooth.json` 格式 1 和 2 的文件会先被转换为格式 3，因此字段名按格式 3 给出。警告不会导致命令失败；请检查这些警告，按需修改 `manifest.json`，然后提交。

## 从 tooth.json 迁移

`id` 是 tooth 名称，即 `tooth` 路径的最后一段（例如 `github.com/user/MyTool` 变为 `MyTool`；`/v2` 这样的主版本后缀会被跳过）。`version` 原样复制，`info` 取 `tooth.json` 的 `name`、`description`、`tags`，以及作为 `icon` 的 `avatar_url`。结果是以下两种文档之一。

**包文档**：当 `tooth.json` 只有一个不带标签的变体、其资源全部为 `self`、且其 `platform` 为空或不是 glob 模式时：该变体的内容（见下文）写在顶层，其 `platform` 成为 `runtime`。

**元文档**：其他情况下，其 `targets` 承载各变体的内容：

- 对每个标签，各变体按 RID 像 lip v0.x 那样合并；合并后内容相同的 RID 共用一个 target，其 `runtime` 列出这些 RID。`platform` 为空的变体另外组成一个不带 `runtime` 的 target。
- target 的 `variant` 为该标签（空标签时省略），带有合并后的内容；带标签的变体还带有 `id` `<tooth 名称>-<label>`，即 lip v0.x 中该变体的包所具有的身份。
- 若存在 `client` 标签（常见的服务端/客户端拆分，lip 按平台选择），无标签的 target 会带上 `"platform": "server"`，`client` 的 target 则再以无标签、`"platform": "client"` 的形式写一遍，并保留其 `id`（`<tooth 名称>-client`）。
- 每个 target 的 `source` 是该 target 的包应当发布的位置：对于 `github.com/<owner>/<repo>` 路径，指向 release 资源 <span v-pre>`https://github.com/<owner>/<repo>/releases/download/v{{version}}/{{id}}[-{{platform}}][-{{runtime}}].mcmod`</span>，其中 target 带有 `platform` 和 `runtime` 时才分别带上 <span v-pre>`-{{platform}}`</span> 和 <span v-pre>`-{{runtime}}`</span>；对于其他路径，是 lip 询问你的 URL 前缀下的同名文件。

lip 不会创建这些包：请用 [`lip pack`](../cli/commands/pack.md) 逐个生成，并附加到 release 上。参见[创建包](creating_packages.md)。

（合并后的）变体的内容：

- 依赖转换为 `dependencies`。键是该依赖所具有的身份（其 tooth 名称，带标签时再加 `-<label>`），值为 `{ "version": <range>, "source": "<path>[#label]" }`。版本范围转换为 [node-semver](https://github.com/npm/node-semver#ranges) 语法；接受任意版本的范围会被省略。
- `self` 以外的资源转换为名为 `asset<i>` 的 `archives`（`i` 为资源的下标），其 `urls` 成为 `url`，类型成为 `format`（`uncompressed` 为 `"file"`）。
- 放置规则转换为 `files`，从其资源对应的 archive 取文件（`from`）；`self` 资源则从包自身取文件。`dir` 放置规则成为目录（`src` 以 `/` 结尾；`""` 和 `"."` 成为 `""`），`file` 放置规则成为单个文件，`src` 为模式时成为 glob。对于 `uncompressed` 资源，`src` 为其 archive 的名称。`dest` 中的 `\` 变为 `/`。
- `preserve_files` 和 `remove_files` 转换为 `preserve` 和 `remove`。
- 脚本转换为 `hooks`：`preInstall`（`pre_install`）、`postInstall`（先 `install`，后 `post_install`）、`preUninstall`（先 `pre_uninstall`，后 `uninstall`）和 `postUninstall`（`post_uninstall`）。
- 空值会被省略。

## 从 LeviLamina 清单迁移

不带 `format` 的 `manifest.json` 会被原地改写为一个 mod：

| LeviLamina 键 | 转换为 |
| --- | --- |
| `name` | `id` |
| `version` | `version` |
| `platform` | `platform`，仅当其为 `"server"` 或 `"client"` 时 |
| `description` | `info.description` |
| `author` | `info.authors` |
| `type`、`entry`、`passive`、`extraInfo` | `mod.loader`、`mod.entry`、`mod.passive`、`mod.extra` |
| `dependencies` | `dependencies`：每个 `name` 映射到其 `version`，或 `"*"` |
| `conflicts` | `conflicts`：每个 `name` 映射到其 `version`，或 `"*"` |
| `optionalDependencies`、`loadAfter` | `mod.loadAfter`：它们的名称，去除重复 |
| `loadBefore` | `mod.loadBefore`：它们的名称，`__before_all_mods__` 变为 `"*"` |

其他键移入 `mod.extra`。转换得到的依赖没有 `source`，因此 lip 不会安装它们：请为应由 lip 安装的依赖加上 `source`，例如 `"LeviLamina": { "version": "...", "source": "github.com/LiteLDev/LeviLamina" }`。

## 局限

`lip migrate` 只是辅助工具，不保证结果正确。请始终检查其输出。

- `src` 为 glob 模式的 `file` 放置规则在 `tooth.json` 中把每个匹配的文件放到 `dest/<文件名>`，而 `files` 中的 glob 会保留模式中不含通配符的部分之下的路径。两者不同时（例如 `**/*.dll`），lip 会给出警告；对于 `self` 资源，它依据仓库目录中的文件进行检查。
- node-semver 无法表示的版本范围，或转换后接受的版本与 `tooth.json` 中的范围不同的，会被报告。
- 不含 `/` 的 `preserve_files` 模式过去会匹配任意目录中的文件名；它会变为 `**/<pattern>`。`remove_files` 模式过去也会删除匹配的目录；它还会以带结尾 `/` 的形式再写一遍。不是有效 glob 的模式（例如以 `/` 开头的）会被丢弃。
- 格式 1 和 2 中版本范围无法在格式 3 中读取的依赖（例如 `2.16.x`）在安装 `tooth.json` 时本就会被忽略；lip 会报告它们，以便你手动添加。
- 安装与卸载钩子以外的脚本（例如 `pre_pack` 或自定义脚本）以及格式 2 的 `prerequisites` 没有对应项，会被丢弃。
- 在 LeviLamina 清单中，`optionalDependencies`、`loadAfter` 和 `loadBefore` 中的版本会被丢弃。在 `dependencies`、`conflicts`、`optionalDependencies`、`loadAfter` 和 `loadBefore` 中，名称不是有效 id 的条目都会被丢弃，不是 node-semver 范围的 `version` 变为 `"*"`。
- 转换得到的 `tooth.json` 文件包会把文件放到与 `tooth.json` 完全相同的位置。如果你的包是 mod，可以考虑改为 mod（带 `mod` 键），它会被安装到 `mods/<id>/`。
- 对其他 `tooth.json` 包的依赖仍然有效，因为 lip 仍可安装 `tooth.json` 包（它会在内存中转换它们）。

只有名称恰为 `v<SemVer>` 的标签（例如 `v1.2.3`）才是版本。`v1.2` 这样的标签会被忽略。
