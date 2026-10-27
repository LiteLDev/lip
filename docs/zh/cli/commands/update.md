# lip update

更新包。

## 概要

```shell
lip update [PACKAGES] [options]
```

## 参数

| 参数 | 说明 |
| --- | --- |
| `[PACKAGES]` | 要更新的包。支持与 [`lip install`](./install.md#参数) 相同的格式：不带版本时安装最新版本；带 `@<version>` 时安装恰好该版本。省略时，所有显式安装的包都会更新到最新版本。 |

## 说明

这些包必须已经安装。更新一个包即先卸载旧版本，再安装新版本；其依赖会按需更新。

## 选项

| 选项 | 说明 |
| --- | --- |
| `-n, --dry-run` | 仅模拟执行，不做实际更改。 |
| `--ignore-scripts` | 更新时跳过安装与卸载生命周期脚本。 |
| `--platform <PLATFORM>` | 工作区的平台：`server` 或 `client`。参见[环境](../../concepts/package_manifest.md#环境)。 |
| `--rid <RID>` | 工作区的 .NET RID，例如 `win-x64`。 |
