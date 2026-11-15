# 故障排查

读完本页，你就能读懂命令失败时 lip 告诉你的内容，找到它的失败报告，并解决最常见的几种失败。

## 读懂失败信息

命令失败时，lip 会在终端里输出三样东西：

```text
error: There is no server to run: 'D:\servers\survival\bedrock_server_mod.exe' is not a file, though the workspace has the game bds 1.26.60 with a loader. It may have been deleted or moved by hand; restore it, or install the game again by uninstalling it and setting it up.

Full report: .lip\logs\20261114T201502Z-4120.log
```

1. **`error:`** 出了什么问题。如果一个问题是由另一个问题引起的，每个原因会各占一行，以 `caused by:` 开头。
2. **`hint:`** 你可以怎么办。提示可能有好几条，也可能没有。
3. **`Full report:`** 一个文件，里面是 lip 对这次失败所知道的一切。

先看错误和提示。大多数时候这就够了。

界面会在对话框里显示同样的内容：

![服务器页面上有一个标题为“启动服务器 · 失败”的对话框。对话框显示错误：没有可运行的服务器，因为 bedrock_server_mod.exe 不存在；下面是 .lip\logs 中完整报告的路径。标注 1 指向错误，标注 2 指向报告。](/screenshots/zh-Hans/dialog-01-failure.svg)

### 失败报告

- **位置：** 运行命令时所在文件夹里的 `.lip/logs/`。在界面里，是所选工作区里的。
- **名字：** 失败的时间（UTC）加上 lip 进程的编号，例如 `20261114T201502Z-4120.log`。
- **数量：** lip 保留最新的 20 份，更旧的会被删除。
- **格式：** 纯文本。用任何编辑器都能打开。

一份报告有五个部分：

| 部分 | 内容 |
| --- | --- |
| `Environment` | 时间、lip 的版本、命令、文件夹、你的系统、配置目录和缓存目录、命令是否由守护进程执行，以及已设置的代理变量。 |
| `Error` | 错误和提示，与终端里显示的一样。 |
| `Exception` | 错误的完整技术细节。 |
| `Workspace (.lip)` | `.lip` 里有什么；如果有被中断的操作，还有它的日志。 |
| `Trace` | lip 一步步做了什么：网络请求、考虑过的版本、改动的文件，以及包的程序的输出。 |

URL 里的密码和令牌（例如代理的）会被替换成 `***`。即便如此，分享之前还是请看一遍：里面有你的文件夹名和包名。

有些失败不会写报告：

- **命令行写错了**，例如用了不存在的选项。lip 会说明问题，并加上一句 `Run lip --help to see how to use lip.`
- **你取消了命令。** lip 会输出 `Cancelled. Any changes to the workspace were undone.`
- **服务器运行了一阵后失败。** `lip run` 只是把服务器的退出码传出来，不会加任何东西。请看服务器自己的输出。

如果报告本身写不进去，例如磁盘是只读的，lip 会改为在终端里输出其中最重要的部分。

### 寻求帮助

如果错误和提示帮不上忙，请在 <https://github.com/LiteLDev/lip/issues> 提交 issue，并附上报告。如果某条提示说 `This looks like a bug in lip`，请务必这样做：那是我们的问题，不是你的。

## 另一个 lip 正在更改工作区

```text
error: lip cannot take the workspace lock 'D:\servers\survival\.lip\lock': most likely another lip command is changing the workspace 'D:\servers\survival'. Wait for the other lip command to finish, then try again. If no other lip command is running, a program may be holding the lock file open: close it, then try again.
```

同一时间只能有一条命令更改一个服务器文件夹。有 `lip setup`、`lip install`、`lip update` 或 `lip uninstall` 正在那里工作，或者界面正在那里应用更改时，第二条命令不会等待：它立刻失败，什么都不改。

`lip run` 也可能这样失败。在启动服务器之前，以及每次自动重启之前，它都要检查文件夹里有没有被中断的更改，那一刻它需要同一把锁。

怎么办：

1. **等**另一条命令结束，再运行你的命令。看看其他终端，如果界面开着也看一下。
2. **如果没有 lip 在工作**，那就是别的程序打开着 `.lip/lock` 文件。备份工具、同步工具和编辑器都可能这样做。关掉它再试。

你永远不需要删除 `.lip/lock`。锁不是这个文件本身，而是对它的占用；持有它的 lip 一结束，占用就自动解除，即使它是崩溃的。

只读的命令（例如 `lip list`）和所有试运行都不加锁，绝不会这样失败。

## 文件被占用

```text
hint: A file is in use by another program. Stop the server and other programs that use the workspace, then try again.
```

这发生在 Windows 上：运行中的程序会一直打开它的文件，谁也没法替换或删除它们。最常见的情况是这个程序就是服务器本身：你在它运行时更改了包，而 lip 必须替换的文件属于运行中的游戏或已加载的模组。

lip 已经撤销了它的更改。工作区和之前一样。

怎么办：

1. **停止服务器。** 在它的终端里按 Ctrl+C，或者在界面的服务器页面选择“停止”。
2. **关闭其他**打开着该文件夹里文件的**程序**，例如正在编辑配置文件的编辑器。
3. **重新运行命令。**

如果 lip 还说了 `Some changes could not be undone`，说明某个文件被占用得太死，lip 连放回去都做不到。同样按这三步做。之后在这个文件夹里的下一次 `lip install`、`lip update`、`lip uninstall` 或 `lip setup` 会先完成清理，再做别的事。见[崩溃或断电之后](#崩溃或断电之后)。

界面不会遇到这种情况：工作区的服务器运行期间，它不更改那里的包。

## 连不上 GitHub

lip 的大多数包都从 GitHub 下载。提示会告诉你遇到的是哪一类问题：

| 提示的开头 | 含义 | 怎么办 |
| --- | --- | --- |
| `lip could not reach the server.` | 完全没有回应：没有网络，或者 GitHub 被屏蔽、非常慢。 | 检查网络连接。然后见 [GitHub 很慢或被屏蔽时](configuration.md#github-很慢或被屏蔽时)。 |
| `The server refused the request, possibly because too many requests were sent.` | GitHub 或你的代理限制了下载量。 | 等几分钟再试。 |
| `The repository or file does not exist, or is private.` | 名字写错了，或者仓库不是公开的。 | 检查名字和版本有没有拼错。lip 只从公开仓库安装。 |
| `The server had a problem.` | GitHub 或对方网站出了问题。 | 稍后再试。 |
| `A file that <package> <version> publishes is missing from where its package file says it is` | 包指向的下载地址已经失效。 | 用 `<package>@<version>` 安装别的版本，并告诉包的作者。 |

如果你设置了 GitHub 代理，而错误信息说代理不支持 git 仓库，那么这个代理做不到 lip 需要的事。用 `lip config delete github_proxy` 移除它，或者换一个。

界面启动时会检查 GitHub。连不上而你又还没做过选择时，它会问你是设置代理、直接连接，还是以后再说。

## 服务器无法启动

`lip run` 或界面里的“启动”在服务器运行起来之前就失败了。请在下面找到你看到的信息。

**`There is no server to run: this workspace has no game.`**

这个文件夹还没有安装服务器，或者你进错了文件夹。进入服务器文件夹，或者安装一个：`lip setup --game <version>`。见[安装服务器](server_from_commands.md#安装服务器)。

**`A server of the workspace '…' is already running`**

这个文件夹里已经有服务器在运行，而一个文件夹同一时间只运行一个服务器。找到它，在它所在的地方停止它：

- 它可能运行在**另一个终端**里，由 `lip run` 启动。在那里按 Ctrl+C。
- 它可能是**托管服务器**，由界面或 `lip run --managed` 启动。运行 `lip run --managed` 接入它，再按 Ctrl+C。或者在界面的服务器页面选择“停止”。

在界面里，对于不是它启动、它也控制不了的服务器，服务器页面会显示**在此界面之外运行**：比如由普通的 `lip run` 启动的服务器，或者这台机器上另一个用户运行的服务器。界面无法停止它，也无法显示它的输出。请在启动它的地方停止它。

**`There is no server to run: '…' is not a file`**

文件夹已经安装了服务器，但服务器的程序不见了。它可能被手动删除或移走，或者被安全软件清除了。请从备份中恢复。没有备份时，可靠的办法是换一个新文件夹：在那里安装服务器、安装你的模组，再把旧文件夹里的 `worlds/`、`server.properties`、`allowlist.json`、`permissions.json` 和模组的配置复制过去。

**`The game of this workspace is the Linux server … which lip runs only on Linux on x64.`**

到 x64 的 Linux 上运行它，或者改装 Windows 服务器：`lip setup --game <version> --build windows`。见 [Linux 与 macOS](linux_macos.md)。

**`The Microsoft Visual C++ Redistributable was not installed: Windows did not allow its installer to run as an administrator.`**

Windows 服务器需要它，而 Windows 询问时你选了“否”。重新运行命令并允许它，或者自己从 <https://aka.ms/vc14/vc_redist.x64.exe> 安装。

**`Rosetta 2 is not installed`**

在 Apple 芯片的 Mac 上运行 `softwareupdate --install-rosetta --agree-to-license`，然后重新运行 lip。

**`lip run --managed needs the daemon of lip`**

见 [lip 自己执行了命令](#lip-自己执行了命令)。

**服务器启动后又退出了。**

这是服务器自己失败了，不是 lip。`lip run` 以服务器的退出码退出，不写报告。请看服务器最后输出的几行。常见原因有：端口被别的程序占用、世界损坏、某个模组加载失败。要检查是不是最后一种，可以卸载你最近安装的模组试试。

如果 `restart` 是 `on-failure` 而服务器一再失败，lip 在 10 分钟内重启 5 次后就不再尝试：

```text
⚠ The server exited with code 1. lip restarted it 5 times within 10 minutes, so it does not restart it again. Look at what the server wrote above for why it fails.
```

## 守护进程是另一个版本

lip 的工作是在后台进程（守护进程）里完成的，第一条命令会启动它。更新 lip 之后，旧版本的守护进程可能还在运行。

多数时候你不会察觉。新的 lip 会请旧的守护进程退出；它空闲时就会退出，然后新的守护进程启动。

正忙着的旧守护进程会留下：它正在执行某条命令，或者正在运行托管服务器。这时新的 lip 会说：

```text
lip could not have its daemon carry out the command (the daemon of lip 0.41.0 that runs carries out a command or runs a managed server); it carries it out itself.
```

你的命令照样能完成。想换到新的守护进程：

1. 看看在运行什么：`lip daemon status`。

   ```text
   The daemon of lip 0.41.0 runs as process 4120, carrying out 0 commands.
   ```

2. 停止托管服务器。对每一个，在它的文件夹里运行 `lip run --managed` 并按 Ctrl+C，或者在界面里停止它。为此，`lip run --managed` 仍然会使用旧的守护进程。
3. 运行 `lip daemon stop`。下一条命令会启动新的守护进程。

守护进程忙碌时，`lip daemon stop` 会拒绝并说明原因。它绝不会替你停止服务器。

## lip 自己执行了命令

```text
lip could not have its daemon carry out the command (<reason>); it carries it out itself.
```

lip 没能启动或连上它的守护进程，于是改由你启动的这个进程来完成工作。这是一种回退，不是失败：命令做的事是一样的。

没有守护进程时，有两点不同：

- **`lip run --managed` 会失败。** 托管服务器存在于守护进程里。请用普通的 `lip run` 运行服务器，或者先解决守护进程的问题。
- **界面不能让服务器持续运行。** 你在界面里启动的服务器，会在你退出界面时停止。退出时界面会告诉你。

括号里的原因说明了问题所在：

| 原因 | 怎么办 |
| --- | --- |
| `the daemon of lip <version> that runs carries out a command or runs a managed server` | 见[守护进程是另一个版本](#守护进程是另一个版本)。 |
| `the daemon that lip started did not listen within 10 seconds`、`the daemon that lip started does not listen` | 守护进程没能启动。重新运行命令。如果一直这样，请连同 `lip daemon status` 的输出一起反馈。 |
| `the daemon keeps exiting`、`the daemon keeps being replaced` | lip 连接时，守护进程连续多次退出或被替换。同时使用两个版本的 lip 时会这样。只用一个版本，再重新运行命令。 |
| 系统给出的信息，例如拒绝访问 | 在 Linux 和 macOS 上，检查你能否写入套接字所在的目录：`$XDG_RUNTIME_DIR/lip`；该变量未设置时是 `~/.local/state/lip`。 |

另一条不同的信息 `lip: the daemon ended before the command did.` 表示守护进程在为你工作时被杀掉了。重新运行命令。如果它当时正在更改工作区，那里的下一条命令会先做清理。

## 崩溃或断电之后

如果 lip 在命令更改服务器文件夹的过程中被杀掉，或者机器断电，这次更改就停在了一半。lip 对此有准备。更改文件之前，它会先把这次更改记在 `.lip/journal` 里，并把每个文件的旧版本留在旁边，直到整条命令做完。

你不需要做什么特别的事。在那个文件夹里的下一次 `lip setup`、`lip install`、`lip update`、`lip uninstall` 或 `lip run` 会先把事情理顺，并告诉你是哪种情况：

```text
⚠ A lip operation on this workspace was interrupted (it started at <time>); undid its <n> changes. The workspace and its state file are as they were before it.
```

被中断的命令还没做完，所以 lip 把它撤回了。请重新运行那条命令。

```text
Finished an interrupted lip operation that had completed (it started at <time>): deleted the backups it left.
```

被中断的命令只差清理没做，所以 lip 替它做完了。不需要重做什么。

如果 lip 没法把某个文件放回去，例如服务器正打开着它，lip 会失败、列出这些文件，并保留日志。停止服务器和使用该文件夹的程序，再运行上述任意一条命令。它会从停下的地方继续。

::: danger
不要删除 `.lip/journal`；日志还在的时候，也不要删除服务器文件夹里名为 `.<名字>.bak` 的文件。lip 要靠它们来恢复。
:::

试运行，以及界面在你应用之前显示的计划，只会提到有操作被中断。它们不会修复它。

## 清理缓存

lip 把下载的东西放在缓存里，这样同一个文件不会下载两次。缓存只是为了省时间。删掉它不会丢失任何东西。

```sh
lip cache clean
```

```text
✓ Cache cleaned successfully.
```

这些时候可以清理：

- **需要磁盘空间。** 游戏的下载文件很大。
- **某个下载似乎坏了**，同一个安装一再失败。lip 本来就会替换缓存里不是有效 ZIP 压缩包的文件，所以很少需要这样做。

别的 lip 命令正在下载时不要清理：那条命令可能会失败。之后重新运行它即可。

你的服务器、你的设置和 lip 的 Wine 都不在缓存里，不受影响。在界面里，同样的功能是设置页面的“清理缓存”。

## 其他信息

| 提示 | 怎么办 |
| --- | --- |
| `lip is not allowed to access a file.` | 检查文件是不是只读的，以及你的用户能否写入服务器文件夹。 |
| `The disk is full.` | 腾出一些空间（例如用 `lip cache clean`），然后再试。 |
| `A hook command of a package failed.` | 某个包在安装或移除过程中运行的程序失败了。它的输出在错误信息上方，也在报告的 `Trace` 部分。这是包的问题：请向包的作者反馈。 |
| `Run lip in an interactive terminal.` | lip 需要问你一些事，但它的输入或输出不是终端，问不了。如果是 Minecraft EULA，请加上 `--accept-eula`。 |
| `The game of this workspace is <version>, with LeviLamina <version>; lip install does not change them.` | 你要安装的包与你的游戏或加载器不兼容。错误信息会指出是哪些。请安装它们的其他版本，或者更改游戏：见[更改游戏版本](server_from_commands.md#更改游戏版本)。 |
| `Request other versions of the packages named above, or install fewer of them together.` | 你要安装的包彼此不兼容。错误信息会指出是哪些。请安装它们的其他版本，或者少装几个。 |

## 文件都在哪里

知道 lip 把东西放在哪里，有助于查找问题。

| 内容 | 位置 |
| --- | --- |
| 你的服务器：游戏、世界、`server.properties`、模组 | 服务器文件夹本身 |
| lip 对这个文件夹的了解：已安装的包、锁、日志、失败报告、Wine 前缀、工作区设置 | 服务器文件夹里的 `.lip/`。在 Windows 上是隐藏的。 |
| 你的设置 | Windows 上是 `%APPDATA%\lip`，其他系统上是 `~/.config/lip` |
| 缓存 | Windows 上是 `%LOCALAPPDATA%\lip\Cache`，其他系统上是 `~/.cache/lip` |
| lip 的 Wine | Linux 和 macOS 上是 `~/.local/share/lip` |
| 界面的工作区列表 | Windows 上是 `%LOCALAPPDATA%\lip\State`，其他系统上是 `~/.local/state/lip` |

每个文件和目录的说明见[文件和目录](../reference/files.md)。
