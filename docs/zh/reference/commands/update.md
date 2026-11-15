# lip update

更新已安装的包。

## 概要

```
lip update [<PACKAGES>...] [-n | --dry-run] [-h | --help]
```

## 说明

`lip update` 重新请求已安装的包，并替换其中发生了变化的，作为一次[工作区操作](../cli.md#工作区操作)。

**不带操作数时**，每个显式安装的包都按它安装时的来源重新请求，来源记录在[状态文件](../../concepts/state_file.md)中：

| 安装时的来源 | `lip update` 安装的内容 |
| --- | --- |
| `<repo>` | 最高的版本标签。 |
| `<repo>@<version>` | 同一个版本：没有变化。 |
| `<repo>@branch:<name>` | 该分支当前指向的提交。 |
| `<repo>@tag:<name>` | 该标签当前指向的提交。 |
| `<repo>@rev:<commit>` | 同一个提交：没有变化。 |
| `https://` URL | 重新下载的 ZIP 归档。 |
| 本地 ZIP 文件或目录 | 重新读取同一路径。路径已不存在时，包保持原样。 |

**带操作数时**，`<PACKAGES>` 中的每一项都是[包参数](../cli.md#包参数)，形式与 [`lip install`](install.md) 接受的相同；只有这些包会按你给出的来源重新请求。更换一个包的来源就是这样做的：

- `lip update github.com/user/repo@2.0.0` 把包换到 2.0.0 版本，升级或降级都可以。
- `lip update github.com/user/repo` 把从某个版本或引用安装的包换回最高的版本标签。

被请求的包成为显式安装的包。

两种情况下都有：

- 只有当得到的包与已安装的包在身份、版本或提交上不同时，才会替换它。来自 URL 或本地路径的包没有提交，所以只有版本变了才会被替换。
- 替换一个包就是先卸载旧的，再安装新的。
- 依赖包只在新的包需要时才更新。

它在做出任何更改之前拒绝的情况：

- 操作数对应的包没有安装。请用 [`lip install`](install.md)。
- 操作数指名了 `github.com/LiteLDev/bds` 或 `github.com/LiteLDev/LeviLamina`。消息会指向 `lip setup`。
- 在游戏和加载器保持不变的前提下无法完成的更新。

`lip update` 保持游戏和加载器不变：不带操作数时不会重新请求它们。要更换游戏版本或 LeviLamina 版本，请运行 [`lip setup`](setup.md)。

## 选项

| 选项 | 说明 |
| --- | --- |
| `-n`、`--dry-run` | [试运行](../cli.md#试运行)。消息会列出将被安装、卸载或标记为显式安装的包。不做任何更改。 |
| `-h`、`--help` | 输出帮助并退出。 |

## 输出

`lip update` 没有结果：它不向标准输出写任何内容。进度和最后一行都是写到标准错误的消息。

## 退出状态

| 退出码 | 含义 |
| --- | --- |
| `0` | 更新已完成（包括没有任何东西需要更改的情况），或试运行已结束。 |
| 非 `0` | 用法错误，或更新失败。失败的更新会被撤销。 |

## 示例

先看看更新所有包会做什么，再执行：

```shell
lip update --dry-run
lip update
```

把一个包更新到最新版本：

```shell
lip update github.com/user/repo
```

把一个包换到指定版本：

```shell
lip update github.com/user/repo@1.2.0
```

## 另请参阅

- [管理包](../../guides/managing_packages.md)
- [模组](../../interface/mods.md)：在界面中更新模组。
- [`lip install`](install.md)、[`lip setup`](setup.md)、[`lip list`](list.md)
