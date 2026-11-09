# lip init

在当前目录创建 `lip.toml`，以初始化一个新包。

## 概要

```shell
lip init
```

## 说明

在终端上询问包 ID，即 [Go 模块路径](https://go.dev/ref/mod#module-path)，例如 `github.com/user/repo`，直到回答合法为止，然后创建一个 `lip.toml`（参见 [lip.toml](../../concepts/lip_toml.md)），内容如下：

- `format`：`4`
- `id`：你输入的包 ID。
- `version`：`0.1.0`
- `variants`：变体 `""` 的一个条目，`side = "server"`，`platform = "win-x64"`

若当前目录已存在 `lip.toml`，命令会失败且不写入任何内容。若标准输入或标准错误不是终端，则无法询问，命令同样会失败。

接下来的步骤请参阅[创建包](../../guides/creating_packages.md)。
