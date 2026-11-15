# lip pkg pack

把一个目录打包成 ZIP 归档。

## 概要

```
lip pkg pack [<DIR>] (-o | --output) <FILE> [-h | --help]
```

## 说明

`lip pkg pack` 向 `<FILE>` 写入一个 ZIP 归档（文件已存在时替换它），其中包含 `<DIR>` 下的每个普通文件，路径为它相对于 `<DIR>` 的路径。

`<DIR>` 是要打包的目录；省略时为当前工作目录。它不是一个存在的目录时，命令失败，不写入任何内容。

不会放进归档的内容：

- `<FILE>` 自身（当它位于 `<DIR>` 之下时）；
- `<DIR>` 根部的 `.git/` 和 `.lip/` 下的所有内容。

文件按原样打包。`lip pkg pack` 不读取也不检查 `lip.toml` 或 `manifest.json`，所以任何目录都能打包。

ZIP 归档中的名字不能含有 `\`。在 Linux 和 macOS 上，如果要打包的文件或其上级目录的名字中含有 `\`，命令失败，不写入 `<FILE>`。

它可以用来构建：

- **`mcmod` 资源的归档**：打包根部有模组的 LeviLamina `manifest.json` 的目录，例如你的构建输出目录。把归档附加到你的发布中，并把它的 URL 写进该资源的 `urls`。
- **单个 ZIP 文件形式的包**：打包根部有 `lip.toml` 的目录，用 [`lip install`](install.md) 安装它，或者把它发布到某个 URL。

`lip pkg pack` 不是工作区操作。

## 选项

| 选项 | 说明 |
| --- | --- |
| `-o <FILE>`、`--output <FILE>` | 要写入的 ZIP 归档的路径。必需。 |
| `-h`、`--help` | 输出帮助并退出。 |

## 输出

`lip pkg pack` 没有结果：它不向标准输出写任何内容。最后一行是写到标准错误的消息。

## 退出状态

| 退出码 | 含义 |
| --- | --- |
| `0` | 归档已写入。 |
| 非 `0` | 用法错误（例如缺少 `-o`）；`<DIR>` 不是目录；或某个文件无法读取或写入。 |

## 示例

把 `build/` 中的文件打包成模组的 `mcmod` 归档：

```shell
lip pkg pack build/ -o MyMod-server-win-x64.zip
```

打包当前目录中的包，然后安装到一个服务器中进行测试：

```shell
lip pkg pack -o ../MyTool.zip
cd ../server
lip install ../MyTool.zip
```

## 另请参阅

- [创建包](../../authors/creating_packages.md)
- [工具](../../interface/tools.md)：界面中的**打包 ZIP…**。
- [`lip pkg view`](pkg_view.md)、[`lip install`](install.md)
