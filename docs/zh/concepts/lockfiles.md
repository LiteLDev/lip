# 工作区文件

`lip` 在工作区的**工作区文件**中记录工作区状态：安装了哪些包、它们的确切版本、来源，以及它们写入磁盘的文件。

## 位置

工作区文件是工作区根目录下的 `.lip/workspace.json`。在 Windows 上，lip 创建 `.lip` 目录时会为其设置隐藏属性；在其他平台上，它的名称本身就使其隐藏。

由于工作区文件位于工作区目录之中，而不是与其路径绑定，移动、重命名或复制工作区后，工作区文件会随之保留，已安装的包也随之保留。

`.lip` 为 lip 保留：安装的包若要把文件写入工作区根目录下的 `.lip` 或其下任何位置，安装会失败；`purge` 模式也永远不会匹配 `.lip` 及其下的任何内容。

## 作用

工作区文件有以下几个用途：

1.  **状态追踪**：作为工作区安装状态的唯一真实来源。
2.  **可复现性**：记录安装每个已安装包时所用的 `tooth.json`，对于来自 git 仓库的包还记录其所在的提交。当 lip 再次需要某个包时，会用该 `tooth.json` 安装它，而不是重新解析该包。
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
*   **`platform`**：工作区的平台，`"server"` 或 `"client"`。lip 写入 `"server"`。
*   **`runtime`**：工作区的 [.NET RID](https://learn.microsoft.com/dotnet/core/rid-catalog)。
*   **`packages`**：已安装的包，按安装顺序排列：一个包排在满足其依赖的包之后。

`platform` 和 `runtime` 总是存在且从不为 `null`；缺少它们的工作区文件无效。没有工作区文件的工作区从其第一次 `install`、`uninstall` 或 `update` 获得环境（参见 [`lip install`](../cli/commands/install.md)）。

### 包条目

每个包恰好由以下成员记录：

*   **`manifest`**：安装该包时所用的完整 `tooth.json`，`format_version` 为 4。对于[早期格式](tooth_json.md#早期格式)的 `tooth.json`，它是 lip 针对工作区环境从中派生的格式 4 `tooth.json`。其 `tooth` 是包的 tooth 路径，`version` 是已安装的版本。
*   **`variant`**：安装来源的变体，默认变体为 `""`。它与 `manifest` 的 `tooth` 一起构成包的[身份](tooth_json.md#身份)。
*   **`explicit`**：
    *   `true`：该包由用户显式安装（例如 `lip install <package>`）。
    *   `false`：该包作为另一个包的依赖安装。
*   **`ref`**：包的安装来源：传给 `lip install` 或 `lip update` 的参数（例如 `github.com/user/repo`、`github.com/user/repo#lua@branch:main` 或 `https://` URL；本地路径记录为其绝对路径的 `file:` URI），对于依赖则是该依赖的键。[`lip update`](../cli/commands/update.md) 会再次请求它。
*   **`commit`**：仅用于来自 git 仓库的包：其归档所在的提交，以小写十六进制表示。来自 URL 或本地路径的包没有 `commit`。
*   **`files`**：包拥有的文件，相对于工作区根目录，以 `/` 分隔。由于在服务端 `plugins` 是指向 `mods` 的链接，`plugins/` 下的文件会记录在 `mods/` 下。

已安装的条目是 `manifest.variants[variant]` 中 `platform` 和 `runtime` 与工作区相同的那个条目。

lip 只按身份、版本和提交比较已安装的包与它解析出的包：身份和版本相同、且都不来自 git 仓库或来自同一提交的两个包是同一个包，即使 URL 或本地路径的文件此后已经改变。下载的 ZIP 归档或资源的校验和既不记录也不检查。

当 lip 再次需要某个已安装包的文件时，它用记录的 `manifest` 安装该包：对于来自 git 仓库的包，从记录的提交安装（即使它来自一个此后已前进的分支）；否则从其 `ref` 现在指向的归档安装。卸载不需要两者中的任何一个。

## 示例

```json
{
  "format_version": 4,
  "platform": "server",
  "runtime": "win-x64",
  "packages": [
    {
      "manifest": {
        "format_version": 4,
        "format_uuid": "f74a5fda-cb2a-4de8-959f-a1a76bad3102",
        "tooth": "github.com/example/MyMod",
        "version": "1.0.0",
        "variants": {
          "": [
            {
              "platform": "server",
              "runtime": "win-x64",
              "dependencies": { "github.com/LiteLDev/LeviLamina": ">=1.0.0 <2.0.0" },
              "assets": [
                {
                  "type": "mcmod",
                  "urls": ["https://github.com/example/MyMod/releases/download/v1.0.0/MyMod-server-win-x64.zip"]
                }
              ]
            }
          ]
        }
      },
      "variant": "",
      "explicit": true,
      "ref": "github.com/example/MyMod",
      "commit": "0123456789abcdef0123456789abcdef01234567",
      "files": [
        "mods/MyMod/MyMod.dll",
        "mods/MyMod/manifest.json"
      ]
    }
  ]
}
```
## 早期格式

lip 不接管早期 lip 版本的工作区：`format_version` 不为 4 的 `.lip/workspace.json`、lip v0.36 的 `.lip/lock.json`，以及 lip v0.x 保存在工作区根目录下的 `tooth_lock.json`。在含有其中之一的工作区中，lip 会给出提示并失败，而不会安装、卸载、更新或列出包，以免覆盖这些包拥有的文件。请用安装这些包的 lip 版本卸载它们，或删除该文件重新开始；此后 lip 对它们安装的文件一无所知。
