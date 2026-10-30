# lip init

在当前目录创建 `manifest.json`，以初始化新项目。

## 概要

```shell
lip init
```

## 说明

会创建一个描述 LeviLamina 原生 mod 的第 4 版 `manifest.json`，取值如下：

- `name`：当前目录的名称（ASCII 字母、数字、`.`、`_` 和 `-` 以外的字符替换为 `-`）。
- `entry`：`"<name>.dll"`
- `type`：`"native"`
- `version`：`"0.1.0"`
- `dependencies`：`[]`

若当前目录已存在 `manifest.json`，或得到的名称不是有效的包名（例如以 `_` 或 `-` 开头），命令会失败，且不写入任何内容。

后续步骤参见[创建包](../../guides/creating_packages.md)。
