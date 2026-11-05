# lip init

在当前目录创建 `manifest.json`，以初始化新项目。

## 概要

```shell
lip init
```

## 说明

会创建一个描述 LeviLamina 原生 mod 的 `manifest.json` 包文档，取值如下：

- `format`：`"bf45b1"`
- `name`：当前目录的名称（ASCII 字母、数字、`.`、`_` 和 `-` 以外的字符替换为 `-`）。
- `version`：`"0.1.0"`
- `dependencies`：`[]`
- `entry`：`"<name>.dll"`
- `type`：`"native"`

若当前目录已存在 `manifest.json`，或得到的名称不是有效的包 id（例如以 `_` 或 `-` 开头），命令会失败，且不写入任何内容。

后续步骤参见[创建包](../../guides/creating_packages.md)。
