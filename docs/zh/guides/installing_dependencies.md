# 安装依赖

lip 提供了灵活的依赖安装方式。

## 基础安装

按包 ID 安装包：

```sh
lip install <PACKAGES>
```

示例：
```sh
lip install github.com/user/repo
```

这会把该包中有对应工作区平台和 RID 条目的最新版本，及其声明的依赖安装到工作区，并记录到[工作区文件](../concepts/lockfiles.md)中，但不会修改任何 `lip.yaml`。

## 指定版本

可通过 `@` 语法安装某个确切版本：

```sh
lip install github.com/user/repo@1.0.0
```

如需选择变体，请在路径后追加 `#<variant>`，例如 `github.com/user/repo#lua@1.0.0`。

## 安装分支、标签或提交

如需试用尚未发布的改动，可安装分支的头、任意标签或某个提交：

```sh
lip install github.com/LiteLDev/LeviLamina@branch:main
lip install github.com/user/repo@tag:nightly
lip install github.com/user/repo@rev:0123456789abcdef0123456789abcdef01234567
```

工作区文件会记录该提交，因此在 `lip update` 之前工作区始终保持该提交；`lip update` 会跟随分支到新的头。参见 [git 引用](../cli/commands/install.md#git-引用)。

## 从本地文件和目录安装

可直接从根部带有 `lip.yaml` 的本地 ZIP 文件或目录安装，使用相对于当前目录的路径或绝对路径：

```sh
lip install ./path/to/package.zip
lip install C:\Downloads\package.zip
lip install ../MyTool
```

这对发布前测试包很有用。

## 从远程 URL 安装

也可以直接从 HTTPS URL 安装根部带有 `lip.yaml` 的 ZIP 归档：

```sh
lip install https://example.com/package.zip
```

## 仅限服务端工作区

lip 管理服务端工作区：mod 安装到 `mods/<name>/`（参见[环境](../concepts/lip_yaml.md#环境)）。客户端工作区由 LeviLauncher 等启动器管理，它们读取同样的包。

在 `lip install`、`lip uninstall` 或 `lip update` 改动任何包之前，lip 会在 `mods` 不存在时创建它。若 `mods` 是链接，lip 停止，不做任何改动。

## 命令参数

- `-n, --dry-run`：仅模拟安装，不做实际更改，可用于预览结果。
- `--no-dependencies`：仅安装指定包，忽略其依赖，也不卸载其他包。这可能导致工作区损坏。
- `--ignore-scripts`：不执行任何包钩子，包括安装过程中被移除或替换的包的卸载钩子。
- `--rid <RID>`：设置工作区的 .NET RID，例如 `win-x64`。
