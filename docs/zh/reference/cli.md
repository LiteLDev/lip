# 命令行

本页说明如何调用 `lip`：有哪些命令、参数怎样读取、输出写到哪里、退出状态代表什么，以及试运行和 Ctrl+C 的行为。每个命令有自己的页面；这里写的是它们共有的规则。

## 概要

```
lip
lip (-h | --help)
lip (-v | --version)
lip (cache | config | daemon | pkg) (-h | --help)
lip <command> [<argument>...]
```

概要中，`word` 照原样书写，`<name>` 要替换成具体的值，`[x]` 可以省略，`x | y` 表示二选一，`(x | y)` 把备选项括在一起，`x...` 表示 `x` 出现一次或多次。

| 调用方式 | lip 的行为 |
| --- | --- |
| `lip` | 标准输入和标准输出都是终端时，运行[界面](interface.md)；否则把 `lip` 的帮助写到标准输出。 |
| `lip -h`、`lip --help` | 把 `lip` 的帮助写到标准输出，后面跟什么都一样。 |
| `lip -v`、`lip --version` | 把自己的版本（一个 [SemVer](https://semver.org/spec/v2.0.0.html) 版本）和一个换行写到标准输出，后面跟什么都一样。 |
| `lip cache -h`、`lip config -h`、`lip daemon -h`、`lip pkg -h` | 把该命令组的帮助写到标准输出。`--help` 相同。 |
| `lip --daemon` | 作为[守护进程](commands/daemon.md)运行直到退出，不输出任何内容。lip 自己会这样启动它，你不需要手动运行。 |
| `lip <command> ...` | 执行该命令。 |

输出帮助或版本时退出码为 0。第一个参数以 `-` 开头而又不是上面这些时，属于[用法错误](#用法错误)。

## 命令

用于运行服务器的命令：

| 命令 | 作用 |
| --- | --- |
| [`lip setup`](commands/setup.md) | 选择工作区的游戏和加载器。 |
| [`lip install`](commands/install.md) | 向工作区安装包。 |
| [`lip uninstall`](commands/uninstall.md) | 从工作区卸载包。 |
| [`lip update`](commands/update.md) | 更新已安装的包。 |
| [`lip list`](commands/list.md) | 列出已安装的包。 |
| [`lip run`](commands/run.md) | 运行工作区的服务器。 |
| [`lip cache clean`](commands/cache_clean.md) | 删除缓存。 |
| [`lip config get`、`set`、`list`、`delete`](commands/config.md) | 读写配置。 |

用于守护进程的命令：

| 命令 | 作用 |
| --- | --- |
| [`lip daemon status`](commands/daemon.md) | 显示守护进程是否在运行。 |
| [`lip daemon stop`](commands/daemon.md) | 停止守护进程。 |

用于开发包的命令：

| 命令 | 作用 |
| --- | --- |
| [`lip pkg init`](commands/pkg_init.md) | 为新包创建 `lip.toml`。 |
| [`lip pkg pack`](commands/pkg_pack.md) | 把一个目录打包成 ZIP 归档。 |
| [`lip pkg view`](commands/pkg_view.md) | 以 JSON 显示包的包文件和版本。 |
| [`lip pkg migrate`](commands/pkg_migrate.md) | 把当前目录中的 `tooth.json` 迁移为 `lip.toml`。 |

用于 lip 自身的命令：

| 命令 | 作用 |
| --- | --- |
| [`lip self-update`](commands/self_update.md) | 把 lip 更新到最新发布的版本。 |

`cache`、`config`、`daemon` 和 `pkg` 是**命令组**：由两个词组成的命令名的第一个词。

## 参数

命令名、命令组名和选项名都区分大小写。

- 第一个参数是单个词的命令名，或者一个命令组。命令组后面必须是 `-h`、`--help`，或者该组某个命令的第二个词。其他写法都是用法错误，命令组后面什么都不跟也是。
- `--` 在 lip 中不是分隔符：任何一个参数是 `--` 都是用法错误。
- 选项和操作数可以按任意顺序写在命令名之后。操作数按给出的顺序赋值。

命令的参数中，以 `-` 开头的都是选项：

| 形式 | 含义 |
| --- | --- |
| `--<name>` | 长选项 `<name>`。 |
| `-<c>` | 短选项 `<c>`，一个字符。 |
| `--<name>=<value>` | 长选项 `<name>`，值为 `<value>`。 |
| `-<c>=<value>` | 短选项 `<c>`，值为 `<value>`。 |

- 需要值的选项如果没有用 `=` 书写，就把下一个参数当作它的值。这个参数不能以 `-` 开头。
- 值不能为空。
- **开关**是不带值的选项。同一个开关写多次，效果和写一次相同。
- 需要值的选项写多次时，以最后一次的值为准。
- 短选项不能合并：`-nh` 的含义没有定义。给开关带上值（如 `--json=true`）的含义也没有定义。不要这样写。

其余的参数都是**操作数**。

## 用法错误

命令的参数在下列情况下属于用法错误：

1. 某个选项不是该命令的选项；
2. 需要值的选项没有值；
3. 概要中要求的操作数或选项缺失；或者
4. 操作数比命令接受的多。

遇到用法错误时，lip 向标准错误写一条消息，以非零退出码退出，不做其他任何事，也不写[失败报告](files.md#失败报告)。

## 所有命令共有的选项

| 选项 | 说明 |
| --- | --- |
| `-h`、`--help` | 把该命令的帮助写到标准输出，并以退出码 0 退出。命令不做其他任何事。 |

`-h` 优先于缺失的操作数和未知的选项：`lip install -h` 和 `lip install -h --bogus` 都输出帮助。它不优先于缺失的选项值和过多的操作数，这两种情况仍然是用法错误。

## 工作目录

每个命令的**工作区**就是当前工作目录。参数中的相对路径相对于它。没有用来指定其他工作区的选项：请先切换目录。

## 输出

- **结果**是命令被要求给出的数据，写到标准输出。只有 `list`、`pkg view`、`config get`、`config list`、`daemon status` 和 `self-update --check` 有结果。
- **消息**是其余的一切：进度、信息、警告、错误、最后的成功提示。消息写到标准错误。它的措辞可能随版本变化，不要解析它。
- 帮助写到标准输出。

因此 `lip list > packages.txt` 只保存列表，`2> /dev/null` 只隐藏消息。消息始终是英文。

### JSON

下列命令接受 `--json`。此时标准输出只收到 JSON 形式的结果，后跟一个换行：

| 命令 | JSON 结果 |
| --- | --- |
| [`lip list --json`](commands/list.md#输出) | `{ "ExplicitInstalled": [...], "ImplicitInstalled": [...] }` |
| [`lip config get <KEY> --json`](commands/config.md#输出) | 值，作为 JSON 字符串；或 `null`。 |
| [`lip config list --json`](commands/config.md#输出) | 一个对象，把每个键映射到它的值或 `null`。 |
| [`lip daemon status --json`](commands/daemon.md#输出) | `{ "running": false }`，或 `{ "running": true, "version": "...", "pid": ... }`。 |

[`lip pkg view`](commands/pkg_view.md) 始终输出 JSON，没有 `--json` 选项。不带 `--json` 时，结果的排版是给人看的，可能会变化。

## 退出状态

| 退出码 | 含义 |
| --- | --- |
| `0` | 命令成功。 |
| 非 `0` | 命令失败，包括用法错误。标准错误上的消息说明原因。 |

[`lip run`](commands/run.md#退出状态) 是例外：一旦启动了服务器，它就以服务器的退出码退出。

因用法错误以外的原因失败的命令可能会写一份**失败报告**，即工作区中 `.lip/logs/` 下的一个文件，并在消息中给出它的路径。参见[失败报告](files.md#失败报告)。

## 包参数

`install`、`update` 和 `pkg view` 接受**包参数**。lip 按下面第一条适用的规则对每个包参数分类：

1. **URL。** 以 `https://` 开头的参数是一个 ZIP 归档的 URL。其他后跟 `://` 的协议（如 `http://`、`file://`）无效。
2. **本地路径。** 取参数中最后一个 `#` 之前的部分；如果整个参数就是一个存在的文件或目录，则取整个参数。满足下列任一条件时，它是本地路径：
   - 这部分以 `./`、`../`、`.\` 或 `..\` 开头；
   - 这部分是 `.` 或 `..`；
   - 这部分是绝对路径；或者
   - 这部分以 `.zip` 结尾（不区分大小写）或是一个存在的文件或目录，并且参数中不含 `@`。

   不存在的本地路径无效。
3. **远程仓库。** 其余的参数：`<repo>[#<variant>][@<ref>]`。不含 `/` 的 `<repo>` 是 LiteLDev 的仓库：`LeviLamina@1.9.7` 就是 `github.com/LiteLDev/LeviLamina@1.9.7`。

URL 和本地路径的末尾可以带 `#<variant>`。远程仓库的引用是下列之一：

| 引用 | 选中 |
| --- | --- |
| 无 | 仓库的版本标签（`v<version>`），从高到低。 |
| `@<version>` | 标签 `v<version>` 的提交。 |
| `@branch:<name>` | 该分支指向的提交。 |
| `@tag:<name>` | 该标签指向的提交。 |
| `@rev:<commit>` | 该 id 的提交：完整的 40 位十六进制数字。 |

包参数无效时，命令在做出任何更改之前失败。[`lip install`](commands/install.md) 中有每种形式的示例。

## 环境

lip 只为一种**环境**安装包：运行端 `server`、平台 `win-x64`，无论 lip 运行在什么系统上。如果工作区的[状态文件](../concepts/state_file.md)记录了其他运行端，或者 `win-x64` 和 `linux-x64` 以外的平台，`setup`、`install`、`uninstall`、`update` 和 `run` 都会失败。记录 `linux-x64` 的状态文件会[被当作 `win-x64` 的来读取](../concepts/state_file.md#linux-x64-的工作区)。

## 工作区操作

不带 `--dry-run` 的 `setup`、`install`、`uninstall` 和 `update` 各执行一次**工作区操作**：对工作区的更改要么全部完成，要么完全不做。

- 操作会取得工作区锁 `.lip/lock`。如果另一个进程持有它，命令立即失败，不做任何更改。
- 如果之前有操作被中断，下一次操作会先把它完成或撤销。
- 在工作区开始更改之前失败，不会留下任何更改。之后失败，更改会被撤销。已经运行过的钩子命令不会被撤回。

[lip 的工作方式](../concepts/how_lip_works.md)有完整的说明。

## 试运行

`setup`、`install`、`uninstall` 和 `update` 接受 `-n` 或 `--dry-run`。试运行执行所有不会改变任何东西的步骤，并用消息说明操作会对哪些包做什么。试运行：

- 不取得工作区锁，所以另一个命令正在更改工作区时也能运行；
- 不恢复被中断的操作：存在 `.lip/journal` 时，它会说明这一点，并按工作区当前的样子来规划；
- 不运行任何钩子命令；
- 不提问；并且
- 不创建、替换或删除任何文件和目录，只有一个例外：失败时可能写一份失败报告。

试运行仍然会使用网络来解析包，但不向缓存写入任何内容。

## Ctrl+C

Ctrl+C 的作用取决于 lip 正在做什么：

| lip 正在 | 第一次 Ctrl+C | 第二次 Ctrl+C |
| --- | --- | --- |
| 规划操作，工作区尚未更改 | 立即结束 lip。没有任何更改。 | |
| 更改工作区 | 取消操作并撤销它的更改。 | 立即结束 lip。工作区的下一次操作会恢复它。 |
| 用 `lip run` 运行服务器 | 向服务器发送 `stop`。服务器退出后 lip 才退出。 | 没有更多作用：lip 不会先于服务器退出。 |
| 连接着托管服务器（`lip run --managed`） | 向服务器发送 `stop`。你保持连接，直到它退出。 | 相同。 |

其他信号参见 [`lip run`](commands/run.md#停止)。

## 守护进程

除 `daemon` 组的命令以外，所有命令以及界面都由**守护进程**执行：它是属于你这个用户的后台 `lip` 进程，由第一条命令启动。你感觉不到区别：命令使用你的工作目录、你的环境变量、你的终端和你的 Ctrl+C，`lip` 以命令的退出码退出。

如果 lip 无法启动或连接守护进程，它会写一条消息，然后自己执行命令。只有 `lip run --managed` 离不开守护进程。参见 [`lip daemon`](commands/daemon.md)。

## 另请参阅

- [界面参考](interface.md)：界面的每个页面对应哪些命令。
- [配置](configuration.md)和[文件](files.md)。
- [用命令运行服务器](../guides/server_from_commands.md)和[管理包](../guides/managing_packages.md)。
