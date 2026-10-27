# 安装依赖

lip 提供了灵活的依赖安装方式。

## 基础安装

按路径安装包：

```sh
lip install <PACKAGES>
```

示例：
```sh
lip install github.com/user/repo
```

这会把该包的最新版本及其声明的依赖安装到工作区，并记录到[锁文件](../concepts/lockfiles.md)中，但不会修改 `manifest.json`。

## 指定版本

可通过 `@` 语法安装某个确切版本：

```sh
lip install github.com/user/repo@1.0.0
```

如需选择变体，请在路径后追加 `#<variant>`，例如 `github.com/user/repo#lua@1.0.0`。

## 从本地文件安装

可直接从本地 `.mcmod` 文件或目录安装：

```sh
lip install ./path/to/package.mcmod
lip install ./path/to/package/
```

这对发布前测试包很有用。

## 从远程 URL 安装

也可以直接从 HTTPS URL 安装 ZIP 归档：

```sh
lip install https://example.com/package.mcmod
```

## 仅限服务端工作区

lip 管理服务端工作区：mod 安装到 `plugins/<name>/`（参见[环境](../concepts/package_manifest.md#环境)）。客户端工作区由 LeviLauncher 等启动器管理，它们读取同样的包。

## 命令参数

- `-n, --dry-run`：仅模拟安装，不做实际更改，可用于预览结果。
- `--no-dependencies`：仅安装指定包，忽略其依赖。
- `--ignore-scripts`：跳过执行包中定义的 `preInstall` 和 `postInstall` 脚本。
- `--rid <RID>`：设置工作区的 .NET RID，例如 `win-x64`。
