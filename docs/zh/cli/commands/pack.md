# lip pack

把当前目录中的包文档打包为 `.mcmod` 文件，或将其输出。

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
| `-o, --output <FILE>` | 把 `.mcmod` 文件写入 `<FILE>`，而不是输出包文档。 |

## 说明

读取当前目录中的 `manifest.json`，它必须是 `bf45b1` 格式的包文档（参见[包清单](../../concepts/package_manifest.md)）。元文档不能打包。

不带 `--output` 时，包文档以 JSON 形式输出到标准输出。带 `--output` 时，lip 把一个 ZIP 归档写入 `<FILE>`（替换已有文件）：包文档作为其根部的 `manifest.json`，`DIR` 下的每个文件按其相对于 `DIR` 的路径放入。`DIR` 根部的 `manifest.json`、`DIR` 根部 `.git/` 和 `.lip/` 中的全部内容，以及 `<FILE>` 本身不会放入。

当 `manifest.json` 不存在或不是有效的包文档时，命令会失败，且不写入任何内容。

请把每个 `.mcmod` 文件发布到你的元文档中某个 target 的 `source` 所指的位置，例如作为 release 资源。参见[创建包](../../guides/creating_packages.md#指向发布资源)。

## 示例

检查并输出包文档：

```shell
lip pack
```

把 `build/` 中的文件打包为 `.mcmod` 文件：

```shell
lip pack build/ -o LegacyMoney-server-win-x64.mcmod
```
