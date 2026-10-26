# 安装依赖

lip 提供了灵活的依赖安装方式。

## 基础安装

按名称或 ID 安装包：

```sh
lip install <PACKAGES>
```

示例：
```sh
lip install github.com/user/repo
```

这会把包安装到工作区，并记录到锁文件（`tooth_lock.json`）中，但不会修改 `tooth.json`。

## 指定版本

可通过 `@` 语法指定版本：

```sh
lip install github.com/user/repo@1.0.0
```

## 从本地文件安装

可直接从本地归档文件（`.zip`、`.tar` 等）安装：

```sh
lip install ./path/to/package.zip
```

这对发布前测试包很有用。

## 从远程 URL 安装

也可以直接从 URL 安装：

```sh
lip install https://example.com/package.zip
```

## 命令参数

- `-n, --dry-run`：仅模拟安装，不做实际更改，可用于预览结果。
- `--no-dependencies`：仅安装指定包，忽略其依赖。
- `--ignore-scripts`：跳过执行包中定义的 `pre_install`、`install`、`post_install` 生命周期脚本。
