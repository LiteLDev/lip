# lip self-update

把 lip 自身更新到最新发布的版本。

## 概要

```shell
lip self-update [options]
```

## 选项

| 选项 | 说明 |
| --- | --- |
| `--check` | 只检查：如果有更新的版本，打印它的版本号，不安装任何内容。 |

## 说明

lip 通过 GitHub API 读取 [lip 的 Release](https://github.com/LiteLDev/lip/releases)。最新的 Release 是版本最高的那个；只有当正在运行的 lip 本身是预发布版本时，`0.41.0-beta.1` 这样的预发布版本才会被考虑。最新的 Release 不比正在运行的 lip 更新时，`lip self-update` 会提示 lip 已是最新，不做任何更改。

否则，lip 按照它的安装方式下载更新，用该 Release 的 `SHA256SUMS` 文件校验，然后安装：

| 安装方式 | 更新 |
| --- | --- |
| `setup.exe` | 下载 `lip-<version>-<platform>-setup.exe`，以管理员身份用 `/quiet` 运行。Windows 会请求你的允许。 |
| `.msi` | 下载 `lip-<version>-<platform>.msi`，以管理员身份运行 `msiexec /i <file> /qn`。Windows 会请求你的允许。 |
| 便携压缩包 | 下载 `lip-<version>-<platform>.zip`（Linux 为 `.tar.xz`），在 lip 所在目录中原地替换 lip 的文件。 |

Windows 安装包会在 `lip.exe` 旁写入 `install.json`，告诉 lip 是哪个安装包安装了它；没有这个文件时，lip 把自己当作便携版。lip 启动安装包后立即退出，安装包在后台完成安装。

便携版更新会保留目录中的其他文件。在 Windows 上，正在运行的 `lip.exe` 无法删除，因此 lip 把它重命名为 `lip.exe.old`，下次运行 lip 时会删除它。如果有文件无法替换，lip 会把旧文件放回原处。在 Linux 上，解压 `.tar.xz` 压缩包需要 `tar` 和 `xz`。

lip 安装更新期间，下载的文件保存在[缓存](cache_clean.md)的 `self-update` 目录中。设置了 `github_proxy` 时，更新文件通过它下载（见[配置](../configuration.md)）；GitHub API 和 `SHA256SUMS` 始终直接访问，因此代理无法改变 lip 安装的内容。

## 示例

检查是否有新版本：

```shell
lip self-update --check
```

更新 lip：

```shell
lip self-update
```
