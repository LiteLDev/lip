# 包清单（Package Manifest）

包清单文件 `tooth.json` 用于定义一个 lip 包。

> [!TIP]
> <span v-pre>你可以在任意字符串字段中使用其他字段的变量：用 `{{}}` 包裹变量名，例如 `{{version}}` 和 `{{info.name}}`。例如，`{{version}}` 会被替换为 `version` 字段的值。</span>

## 示例

```json
{
  "format_version": 3,
  "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
  "tooth": "github.com/user/repo",
  "version": "1.0.0",
  "info": {
    "name": "My Package",
    "description": "A short description",
    "tags": ["tool", "utility"],
    "avatar_url": "https://example.com/avatar.png"
  },
  "variants": [
    {
      "label": "",
      "platform": "win-x64",
      "dependencies": {
        "github.com/other/package": ">=1.0.0"
      },
      "assets": [
        {
          "type": "zip",
          "urls": ["https://example.com/release.zip"],
          "placements": [
            {
              "type": "dir",
              "src": "bin/",
              "dest": "bin/"
            }
          ]
        }
      ],
      "preserve_files": ["config/*.json"],
      "remove_files": ["temp/"],
      "scripts": {
        "pre_install": ["echo pre-install"],
        "post_install": ["echo post-install"],
        "pre_uninstall": ["echo pre-uninstall"],
        "post_uninstall": ["echo post-uninstall"]
      }
    }
  ]
}
```

## 字段

### 顶层

| 字段 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `format_version` | `int` | 是 | 必须为 `3`。 |
| `format_uuid` | `string` | 是 | 必须为 `"289f771f-2c9a-4d73-9f3f-8492495a924d"`。 |
| `tooth` | `string` | 是 | 包路径，必须是合法的 [Go module path](https://go.dev/ref/mod#module-path)。 |
| `version` | `string` | 是 | 包版本，必须是合法的[语义化版本](https://semver.org/)。 |
| `info` | `object` | 否 | 包元数据。见 [Info](#info)。 |
| `variants` | `array` | 否 | 包变体。见 [Variant](#variant)。 |

### Info

| 字段 | 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `name` | `string` | `""` | 显示名称。 |
| `description` | `string` | `""` | 简短描述。 |
| `tags` | `string[]` | `[]` | 标签。每个标签必须匹配 `^[a-z0-9-]+(:[a-z0-9-]+)?$`。 |
| `avatar_url` | `string` | `null` | 头像图片 URL。 |

### Variant

变体用于定义平台相关或带标签的包配置。解析变体时，lip 会合并 `label` 与 `platform` 匹配的所有变体。

| 字段 | 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `label` | `string` | `""` | 变体标签。 |
| `platform` | `string` | `""` | 目标平台（[.NET RID](https://learn.microsoft.com/zh-cn/dotnet/core/rid-catalog)）。为空时匹配全部，支持 glob。 |
| `dependencies` | `object` | `{}` | 包 ID 到 [semver 范围](https://github.com/WalkerCodeRanger/semver#version-ranges) 的映射。 |
| `assets` | `array` | `[]` | 资源定义。见 [Asset](#asset)。 |
| `preserve_files` | `string[]` | `[]` | 卸载时保留文件的 glob 模式。模式按相对于工作目录的路径匹配，并回退到文件名匹配。 |
| `remove_files` | `string[]` | `[]` | 卸载时额外删除文件的 glob 模式。模式按相对于工作目录的路径匹配；以 `/` 结尾的模式匹配目录。 |
| `scripts` | `object` | `{}` | 生命周期脚本。见 [Scripts](#scripts)。 |

### Asset

| 字段 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `type` | `string` | 是 | 压缩包类型：`"self"`、`"tar"`、`"tgz"`、`"uncompressed"` 或 `"zip"`。 |
| `urls` | `string[]` | 否 | 下载 URL 列表。 |
| `placements` | `array` | 否 | 文件放置规则。见 [Placement](#placement)。 |

### Placement

| 字段 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `type` | `string` | 是 | `"file"` 或 `"dir"`。 |
| `src` | `string` | 是 | 压缩包内源路径。 |
| `dest` | `string` | 是 | 相对工作区的目标路径。不得为绝对路径、根路径或包含 `..`。 |

### Scripts

| 字段 | 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `pre_install` | `string[]` | `[]` | 安装前执行的命令。 |
| `install` | `string[]` | `[]` | `post_install` 的别名。包文件放置后执行。 |
| `post_install` | `string[]` | `[]` | 安装后执行的命令。 |
| `pre_uninstall` | `string[]` | `[]` | 卸载前执行的命令。 |
| `uninstall` | `string[]` | `[]` | `pre_uninstall` 的别名。删除已安装文件前执行。 |
| `post_uninstall` | `string[]` | `[]` | 卸载后执行的命令。 |

在新清单中，推荐使用 `post_install` 和 `pre_uninstall`。`install` 与 `uninstall` 作为兼容别名仍受支持，若两种形式同时存在，lip 会在对应阶段执行两组命令。
