# 锁文件

`lip` 使用锁文件（工作区根目录下的 `lip_lock.json`）来维护当前工作区状态。该文件会精确记录已安装的包、具体版本、包的来源，以及写入磁盘的文件。

## 作用

锁文件在 `lip` 中有几个关键用途：

1. **状态追踪**：作为工作区当前安装状态的唯一真实来源。
2. **可复现性**：记录每个已安装包的确切归档。当 lip 再次需要某个包时（例如卸载它），会直接打开该归档并校验其标识，而不是重新解析该包。
3. **卸载清理**：`lip` 会追踪包写入的每个文件，使 `lip uninstall` 能干净删除相关文件，避免工作区出现“陈旧文件”。
4. **依赖图**：区分用户显式安装的包和作为依赖隐式安装的包。这样 `lip uninstall` 会一并移除不再被任何显式安装的包需要的依赖（除非指定了 `--no-dependencies`），`lip list` 也能分组显示这两类包。
5. **环境**：一旦确定了工作区的平台和 RID，就记录下来，使之后的命令使用相同的[环境](package_manifest.md#环境)。

每安装或卸载一个包，锁文件就保存一次，因此即使后续步骤失败，它也与工作区一致。

## 结构

锁文件是一个 JSON 文件，由 JSON Schema [`lock.v4.schema.json`](https://github.com/LiteLDev/lip/blob/main/schemas/lock.v4.schema.json) 描述：

```json
{
  "formatVersion": 4,
  "formatUuid": "0c6b977a-64ad-441f-9a29-c7f4b196882f",
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
* **`platform`**：工作区的平台。lip 写入 `"server"`；从 `tooth_lock.json` 迁移而来的锁在 lip 保存之前为 `null`。
* **`rid`**：工作区的 [.NET RID](https://learn.microsoft.com/zh-cn/dotnet/core/rid-catalog)。
* **`packages`**：已安装的包，按安装顺序排列。

### 包条目

* **`name`**：包的身份：其 `manifest.json` 的 `name`，或 `tooth.json` 包的 `tooth` 路径。
* **`version`**：已安装的版本。
* **`kind`**：`manifest.json` 包为 `"mcmod"`，`tooth.json` 包为 `"tooth"`。
* **`explicit`**：
  * `true`：用户显式安装（如 `lip install <package>`）。
  * `false`：作为其他包依赖被隐式安装。
* **`chain`**：包的解析链：每一跳的 `ref`，以及在该跳选中的路由条目下标 `artifact`（该跳不是路由文档时为 `null`）。
* **`archiveKey`**：最终归档的键，例如 `github.com/user/repo@1.0.0`。
* **`archive`**：最终归档的标识：
  * `{ "kind": "zip", "sha256": ..., "url": ... }`：ZIP 归档。嵌套在其他归档中的 ZIP 文件，其 `url` 为 `null`；包是 ZIP 文件内的一个目录时，会设置 `path`。
  * `{ "kind": "git", "repo": ..., "commit": ..., "path": ... }`：git 仓库的一个提交；仓库根目录的 `path` 为 `""`。
  * `{ "kind": "directory", "path": ... }`：本地目录。
  * `null`：从 `tooth_lock.json` 迁移而来的条目。
* **`files`**：该包拥有的文件，相对于工作区根目录，以 `/` 分隔。
* **`manifest`**：包的文档（`manifest.json`；`tooth` 包为 `tooth.json`），这样 lip 无需再次获取。

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
    "plugins/LegacyMoney/LegacyMoney.dll",
    "plugins/LegacyMoney/manifest.json"
  ],
  "manifest": { "...": "..." }
}
```

## 旧格式：tooth_lock.json

lip v0.x 使用 `tooth_lock.json`（格式 3）。若工作区没有 `lip_lock.json` 但有 `tooth_lock.json`，lip 会在加载时迁移它，在下一次保存时写入 `lip_lock.json` 并删除 `tooth_lock.json`。迁移而来的条目类型为 `"tooth"`，没有 `archive`；当 lip 需要它们的文件时，会重新解析标签 `path@version`。

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
