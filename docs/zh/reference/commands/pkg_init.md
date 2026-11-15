# lip pkg init

在当前目录中为新包创建 `lip.toml`。

## 概要

```
lip pkg init [-h | --help]
```

## 说明

`lip pkg init` 询问包 ID，然后在当前工作目录中写入 `lip.toml`。

1. 如果那里已经有 `lip.toml`，命令失败，不写入任何内容。
2. lip 在终端上询问包 ID，即一个 [Go 模块路径](https://pkg.go.dev/golang.org/x/mod/module#CheckPath)，例如 `github.com/user/repo`；回答无效时会一直重新询问。对于位于 git 仓库中的包，包 ID 就是仓库的路径。
3. 它写入一个恰好包含下列值的[包文件](../../concepts/lip_toml.md)，其中 `<id>` 是你的回答：

   ```toml
   format = 4
   id = "<id>"
   version = "0.1.0"

   [[variants.""]]
   side = "server"
   platform = "win-x64"
   ```

标准输入或标准错误不是终端时，lip 无法询问，命令会失败。没有用来给出包 ID 的选项；在脚本中请自己写 `lip.toml`。

这个包还不安装任何东西。要添加什么，参见[创建包](../../authors/creating_packages.md)。

`lip pkg init` 不是工作区操作：它不需要服务器，不取得任何锁，除 `lip.toml` 之外什么都不碰。

## 选项

| 选项 | 说明 |
| --- | --- |
| `-h`、`--help` | 输出帮助并退出。 |

## 输出

`lip pkg init` 没有结果：它不向标准输出写任何内容。提问和最后一行都是写到标准错误的消息：

```
Enter the package id of the package (a Go module path, e.g. github.com/owner/repo):
```

## 退出状态

| 退出码 | 含义 |
| --- | --- |
| `0` | `lip.toml` 已写入。 |
| 非 `0` | 用法错误；`lip.toml` 已存在；lip 无法询问；或文件无法写入。 |

## 示例

```shell
cd MyMod
lip pkg init
```

## 另请参阅

- [创建包](../../authors/creating_packages.md)
- [lip.toml](../../concepts/lip_toml.md)
- [工具](../../interface/tools.md)：界面中的**创建 lip.toml**。
