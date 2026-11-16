# lip self-update

把 lip 自身更新到最新发布的版本。

## 概要

```
lip self-update [--check] [-h | --help]
```

## 说明

`lip self-update` 找出 lip 的最新发布；如果它比正在运行的 lip 新，就按 lip 当初的安装方式安装它。

### 检查

lip 从 GitHub API 读取 [lip 的发布](https://github.com/LiteLDev/lip/releases)，地址是 `https://api.github.com/repos/LiteLDev/lip/releases?per_page=100`。草稿，以及标签不是 `v` 加 SemVer 版本的发布，会被忽略。

**最新发布**是版本最高的那个。像 `1.1.0-beta.1` 这样的预发布版本，只有在正在运行的 lip 本身也是预发布版本时才计入。

没有任何发布，或者最新发布并不比正在运行的 lip 新时，lip 写明它已是最新，并且不做任何更改。带 `--check` 时，无论哪种情况，lip 都到此为止。

### 安装类型

| 类型 | 含义 | lip 如何得知 |
| --- | --- | --- |
| `msi` | lip 是由 Windows 安装程序 `lip-<version>-<platform>.msi` 安装的。 | `lip` 可执行文件旁边的 `install.json` 文件内容为 `{"kind": "msi"}`。它由安装程序写入。 |
| `portable` | lip 是从归档中解压出来的，或以其他任何方式安装的。 | 没有 `install.json`。 |

如果 `install.json` 存在，却不是 `kind` 为 `"msi"` 的 JSON 对象，`lip self-update` 失败。

`<platform>` 是 lip 运行所在的系统：`win-`、`linux-` 或 `osx-`，后接 `x64` 或 `arm64`。在其他系统上，`lip self-update` 在做出任何更改之前失败。

### 更新

不带 `--check` 且存在更新的发布 `V` 时，lip 依次：

1. 读取安装类型。
2. 选定发布中要安装的文件。发布中没有这个文件，或者没有 `SHA256SUMS` 文件时，命令失败。在 Windows 以外的系统上，类型 `msi` 也会失败。

   | 类型 | 文件 |
   | --- | --- |
   | `msi` | `lip-V-<platform>.msi` |
   | Linux 上的 `portable` | `lip-V-<platform>.tar.xz` |
   | Windows 和 macOS 上的 `portable` | `lip-V-<platform>.zip` |

3. 清空并创建[缓存目录](../files.md#缓存目录)中的 `self-update` 目录。
4. 下载 `SHA256SUMS`，即该发布的校验和。
5. 把文件下载到 `self-update` 中。如果 `SHA256SUMS` 中没有这个文件的行，或者下载内容的 SHA-256 不一致，lip 删除下载的文件并失败。
6. 按安装类型安装该文件；见下文。

第 1 到 5 步中的失败不会更改安装目录中的任何文件。

**`msi`。** lip 以管理员身份启动 `msiexec /i <file> /qn /norestart`；Windows 会请你允许。你拒绝，或者 `msiexec` 没能启动时，命令失败。否则 lip 写明安装程序正在运行，并立即以退出码 0 退出，不等待它结束。安装程序在后台静默地替换 lip，不会重启 Windows。请关闭其他正在运行的 `lip` 进程，让它能够完成。

**`portable`。** lip 把归档解压到 `self-update` 中，然后逐个替换 lip 所在目录中的文件：每个已有的文件先改名为 `<file>.old`，再在原位置写入新文件。任何一步失败时，lip 把所有改过名的文件恢复原样，然后失败。所有文件都写好之后，lip 删除能删除的 `.old` 文件和 `self-update` 目录。

- 目录中不在归档里的文件会保留。
- 在 Windows 上，正在运行的 `lip.exe` 无法删除，所以 `lip.exe.old` 会留下。下一次启动的 `lip` 会删除它。
- 在 Linux 上，解压 `.tar.xz` 归档需要系统中有 `tar` 和 `xz`。

### 更新之后

如果这次更新替换了 lip 或启动了安装程序，执行它的[守护进程](daemon.md)会在空闲后退出，这样下一条命令就会启动新版本。它还在运行托管服务器时会继续运行；参见[更新之后](daemon.md#更新之后)。

### 网络

发布列表和 `SHA256SUMS` 始终直接从 GitHub 读取，这样代理无法改变 lip 安装的内容。只有更新文件本身通过 [`github_proxy`](../configuration.md#github-proxy) 下载。

## 选项

| 选项 | 说明 |
| --- | --- |
| `--check` | 只查找最新发布。不安装任何东西，也不做任何更改。 |
| `-h`、`--help` | 输出帮助并退出。 |

## 输出

只有 `lip self-update --check` 有结果。存在更新的发布时，标准输出收到它的版本和一个换行：

```
1.1.0
```

lip 已是最新时，标准输出什么都收不到。其余的都是写到标准错误的消息。

所以脚本要靠输出是否为空来区分这两种情况，而不是靠退出码。

## 退出状态

| 退出码 | 含义 |
| --- | --- |
| `0` | lip 已是最新；或 `--check` 已完成（无论是否存在更新的发布）；或更新已安装；或安装程序已启动。 |
| 非 `0` | 用法错误，或检查或更新失败。 |

## 示例

检查是否有新版本：

```shell
lip self-update --check
```

更新 lip：

```shell
lip self-update
```

在 POSIX shell 脚本中，只在有新版本时更新：

```shell
if [ -n "$(lip self-update --check 2> /dev/null)" ]; then
  lip self-update
fi
```

## 另请参阅

- [开你的第一个服务器](https://levimc.org/tutorials/zh-Hans/server/first-server/)：安装 lip。
- [设置](../../interface/settings.md)：界面中的**检查更新**。
- [文件](../files.md#安装目录)：`install.json` 和 `.old` 文件。
