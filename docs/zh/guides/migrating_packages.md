# 迁移包

为 lip v0.37.0 及更早版本编写的包使用格式版本 1、2 或 3 的 `tooth.json`。当前格式为格式版本 4（参见 [tooth.json](../concepts/tooth_json.md)）。lip 仍可安装早期格式的包，但你的包的新版本应使用格式 4。`lip migrate` 命令可帮助你完成转换。

## 使用 `lip migrate`

在包仓库的根目录（即包含 `tooth.json` 的目录）中不带参数运行 `lip migrate`：

```sh
cd MyTool
lip migrate
git add tooth.json
git commit -m "Migrate tooth.json to format 4"
```

它把 `tooth.json` 原地改写为格式 4，以两个空格缩进。没有 `tooth.json`、它已是格式 4 或无法读取时，命令会失败，且不做任何修改。

### 询问

格式 4 没有 RID 模式：每个条目只指定一个运行时。当结果需要某个旧文件未提供的值时，lip 会在终端上询问，直到回答有效为止：

- `win-*` 这样的平台模式所代表的 RID，以逗号分隔（每个都必须匹配该模式）；
- 没有 `platform` 的变体所适用的 RID（存在这类变体时）。

若标准输入或标准错误不是终端（例如在 CI 中），`lip migrate` 会失败，指明本应询问的值，且不做任何修改。[`lip rpc`](../cli/commands/rpc.md) 的 `Migrate` 方法从不询问：它会以同样的方式失败。

### 警告

所有被丢弃或以不同含义转换的内容都会以警告形式输出，并指明其来源字段，例如 `variants (label '', win-x64).assets[1]`。`tooth.json` 格式 1 和 2 的文件会先被转换为格式 3，因此字段名按格式 3 给出。警告不会导致命令失败；请检查这些警告，按需修改 `tooth.json`，然后提交。

## 变化内容

`format_version` 和 `format_uuid` 设为格式 4 的值，`tooth`、`version` 和 `info` 原样复制。变体按以下方式改写：

- 每个标签变为同名变体；空标签变为变体 `""`。
- 对标签适用的每个运行时（其各平台，模式展开为你给出的 RID），该标签下适用于该运行时的变体按 lip v0.37.0 的方式合并，结果变为该运行时的一个服务端条目和一个客户端条目。
- 存在 `client` 标签时（常见的服务端/客户端拆分），变体 `""` 只得到服务端条目，`client` 变体的条目同时也作为变体 `""` 的客户端条目写入。
- `dependencies` 连同其范围一起复制。无法以相同含义写成格式 4 语法的范围会被省略，并给出警告。
- `self` 和 `zip` 资源保留，其放置规则变为格式 4 的放置规则：`dir` 放置规则采用目录形式（`src` 和 `dest` 以 `/` 结尾，或为 `""`），`file` 放置规则采用文件形式，其 `src` 为模式时采用 glob 形式。`dest` 中的 `\` 变为 `/`。`tar`、`tgz` 和 `uncompressed` 资源类型没有对应项，会被丢弃。
- `preserve_files` 和 `remove_files` 保留，其中的 `\` 替换为 `/`（会改变的模式参见[局限](#局限)）。
- scripts 变为 `hooks`：`pre_install`、`post_install`（先 `install`，再 `post_install`）、`pre_uninstall`（先 `pre_uninstall`，再 `uninstall`）和 `post_uninstall`。
- 空值会被省略。

## 局限

`lip migrate` 只是辅助工具，不保证结果正确。请始终检查其输出。

- 格式 4 要求标签处的 `version` 等于标签版本，且仓库的 `tooth` 等于其路径。lip v0.37.0 会安装违反这些规则的 `tooth.json`；发布前请检查你的文件。
- `self` 资源的放置规则会依据仓库目录中的文件进行检查。没有放置任何文件的放置规则会被省略；格式 4 形式会放置其他文件的放置规则（例如还匹配子目录中文件的 `file` 模式，而 `tooth.json` 只按文件名放置它们）会被展开为每个文件一条放置规则，因此之后新增的文件不会被放置。两种情况都会被报告。对于 `zip` 资源，文件未知，这类模式只会被报告。
- 不含 `/` 的 `preserve_files` 模式过去会匹配任意目录中的文件名；它会变为 `**/<pattern>`。`remove_files` 模式过去也会删除匹配的目录；它还会以带结尾 `/` 的形式再写一遍。不是有效 glob 的模式（例如以 `/` 开头的）会被丢弃。
- 格式 1 和 2 中版本范围无法在格式 3 中读取的依赖（例如 `2.16.x`）在安装 `tooth.json` 时本就会被忽略；lip 会报告它们，以便你手动添加。
- 对其他包 `client` 变体的依赖会保留其 `#client`。待那个包迁移到格式 4 后请将其删除，因为格式 4 的变体 `""` 已涵盖客户端。
- 安装与卸载钩子以外的脚本（例如 `pre_pack` 或自定义脚本）以及格式 2 的 `prerequisites` 没有对应项，会被丢弃。
- 迁移得到的 `tooth.json` 是一个文件包，把文件放到与旧 `tooth.json` 相同的位置。如果你的包是 LeviLamina mod，可以考虑改为以 `mcmod` 资源发布（参见 [mod](creating_packages.md#mod)），它会被安装到 `mods/<name>/`。
- 对其他早期格式包的依赖仍然有效，因为 lip 仍可安装它们。

只有名称恰为 `v<SemVer>` 的标签（例如 `v1.2.3`）才是版本。`v1.2` 这样的标签不是版本。
