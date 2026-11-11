# lip pkg pack

把一个目录打包为 ZIP 归档。

## 概要

```shell
lip pkg pack [DIR] -o <FILE>
```

## 参数

| 参数 | 说明 |
| --- | --- |
| `[DIR]` | 其文件要放入归档的目录。默认为当前目录。 |

## 选项

| 选项 | 说明 |
| --- | --- |
| `-o, --output <FILE>` | 要写入的 ZIP 归档。必填。 |

## 说明

向 `<FILE>` 写入一个 ZIP 归档（若已存在则替换），其中包含 `DIR` 之下的每个普通文件，路径为其相对于 `DIR` 的路径。`<FILE>` 本身，以及 `DIR` 根部 `.git/` 和 `.lip/` 中的所有内容，都不会放入。文件按原样打包：`lip pkg pack` 不读取也不检查 `lip.toml` 或 `manifest.json`。若 `DIR` 不是已存在的目录，命令会失败且不写入任何内容。

ZIP 归档中的名称不能含有 `\`，因此在 Linux 和 macOS 上，若要打包的文件或其上层目录的名称含有 `\`，`lip pkg pack` 会失败，且不写入 `<FILE>`。请先将其重命名。

可用它构建：

- `mcmod` 资源的归档：从根部带有该 mod 的 LeviLamina `manifest.json` 的目录（例如构建输出目录）打包。把它附加到 Release，并把其 URL 列入该资源的 `urls`。参见 [mod](../../guides/creating_packages.md#mod)。
- 单个 ZIP 文件形式的包：从根部带有 `lip.toml` 的目录打包，以便用 [`lip install`](install.md) 安装，或在某个 URL 上发布。

## 示例

把 `build/` 中的文件打包为一个 mod 的 `mcmod` 归档：

```shell
lip pkg pack build/ -o MyMod-server-win-x64.zip
```

打包当前目录中的包：

```shell
lip pkg pack -o MyTool.zip
```
