# 工作区文件

`lip` 在工作区的**工作区文件**中记录工作区状态：安装了哪些包、它们的确切版本、来源，以及它们写入磁盘的文件。

## 位置

工作区文件是工作区根目录下的 `.lip/workspace.json`。在 Windows 上，lip 创建 `.lip` 目录时会为其设置隐藏属性；在其他平台上，它的名称本身就使其隐藏。

由于工作区文件位于工作区目录之中，而不是与其路径绑定，移动、重命名或复制工作区后，工作区文件会随之保留，已安装的包也随之保留。

`.lip` 为 lip 保留：安装的包若要把文件写入工作区根目录下的 `.lip` 或其下任何位置，安装会失败；`remove_files` 模式也永远不会匹配 `.lip` 及其下的任何内容。

## 作用

工作区文件有以下几个用途：

1.  **状态追踪**：作为工作区安装状态的唯一真实来源。
2.  **可复现性**：记录每个已安装包的确切归档和资源，以及安装的条目。当 lip 再次需要某个包时，会打开该归档和这些资源并检查它们未被改变，而不是重新解析该包。
3.  **卸载**：`lip` 追踪包写入磁盘的每个文件，使 `lip uninstall` 能干净地删除与包相关的所有文件，避免工作区中残留"腐烂文件"。
4.  **依赖图**：区分用户显式安装的包和作为依赖安装的包。这样 `lip uninstall` 也能移除不再被任何显式安装的包需要的依赖（除非指定了 `--no-dependencies`），`lip list` 也能把两组分开显示。
5.  **环境**：一旦得知工作区的平台和 RID，就将其记录下来，使后续命令使用同一[环境](tooth_json.md#环境)。

工作区文件在一次操作的所有包都安装或卸载完成后才替换一次。若操作失败，lip 会撤销其对工作区的修改，工作区文件保持原样，因此两者始终一致（参见[内部机制](../guides/internal_mechanism.md#流程)）。

## 结构

工作区文件是一个 JSON 文件，由 JSON Schema [`workspace.schema.json`](https://github.com/LiteLDev/lip/blob/main/protocol/workspace.schema.json) 描述：

```json
{
  "format_version": 4,
  "platform": "server",
  "runtime": "win-x64",
  "packages": [
    ...
  ]
}
```

### 字段

*   **`format_version`**：工作区文件的格式，`4`。
*   **`platform`**：工作区的平台，在得知之前为 `null`。lip 写入 `"server"`。
*   **`runtime`**：工作区的 [.NET RID](https://learn.microsoft.com/dotnet/core/rid-catalog)，在得知之前为 `null`。
*   **`packages`**：已安装的包，按安装顺序排列：一个包排在满足其依赖的包之后。

### 包条目

*   **`tooth`**：包的 tooth 路径，来自其 `tooth.json`。
*   **`variant`**：安装来源的变体，默认变体为 `""`。它与 `tooth` 一起构成包的[身份](tooth_json.md#身份)。
*   **`version`**：已安装的版本。
*   **`explicit`**：
    *   `true`：该包由用户显式安装（例如 `lip install <package>`）。
    *   `false`：该包作为另一个包的依赖安装。
*   **`source`**：包的安装来源：传给 `lip install` 或 `lip update` 的参数（例如 `github.com/user/repo`、`github.com/user/repo#lua@branch:main` 或 `https://` URL；本地路径记录为其绝对路径的 `file:` URI），对于依赖则是该依赖的键。[`lip update`](../cli/commands/update.md) 会再次请求它。
*   **`archive`**：包的归档：
    *   `{ "kind": "git", "repo": ..., "commit": ... }`：git 仓库的一个提交；`repo` 为 `https://<repo>.git`。
    *   `{ "kind": "zip", "sha256": ..., "url": ... }`：ZIP 归档。`url` 是下载它的 `https://` URL（不含 `#<variant>`），或本地文件的 `file:` URI。
    *   `{ "kind": "directory", "path": ... }`：本地目录，以其绝对路径表示。
*   **`assets`**：条目的每个资源按顺序各对应一个成员：`self` 资源为 `null`，下载的资源为 `{ "url": ..., "sha256": ... }`，`url` 是下载它的 URL。
*   **`entry`**：安装的 `tooth.json` 条目。对于[早期格式](tooth_json.md#早期格式)的 `tooth.json`，它是 lip 将其读取成的格式 4 条目。
*   **`files`**：包拥有的文件，相对于工作区根目录，以 `/` 分隔。由于在服务端 `plugins` 是指向 `mods` 的链接，`plugins/` 下的文件会记录在 `mods/` 下。

SHA-256 值和提交 ID 以小写十六进制写入。

当 lip 再次需要某个已安装包的文件时，它安装工作区文件所记录的内容，而不论来源现在指向什么：git 归档按记录的提交获取（即使它来自一个已前进的分支），ZIP 归档或资源必须仍具有记录的 SHA-256，否则操作失败。卸载不需要两者中的任何一个。

## 示例

```json
{
  "tooth": "github.com/example/MyMod",
  "variant": "",
  "version": "1.0.0",
  "explicit": true,
  "source": "github.com/example/MyMod",
  "archive": {
    "kind": "git",
    "repo": "https://github.com/example/MyMod.git",
    "commit": "0123456789abcdef0123456789abcdef01234567"
  },
  "assets": [
    {
      "url": "https://github.com/example/MyMod/releases/download/v1.0.0/MyMod-server-win-x64.zip",
      "sha256": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
    }
  ],
  "entry": {
    "platform": "server",
    "runtime": "win-x64",
    "dependencies": { "github.com/LiteLDev/LeviLamina": ">=1.0.0 <2.0.0" },
    "assets": [
      {
        "type": "mcmod",
        "urls": ["https://github.com/example/MyMod/releases/download/v1.0.0/MyMod-server-win-x64.zip"]
      }
    ]
  },
  "files": [
    "mods/MyMod/MyMod.dll",
    "mods/MyMod/manifest.json"
  ]
}
```

## 早期格式

lip 不接管早期 lip 版本的工作区：`format_version` 不为 4 的 `.lip/workspace.json`、lip v0.36 的 `.lip/lock.json`，以及 lip v0.x 保存在工作区根目录下的 `tooth_lock.json`。在含有其中之一的工作区中，lip 会给出提示并失败，而不会安装、卸载、更新或列出包，以免覆盖这些包拥有的文件。请用安装这些包的 lip 版本卸载它们，或删除该文件重新开始；此后 lip 对它们安装的文件一无所知。
