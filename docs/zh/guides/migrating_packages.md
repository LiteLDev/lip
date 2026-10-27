# 迁移包

lip 包过去由 `tooth.json`（格式版本 1 到 3）描述，现在由 `manifest.json`（格式版本 4，参见[包清单](../concepts/package_manifest.md)）描述。lip 仍可安装 `tooth.json` 包，但你的包的新版本应使用 `manifest.json`。`lip migrate` 命令可帮助你完成转换。

## 使用 `lip migrate`

```sh
lip migrate <FILE> <OUTPUT>
```

- `<FILE>`：现有 `tooth.json`（格式 1、2 或 3）的路径。
- `<OUTPUT>`：`manifest.json` 的写入路径。

### 示例

```sh
lip migrate tooth.json manifest.json
```

所有需要你决定的事项都会以警告形式输出。`manifest.json` 准备好后，请从仓库中删除 `tooth.json`：归档同时含有两者时，lip 读取的是 `tooth.json`。

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

lip 会用自身的解析器检查结果；如果结果还不是有效的 `manifest.json`（例如两个同标签的变体相互重叠），会给出警告。

lip 不会创建这些包：请按 lip 针对每个变体输出的提示，自行用该变体的文件构建具体的 `manifest.json`。参见[创建包](creating_packages.md)。

## 局限

`lip migrate` 只是辅助工具，不保证结果正确。请始终检查其输出。

- `info` 部分（名称、描述、标签、头像）没有对应项，会被丢弃。
- `src` 为 glob 模式的文件放置规则无法转换；请改为逐个列出文件。
- `platform` 的 glob 模式无法表示为 `rid`；此时包不限定 RID。
- LeviLamina 语法无法表示的版本范围会被省略。
- 使用了 `*` 和 `**` 以外语法的 glob 模式会原样复制，但匹配的文件可能不同。
- 转换得到的文件包会把文件放到与 `tooth.json` 完全相同的位置。如果你的包是 mod，可以考虑改为 mod（`type` 不为 `"files"`），它会被安装到 `mods/<name>/`。
- 对其他 `tooth.json` 包的依赖仍然有效，因为 lip 仍可安装 `tooth.json` 包（它会在内存中把它们转换为第 4 版）。

只有名称恰为 `v<SemVer>` 的标签（例如 `v1.2.3`）才是版本。`v1.2` 这样的标签会被忽略。
