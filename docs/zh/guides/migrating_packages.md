# 迁移包

lip 包过去由 `tooth.json`（格式版本 1 到 3）描述，现在由 `manifest.json`（格式版本 4，参见[包清单](../concepts/package_manifest.md)）描述。lip 仍可安装 `tooth.json` 包，但你的包的新版本应使用 `manifest.json`。`lip migrate` 命令可帮助你完成转换。

## 使用 `lip migrate`

在包仓库的根目录（即包含 `tooth.json`（格式 1、2 或 3）的目录）中不带参数运行 `lip migrate`：

```sh
cd MyTool
lip migrate
git add -A
git commit -m "Migrate to manifest.json"
```

它按下文所述把 `tooth.json` 转换为 `manifest.json`，写入 `manifest.json`，然后删除 `tooth.json`（归档同时含有两者时，lip 读取的是 `tooth.json`，所以必须删除它）。只有在 `manifest.json` 写入成功后才会删除 `tooth.json`。如果当前目录中没有 `tooth.json`，命令会失败，且不做任何修改。

所有无法用 `manifest.json` 表达或需要你决定的事项都会以警告形式输出，并指明相关的 `tooth.json` 字段，例如 `variants[0].preserve_files`。格式 1 和 2 的文件会先被转换为格式 3，因此字段名按格式 3 给出。警告不会导致命令失败；请检查这些警告，按需修改 `manifest.json`，然后提交。

### 已有的 `manifest.json`

许多仓库已经有 `manifest.json`，例如 mod 的 LeviLamina 清单。`lip migrate` 会合并进该文件，而不是替换它：

- 文件中还没有的键会按转换后的顺序、以该文件自身的缩进和换行符追加到对象末尾，其中包括格式标记 `formatVersion` 和 `formatUuid`。
- 文件中已有的键绝不会被覆盖。若已有值与转换得到的值不同，lip 会输出一条包含两个值的警告。
- 文件的其余内容（包括键的顺序和格式）保持不变。

随后 lip 会检查合并后的文件；如果它还不是有效的 `manifest.json`，会给出警告。例如，LeviLamina mod 模板中 `version` 是 `"${modVersion}"` 这样的构建占位符时就会如此：路由文档的 `version` 必须是 SemVer 版本，并且与其发布时的标签相同。发布前请把这类占位符替换为实际值，或把构建模板移到别处。

## 迁移逻辑

`name` 取自 `tooth` 路径的最后一段（例如 `github.com/user/MyTool` 变为 `MyTool`），`version` 原样复制。结果是以下两种文档之一。

**文件包**：当 `tooth.json` 只有一个不带标签的变体，且其资源全部为 `self` 时：

- `"type": "files"`，`rid` 取自该变体的 `platform`。
- 放置规则转换为 `placements`。
- 依赖转换为 `installDependencies`，其版本范围转换为 LeviLamina 的语法。
- `preserve_files` 和 `remove_files` 转换为 `preserveFiles` 和 `removeFiles`。
- 脚本转换为 `preInstall`、`postInstall`（先 `install`，后 `post_install`）、`preUninstall`（先 `pre_uninstall`，后 `uninstall`）和 `postUninstall`。

**路由文档**：其他情况下，每个变体对应一个条目：`variant` 为标签，`rid` 为平台。每个条目指向该变体的具体包应当发布的位置：

- 对于 `github.com/<owner>/<repo>` 路径，指向 release 资源 <span v-pre>`https://github.com/<owner>/<repo>/releases/download/v{{version}}/{{name}}-<label>[-{{rid}}].mcmod`</span>（空标签记作 `default`）；
- 否则，指向仓库中的目录 `./<label>/`。

如果标签只有空标签和 `client`（常见的服务端/客户端拆分，lip 按平台选择），二者会成为默认变体下 `"platform": "server"` 与 `"platform": "client"` 的两个条目，release 资源名为 <span v-pre>`{{name}}-{{platform}}[-{{rid}}].mcmod`</span>。

lip 会用自身的解析器检查结果；如果结果还不是有效的 `manifest.json`（例如两个同标签的变体相互重叠，或从已有 `manifest.json` 保留的键与之冲突），会给出警告。

lip 不会创建这些包：请按 lip 针对每个变体输出的提示，自行用该变体的文件构建具体的 `manifest.json`。参见[创建包](creating_packages.md)。

## 局限

`lip migrate` 只是辅助工具，不保证结果正确。请始终检查其输出。

- `info` 部分（名称、描述、标签、头像）没有对应项，会被丢弃。
- `self` 以外的资源（`zip`、`tar`、`tgz`、`uncompressed`）需要从 URL 下载，而 `manifest.json` 无法做到：这样的变体会成为路由文档中的一个条目，其文件需要打包进它的具体包中。
- 在文件包中，每条放置规则都会依据仓库目录中的文件（lip v3 正是从这些文件放置的）进行检查。`manifest.json` 无法写成放置相同文件的放置规则（例如 `src` 为 glob 模式的 `file` 放置规则）会被展开为每个文件一条的文件放置规则，并给出警告：之后新增的文件在你修改该列表之前不会被放置。
- `platform` 的 glob 模式无法表示为 `rid`；此时包不限定 RID。
- LeviLamina 语法无法表示的版本范围会被省略。转换后接受的版本与 `tooth.json` 中的范围不同的（lip 会探测其边界附近的版本，例如预发布版本），会保留并给出警告。
- 不含 `/` 的 `preserve_files` 模式过去会匹配任意目录中的文件名；它会变为 `**/<pattern>`，并给出警告。
- 使用了 `*` 和 `**` 以外语法的 glob 模式会原样复制，但匹配的文件可能不同。
- 在文件包中，不匹配仓库中任何文件的放置规则（例如指向目录的 `file` 放置规则）在 `tooth.json` 中不会放置任何文件，但会导致安装失败；它会被省略并给出警告。仓库目录中可能包含尚未提交的文件，lip 无法区分它们。
- 格式 1 和 2 中版本范围无法在格式 3 中读取的依赖（例如 `2.16.x`）在安装 `tooth.json` 时本就会被忽略；lip 会报告它们，以便你手动添加。
- 安装与卸载钩子以外的脚本（例如 `pre_pack` 或自定义脚本）以及格式 2 的 `prerequisites` 没有对应项，会被丢弃。
- 转换得到的文件包会把文件放到与 `tooth.json` 完全相同的位置。如果你的包是 mod，可以考虑改为 mod（`type` 不为 `"files"`），它会被安装到 `mods/<name>/`。
- 对其他 `tooth.json` 包的依赖仍然有效，因为 lip 仍可安装 `tooth.json` 包（它会在内存中把它们转换为第 4 版）。

只有名称恰为 `v<SemVer>` 的标签（例如 `v1.2.3`）才是版本。`v1.2` 这样的标签会被忽略。
