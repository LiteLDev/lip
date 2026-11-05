# lip pack

把当前目录中的 `manifest.json` 打包为包文档或 `.mcmod` 文件。

## 概要

```shell
lip pack [DIR] [options]
```

## 参数

| 参数 | 说明 |
| --- | --- |
| `[DIR]` | 使用 `--output` 时，其中的文件会放入 `.mcmod` 文件的目录。默认为当前目录。 |

## 选项

| 选项 | 说明 |
| --- | --- |
| `--variant <VARIANT>` | 要打包的变体。默认为 `""`，即默认变体。 |
| `--platform <PLATFORM>` | 打包的目标平台：`server` 或 `client`。默认为 `server`。 |
| `--rid <RID>` | 打包的目标 .NET RID，例如 `win-x64`。默认为 lip 自身运行时的 RID。 |
| `-o, --output <FILE>` | 把 `.mcmod` 文件写入 `<FILE>`，而不是输出包文档。 |

## 说明

读取当前目录中的 `manifest.json`（必须带有 `"format": "bf45b1"`，参见[包清单](../../concepts/package_manifest.md)），并把它转换为某个平台和 RID 的包文档：

- 对于元文档，lip 像 `lip install` 那样按 `--variant`、`--platform` 和 `--rid` 选出 target，并对其[打包](../../concepts/package_manifest.md#打包)：文档的其余键以 target 的键补全，target 写出 `platform` 和 `runtime` 时还会设置这两个键。
- 对于包文档，结果就是文档本身：此时 `--variant` 必须为空或不指定，`--platform` 和 `--rid` 不起作用。

不带 `--output` 时，包文档以 JSON 形式输出到标准输出。带 `--output` 时，lip 把一个 ZIP 归档写入 `<FILE>`（替换已有文件）：包文档作为其根部的 `manifest.json`，`DIR` 下的每个文件按其相对于 `DIR` 的路径放入。`DIR` 根部的 `manifest.json`、`DIR` 根部 `.git/` 和 `.lip/` 中的全部内容，以及 `<FILE>` 本身不会放入。

当 `manifest.json` 不存在或不是有效的 `bf45b1` 格式文档、没有或有多个 target 匹配该变体、平台和 RID，或者结果不是有效的包文档时，命令会失败，且不写入任何内容。

请把每个 `.mcmod` 文件发布到其 target 的 `source` 所指的位置，例如作为 release 资源。参见[创建包](../../guides/creating_packages.md#指向发布资源)。

## 示例

输出 `win-x64` 服务端的包文档：

```shell
lip pack --rid win-x64
```

把 `build/` 中的文件打包为某个 mod 的服务端包和客户端包：

```shell
lip pack build/ --platform server --rid win-x64 -o LegacyMoney-server-win-x64.mcmod
lip pack build/ --platform client --rid win-x64 -o LegacyMoney-client-win-x64.mcmod
```
