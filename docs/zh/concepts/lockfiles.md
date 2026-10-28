# 锁文件

`lip` 使用锁文件来维护当前工作区状态。该文件会精确记录已安装的包、具体版本、包的来源，以及写入磁盘的文件。

## 位置

锁文件保存在工作区之外、lip 的用户数据目录中，因此工作区里只有各个包的文件：

| 平台 | 路径 |
| --- | --- |
| Windows | `%LOCALAPPDATA%\lip\workspaces\<hash>.json` |
| Linux / macOS | `$XDG_DATA_HOME/lip/workspaces/<hash>.json`（默认 `~/.local/share/lip/workspaces/<hash>.json`） |

`<hash>` 是工作区绝对路径（不带末尾分隔符；Windows 上路径不区分大小写，因此先转为小写）的 SHA-256，以小写十六进制表示。锁文件还会在 `workspace` 中记录该路径；若两者不一致，lip 会报错停止，而不会使用其他工作区的锁。

由于锁与路径绑定，移动或重命名工作区目录后，原来的锁不会跟随：lip 会把移动后的工作区视为未安装任何包。

### 从工作区迁移

早期版本把锁保存在工作区根目录，即 `lip_lock.json`（格式 4）或 `tooth_lock.json`（格式 3）。当数据目录中没有某个工作区的锁时，lip 会读取 `lip_lock.json`，否则迁移 `tooth_lock.json`（参见[下文](#旧格式-tooth-lock-json)）。下一次保存锁时（安装、卸载或更新包之后），lip 会把锁写入数据目录，并从工作区删除这两个文件。

## 作用

锁文件在 `lip` 中有几个关键用途：

1. **状态追踪**：作为工作区当前安装状态的唯一真实来源。
2. **可复现性**：记录每个已安装包的确切归档。当 lip 再次需要某个包时（例如卸载它），会直接打开该归档并校验其标识，而不是重新解析该包。
3. **卸载清理**：`lip` 会追踪包写入的每个文件，使 `lip uninstall` 能干净删除相关文件，避免工作区出现“陈旧文件”。
4. **依赖图**：区分用户显式安装的包和作为依赖隐式安装的包。这样 `lip uninstall` 会一并移除不再被任何显式安装的包需要的依赖（除非指定了 `--no-dependencies`），`lip list` 也能分组显示这两类包。
5. **环境**：一旦确定了工作区的平台和 RID，就记录下来，使之后的命令使用相同的[环境](package_manifest.md#环境)。

锁文件在一次操作的所有包都安装或卸载完成之后保存一次。操作失败时，lip 会撤销它对工作区的修改，锁文件保持原样，因此两者始终一致（参见[内部机制](../guides/internal_mechanism.md#流程)）。

## 结构

锁文件是一个 JSON 文件，由 JSON Schema [`lock.v4.schema.json`](https://github.com/LiteLDev/lip/blob/main/schemas/lock.v4.schema.json) 描述：

```json
{
  "formatVersion": 4,
  "formatUuid": "0c6b977a-64ad-441f-9a29-c7f4b196882f",
  "workspace": "C:\\bds",
  "platform": "server",
  "rid": "win-x64",
  "packages": [
    ...
  ]
}
```

### 字段

* **`formatVersion`**：锁文件格式版本号（当前为 `4`）。
* **`formatUuid`**：用于确保文件类型正确的唯一标识。
* **`workspace`**：锁文件所属工作区的绝对路径。
* **`platform`**：工作区的平台。lip 写入 `"server"`；从 `tooth_lock.json` 迁移而来的锁在 lip 保存之前为 `null`。
* **`rid`**：工作区的 [.NET RID](https://learn.microsoft.com/zh-cn/dotnet/core/rid-catalog)。
* **`packages`**：已安装的包，按安装顺序排列。

### 包条目

* **`name`**：包的身份：其 `manifest.json` 的 `name`，或 `tooth.json` 包的 `tooth` 路径，带标签的变体在其后加 `#<label>`（例如 `github.com/LiteLDev/LegacyScriptEngine#lua`）。早期版本写入的锁对带标签的变体只记录了 `tooth` 路径；lip 读取时会补上标签。
* **`version`**：已安装的版本。
* **`kind`**：`manifest.json` 包为 `"mcmod"`，`tooth.json` 包为 `"tooth"`。两者的安装与卸载方式相同；kind 说明 `manifest` 中保存的是什么。
* **`explicit`**：
  * `true`：用户显式安装（如 `lip install <package>`）。
  * `false`：作为其他包依赖被隐式安装。
* **`chain`**：包的解析链，每一跳一项，包含：
  * `ref`：该跳解析时的 ref，例如 `github.com/user/repo`、[git 引用](../cli/commands/install.md#git-引用)形式的 `github.com/user/repo@branch:main`、`https://` URL、上一归档内的 `./sub/`，或传给 `lip install` 的本地文件的 `file:///` URI；
  * `artifact`：在该跳选中的路由条目下标（该跳不是路由文档时为 `null`）；
  * `commit`（仅包路径）：该跳仓库所在的提交：版本标签的提交，或 git 引用解析到的提交。早期版本写下的锁中没有此项。

  [`lip update`](../cli/commands/update.md) 会重新请求第一跳：分支跟随到新的头，标签重新解析，rev 保持不变。
* **`archiveKey`**：最终归档的键，例如 `github.com/user/repo@1.0.0`；带 git 引用的包路径为 `github.com/user/repo@rev:<commit>`。
* **`archive`**：最终归档的标识：
  * `{ "kind": "zip", "sha256": ..., "url": ... }`：ZIP 归档。`url` 是下载它的 `https://` URL、本地文件的 `file:///` URI，或对嵌套在其他归档中的 ZIP 文件为 `null`；包是 ZIP 文件内的一个目录时，会设置 `path`。
  * `{ "kind": "git", "repo": ..., "commit": ..., "path": ... }`：git 仓库的一个提交；仓库根目录的 `path` 为 `""`。
  * `null`：从 `tooth_lock.json` 迁移而来的条目。

  lip 再次需要已安装包的文件时，会打开该归档并校验其标识：git 归档按记录的提交获取（即使它来自一个已经前进的分支），下载的或本地的 ZIP 文件必须仍具有记录的 SHA-256。卸载则两者都不需要。
* **`files`**：该包拥有的文件，相对于工作区根目录，以 `/` 分隔。服务端的 `plugins` 是指向 `mods` 的链接，因此 `plugins/` 下的文件记为 `mods/` 下的路径；早期版本写下的锁中的 `plugins/` 路径会在加载时被改写。
* **`manifest`**：包的文档（`manifest.json`；`tooth` 包为 `tooth.json`，lip 需要该包时会再次把它转换为第 4 版），这样 lip 无需再次获取。

## 示例

```json
{
  "name": "LegacyMoney",
  "version": "0.22.0",
  "kind": "mcmod",
  "explicit": true,
  "chain": [
    { "ref": "github.com/LiteLDev/LegacyMoney", "artifact": 0 },
    {
      "ref": "https://github.com/LiteLDev/LegacyMoney/releases/download/v0.22.0/LegacyMoney-server-win-x64.mcmod",
      "artifact": null
    }
  ],
  "archiveKey": "https://github.com/LiteLDev/LegacyMoney/releases/download/v0.22.0/LegacyMoney-server-win-x64.mcmod",
  "archive": {
    "kind": "zip",
    "sha256": "…",
    "url": "https://github.com/LiteLDev/LegacyMoney/releases/download/v0.22.0/LegacyMoney-server-win-x64.mcmod"
  },
  "files": [
    "mods/LegacyMoney/LegacyMoney.dll",
    "mods/LegacyMoney/manifest.json"
  ],
  "manifest": { "...": "..." }
}
```

## 旧格式：tooth_lock.json

lip v0.x 在工作区根目录使用 `tooth_lock.json`（格式 3）。若数据目录中没有锁、工作区中也没有 `lip_lock.json`，但有 `tooth_lock.json`，lip 会在加载时迁移它，在下一次保存时把锁写入数据目录并删除 `tooth_lock.json`。迁移而来的条目类型为 `"tooth"`，没有 `archive`；当 lip 需要它们的文件时，会重新解析标签 `path@version`。

旧格式由 [`workspace_state.schema.json`](https://github.com/LiteLDev/lip/blob/main/schemas/workspace_state.schema.json) 描述：

```json
{
  "format_version": 3,
  "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
  "packages": [
    {
      "manifest": { "...": "the tooth.json" },
      "variant": "",
      "locked": true,
      "files": ["bin/example.exe"]
    }
  ]
}
```

* **`manifest`**：包的完整 `tooth.json`。
* **`variant`**：已安装变体的标签。
* **`locked`**：包为显式安装时为 `true`。
* **`files`**：包写入的文件。
