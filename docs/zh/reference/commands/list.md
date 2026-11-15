# lip list

列出已安装的包。

## 概要

```
lip list [--json] [-h | --help]
```

## 说明

`lip list` 读取工作区的[状态文件](../../concepts/state_file.md)，把已安装的包以 `<identity>@<version>` 的形式分两组输出：

1. **显式安装**的包：你要求安装的包，包括游戏和加载器；
2. **依赖包**：因为其他包需要而安装的包。

显式安装的包排在前面。每组之内按安装顺序排列：一个包排在它所依赖的包之后。

包的**身份**是它的包 ID；变体不是默认变体时，后面再加上 `#<variant>`，例如 `github.com/LiteLDev/LegacyScriptEngine#lua`。[`lip uninstall`](uninstall.md) 接受的就是它。

`lip list` 不做任何更改，也不取得任何锁，所以其他命令或服务器运行时也能使用。没有状态文件的工作区，两组都是空的。lip v0.x 的工作区没有状态文件而有 `tooth_lock.json`，此时会根据该文件列出，并且不改动它。

## 选项

| 选项 | 说明 |
| --- | --- |
| `--json` | 以 JSON 输出结果。 |
| `-h`、`--help` | 输出帮助并退出。 |

## 输出

结果写到标准输出。不带 `--json` 时是两棵给人看的树：

```
User-Requested
├── github.com/LiteLDev/bds@1.21.0
├── github.com/LiteLDev/LeviLamina@1.0.0
└── github.com/LiteLDev/LegacyScriptEngine#lua@0.9.0
Dependencies
└── github.com/LiteLDev/LegacyRemoteCall@0.9.0
```

空的组显示 `None`。这种排版可能会变化；在脚本中请用 `--json`。

带 `--json` 时，结果是一个恰好包含下列成员的 JSON 对象：

```json
{
  "ExplicitInstalled": [
    "github.com/LiteLDev/bds@1.21.0",
    "github.com/LiteLDev/LeviLamina@1.0.0",
    "github.com/LiteLDev/LegacyScriptEngine#lua@0.9.0"
  ],
  "ImplicitInstalled": [
    "github.com/LiteLDev/LegacyRemoteCall@0.9.0"
  ]
}
```

| 成员 | 值 |
| --- | --- |
| `ExplicitInstalled` | 由字符串 `<identity>@<version>` 组成的数组：显式安装的包。 |
| `ImplicitInstalled` | 由字符串 `<identity>@<version>` 组成的数组：依赖包。 |

## 退出状态

| 退出码 | 含义 |
| --- | --- |
| `0` | 列表已输出。 |
| 非 `0` | 用法错误，或无法读取状态文件。 |

## 示例

列出包：

```shell
lip list
```

在脚本中用 [jq](https://jqlang.org/) 取得显式安装的包：

```shell
lip list --json | jq -r '.ExplicitInstalled[]'
```

## 另请参阅

- [管理包](../../guides/managing_packages.md)
- [模组](../../interface/mods.md)：界面中的已安装模组。
- [状态文件](../../concepts/state_file.md)
