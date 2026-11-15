# lip pkg view

以 JSON 显示包的包文件和版本。

## 概要

```
lip pkg view <PACKAGE> [-h | --help]
```

## 说明

`<PACKAGE>` 是一个[包参数](../cli.md#包参数)，形式与 [`lip install`](install.md) 接受的相同：仓库（可以带 `#<variant>` 和 `@<ref>`）、`https://` URL，或本地的 ZIP 文件或目录。

`lip pkg view` 读取该参数首先指名的包，输出它的包文件和所在仓库的版本：

| 参数 | 显示的包 |
| --- | --- |
| `<repo>` | 仓库在最高版本标签处的内容。 |
| `<repo>@<version>` | 仓库在标签 `v<version>` 处的内容。 |
| `<repo>@branch:<name>`、`@tag:<name>`、`@rev:<commit>` | 仓库在该提交处的内容。 |
| URL 或本地路径 | 该归档本身。 |

无法取得该归档，或者它不是包时，命令失败。

lip v0.x 的 `tooth.json` 会显示为 lip 针对运行端 `server` 和平台 `win-x64` 从中读出的包文件。参见[早期格式](../../concepts/lip_toml.md#早期格式)。

除了保存下载内容的缓存之外，`lip pkg view` 不更改任何东西。它不是工作区操作，也不需要服务器。

## 选项

| 选项 | 说明 |
| --- | --- |
| `-h`、`--help` | 输出帮助并退出。 |

没有 `--json` 选项：结果始终是 JSON。

## 输出

结果写到标准输出：一个恰好包含下列成员的 JSON 对象，后跟一个换行。

```json
{ "manifest": { ... }, "versions": ["<version>", ...] }
```

| 成员 | 值 |
| --- | --- |
| `manifest` | 该包的包文件 `lip.toml`，作为 JSON 对象，其中的[模板](../../concepts/lip_toml.md#模板)已渲染。 |
| `versions` | 对于仓库：它的版本标签（名为 `v` 加 SemVer 版本的标签）所对应的版本，不带 `v`，从低到高，每个只出现一次。参数中的变体和引用不影响这个列表。对于 URL 或本地路径：空数组。 |

例如，对一个模组的目录执行 `lip pkg view ./MyMod`：

```json
{
  "manifest": {
    "format": 4,
    "id": "github.com/user/MyMod",
    "version": "1.0.0",
    "variants": {
      "": [
        {
          "side": "server",
          "platform": "win-x64",
          "deps": {
            "github.com/LiteLDev/LeviLamina": ">=1.0.0 <2.0.0"
          },
          "assets": [
            {
              "type": "mcmod",
              "urls": [
                "https://github.com/user/MyMod/releases/download/v1.0.0/MyMod-server-win-x64.zip"
              ]
            }
          ]
        }
      ]
    }
  },
  "versions": []
}
```

## 退出状态

| 退出码 | 含义 |
| --- | --- |
| `0` | 结果已输出。 |
| 非 `0` | 用法错误；参数无效；或无法取得归档，或者它不是包。 |

## 示例

查看一个包的最新版本，并列出它的版本：

```shell
lip pkg view github.com/LiteLDev/LeviLamina
```

用 [jq](https://jqlang.org/) 只列出版本：

```shell
lip pkg view github.com/LiteLDev/LeviLamina | jq -r '.versions[]'
```

查看指定版本，以及某个分支的最新提交：

```shell
lip pkg view github.com/user/repo@1.0.0
lip pkg view github.com/user/repo@branch:main
```

检查 lip 能否读取你正在编写的 `lip.toml`：

```shell
lip pkg view .
```

## 另请参阅

- [创建包](../../authors/creating_packages.md#本地测试)
- [工具](../../interface/tools.md)：界面中的**查看包信息…**。
- [lip.toml](../../concepts/lip_toml.md)
