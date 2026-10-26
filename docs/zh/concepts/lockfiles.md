# 锁文件

`lip` 使用锁文件（工作区根目录下的 `tooth_lock.json`）来维护当前工作区状态。该文件会精确记录已安装的包、具体版本、所用变体，以及写入磁盘的文件。

## 作用

锁文件在 `lip` 中有几个关键用途：

1. **状态追踪**：作为工作区当前安装状态的唯一真实来源。
2. **可复现性**：记录每个包的精确版本和变体，确保工作区状态可确定、可复现。
3. **卸载清理**：`lip` 会追踪包写入的每个文件，使 `lip uninstall` 能干净删除相关文件，避免工作区出现“陈旧文件”。
4. **依赖图**：区分用户显式安装的包和作为依赖隐式安装的包。这样 `lip uninstall` 会一并移除不再被任何显式安装的包需要的依赖（除非指定了 `--no-dependencies`），`lip list` 也能分组显示这两类包。

## 结构

锁文件是一个 JSON 文件，顶层结构如下：

```json
{
  "format_version": 3,
  "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
  "packages": [
    ...
  ]
}
```

### 字段

* **`format_version`**：锁文件格式版本号（当前为 `3`）。
* **`format_uuid`**：用于确保文件类型正确的唯一标识。
* **`packages`**：`WorkspaceStatePackage` 对象列表，表示已安装包。

### 包条目

`packages` 中每个条目代表一个已安装包，包含：

* **`manifest`**：包清单（`tooth.json`）完整内容。这样 `lip` 无需再次查询包的仓库也能获取已安装包元数据。
* **`variant`**：实际安装的包变体标签（如 `debug`）。默认变体的标签为空字符串 `""`。
* **`locked`**：布尔标记。
  * `true`：用户显式安装（如 `lip install <package>`）。
  * `false`：作为其他包依赖被隐式安装。
* **`files`**：属于该包的文件路径列表（相对工作区根目录）。卸载时据此删除包文件。

## 示例

`github.com/example/cli` 包的条目示例：

```json
{
  "manifest": {
    "format_version": 3,
    "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
    "tooth": "github.com/example/cli",
    "version": "1.0.0",
    "info": {
      "name": "Example CLI",
      "description": "An example CLI tool",
      "tags": [],
      "avatar_url": ""
    },
    "variants": [
      ...
    ]
  },
  "variant": "",
  "locked": true,
  "files": [
    "bin/example.exe",
    "lib/example.dll"
  ]
}
```
