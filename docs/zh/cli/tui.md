# 交互界面

在终端里不带参数运行 `lip`，即可打开交互界面：lip 的全部命令、[levimc.org/mods](https://levimc.org/mods/) 上的模组，以及工作区的服务器，都在一个全屏程序里。

```shell
lip
```

与所有命令一样，工作区是当前目录。当 lip 的标准输入或标准输出不是终端时（脚本或管道中），不带参数的 `lip` 仍然输出帮助。

## 标签页

用 `Tab` 和 `Shift+Tab`，或标签页的数字切换。屏幕最下面一行始终列出当前可用的按键。

| 标签页 | 作用 | 对应命令 |
| --- | --- | --- |
| **1 Packages** | 列出已安装的包；安装、更新、卸载。 | [`lip list`](./commands/list.md)、[`lip install`](./commands/install.md)、[`lip update`](./commands/update.md)、[`lip uninstall`](./commands/uninstall.md) |
| **2 Browse** | 搜索、筛选、排序 levimc.org 上的模组，查看详情、版本和 README，并安装。 | [`lip install`](./commands/install.md) |
| **3 Server** | 在窗口里运行工作区的服务器，显示它的输出，并把你输入的行发给它。 | [`lip run`](./commands/run.md) |
| **4 Settings** | 查看和修改配置；清理缓存。 | [`lip config`](./commands/config.md)、[`lip cache clean`](./commands/cache_clean.md) |
| **5 Develop** | 创建 `lip.toml`、迁移 `tooth.json`、打包 ZIP、查看包。 | [`lip pkg init`](./commands/pkg_init.md)、[`lip pkg migrate`](./commands/pkg_migrate.md)、[`lip pkg pack`](./commands/pkg_pack.md)、[`lip pkg view`](./commands/pkg_view.md) |

按 `q` 或 `Ctrl+Q` 退出。

## 安装、更新与卸载

对工作区的每次修改都会先生成计划：lip 先以 dry run（`--dry-run`）执行一遍，并显示将要做什么。在你选择 **Apply** 之前，什么都不会改变。

执行修改期间，按 `Ctrl+C` 可以取消，并撤销已经完成的部分。命令失败时，界面会显示原因、可以采取的措施，以及 `.lip/logs` 下失败报告的路径，然后继续运行。

在安装对话框里，包的写法与命令行相同（见 [`lip install`](./commands/install.md)），以空格分隔；第二个输入框是 `--platform`。

## 浏览模组

Browse 标签页在第一次打开时读取 levimc.org 的模组目录（`https://levimc.org/api/v4/mods/`）；目录内容不会写入磁盘，也不经过 `github_proxy`。lip 只管理服务端，所以列表默认只显示有服务端构建的模组。

| 按键 | 作用 |
| --- | --- |
| `/` | 按名称、id、作者、标签和描述搜索。 |
| `s` | 切换排序：相关度、星标、最近更新、最新发布。 |
| `f` | 切换运行端：服务端、客户端、全部。 |
| `t` | 切换模组必须带有的标签。 |
| `Enter` | 安装选中的模组。 |
| `v` | 列出它的版本；`Enter` 安装所选版本。 |
| `r` | 显示它的 README。 |
| `o` | 在浏览器中打开它的页面。 |
| `R` | 重新读取目录。 |

## 运行服务器

在 Server 标签页按 `Enter`，会像 [`lip run`](./commands/run.md) 一样启动 `bedrock_server_mod.exe`。服务器的输出显示在窗口里，`PgUp` 和 `PgDn` 可以回看。服务器运行期间，你输入的内容是发给服务器的一行，按 `Enter` 发送；`↑` 和 `↓` 可以调出之前输入过的行。

`Ctrl+C` 会向服务器发送一行 `stop`，请它停止。在服务器运行时退出 lip 也是如此，lip 会等到服务器退出。

服务器运行期间，其他标签页仍然可用。与 `lip run` 一样，此时工作区没有被锁定，对包的修改在服务器下次启动时生效。
