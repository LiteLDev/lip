# lip uninstall

从工作区卸载包。

## 概要

```
lip uninstall <PACKAGES>... [-n | --dry-run] [-h | --help]
```

## 说明

`lip uninstall` 卸载 `<PACKAGES>` 指名的已安装包，作为一次[工作区操作](../cli.md#工作区操作)。

`<PACKAGES>` 中的每一项按下面第一条能匹配到已安装包的规则来指名已安装的包：

1. **按身份。** 操作数等于 [`lip list`](list.md) 显示的身份（不含版本），例如 `github.com/LiteLDev/LegacyScriptEngine#lua`。
2. **按来源。** 把操作数当作[包参数](../cli.md#包参数)读取后，等于该包安装时的来源，例如 `github.com/user/repo@branch:main` 或 `./package.zip`。仓库名不区分大小写。不带变体的操作数匹配所有变体，不带引用的操作数匹配所有引用。

和 `lip install` 一样，不含 `/` 的名字（如 `LeviLamina`）是 LiteLDev 的仓库。

它做的事：

- 被指名的包不再是显式安装的包。
- 不再被任何剩余的显式安装包需要的包都会被卸载：运行它的 `pre_uninstall` 钩子，删除它拥有的文件（`preserve` 保留的除外），删除 `purge` 指定的路径，再运行它的 `post_uninstall` 钩子。
- 被指名的包如果仍被其他显式安装的包需要，就作为依赖包继续保留。

它在做出任何更改之前拒绝的情况：

- 操作数没有指名任何已安装的包。
- 被指名的包不是显式安装的。请改为卸载需要它的那个包。

游戏和加载器像其他包一样卸载。游戏被卸载后，工作区就没有游戏了：在安装模组或运行服务器之前，请用 [`lip setup`](setup.md) 重新设置。卸载游戏会保留 `server.properties`、`allowlist.json` 和 `permissions.json`；参见[服务器文件](../files.md#服务器文件)。

## 选项

| 选项 | 说明 |
| --- | --- |
| `-n`、`--dry-run` | [试运行](../cli.md#试运行)。消息会列出将被卸载或标记为不再显式安装的包。不做任何更改。 |
| `-h`、`--help` | 输出帮助并退出。 |

## 输出

`lip uninstall` 没有结果：它不向标准输出写任何内容。进度和最后一行都是写到标准错误的消息。

## 退出状态

| 退出码 | 含义 |
| --- | --- |
| `0` | 包已卸载，或试运行已结束。 |
| 非 `0` | 用法错误，或命令失败。失败的操作会被撤销。 |

## 示例

按身份卸载一个包：

```shell
lip uninstall github.com/LiteLDev/LegacyScriptEngine#lua
```

查看卸载两个包会移除什么：

```shell
lip uninstall github.com/user/repo github.com/user/other --dry-run
```

卸载一个从本地 ZIP 文件安装的包：

```shell
lip uninstall ./package.zip
```

## 另请参阅

- [管理包](../../guides/managing_packages.md)
- [模组](../../interface/mods.md)：在界面中卸载模组。
- [`lip list`](list.md)、[`lip install`](install.md)、[`lip setup`](setup.md)
