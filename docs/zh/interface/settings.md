# 设置

读完本页，你就能更改界面的语言、告诉 lip 怎样连接 GitHub、让服务器在崩溃后自动重启、释放磁盘空间，以及更新 lip。

## 页面

打开页面：在侧边栏移到**设置**，按 `Enter`。

![“设置”页：从“语言”到“关于”共七行，每行都有自己的值。列表下面的一行写着“界面使用的语言。”](/screenshots/zh-Hans/settings-01-list.svg)

1. 一行设置及其值。用 `↑` `↓` 移动，按 `Enter` 修改或执行这一行。
2. 所选行的作用。值太长、一行放不下时，会在这里完整显示。

| 行 | 作用 | 归属 |
| --- | --- | --- |
| [语言](#语言) | 选择界面的语言。 | 这台电脑上的你。 |
| [GitHub 代理](#github-代理) | 决定 lip 怎样连接 GitHub。 | 这台电脑上的你。 |
| [自动重启](#自动重启) | 服务器崩溃时重新启动它。 | 当前工作区。 |
| [清理缓存](#清理缓存) | 删除已下载的文件。 | 这台电脑上的你。 |
| [检查更新](#更新) | 查看有没有更新的 lip，并安装它。 | lip 本身。 |
| [守护进程](#守护进程) | 显示 lip 的后台进程。 | 这台电脑上的你。 |
| [关于](#关于) | 显示 lip 的版本。 | lip 本身。 |

这个页面上的每项更改都立即生效，没有“保存”按钮。

## 语言

界面支持英文、简体中文和繁体中文。

1. 移到**语言**，按 `Enter`。

   ![“语言”对话框列出“跟随系统”、English、简体中文和繁體中文。](/screenshots/zh-Hans/settings-02-language.svg)

2. 用 `↑` `↓` 选择语言，按 `Enter`。整个界面立刻换成这种语言，不需要重启任何东西。

   ![刚选完语言之后的英文“设置”页。语言这一行显示 English。](/screenshots/zh-Hans/settings-23-language-changed.svg)

   1. 你选择的语言。
   2. 界面上的所有文字都换成了这种语言，包括侧边栏和最下面一行。

**跟随系统**使用系统的语言：

- 在 Windows 上，是你这个用户的显示语言。
- 在其他系统上，是环境变量 `LC_ALL`、`LC_MESSAGES`、`LANG` 中第一个指明了语言的。
- 台湾、香港、澳门的中文区域设置，或者带有 `Hant` 标记的，使用繁体中文；其他中文区域设置使用简体中文；其余一律使用英文。

只有界面本身有翻译。命令输出的内容在任何语言下都是英文：计划里的各行、安装过程中的消息，以及错误。

::: details 用命令做同样的事
这个选择就是你的配置里的 `language` 键，见[配置](../reference/configuration.md)。

```shell
lip config set language zh-Hans
lip config delete language
```

可用的值是 `en`、`zh-Hans` 和 `zh-Hant`。删除这个键就是**跟随系统**。
:::

## GitHub 代理

lip 从 GitHub 下载 LeviLamina 和大多数模组。GitHub 访问缓慢或者连不上时，可以使用 **GitHub 代理**：lip 会把这个网址加在每个 GitHub 下载地址的前面。

这一行的值有三种：

| 值 | 含义 |
| --- | --- |
| **未设置** | lip 直接连接 GitHub。每次界面启动时，lip 都会在后台检查 GitHub 是否有响应，没有响应就问你怎么办，见[连不上 GitHub 时](home.md#连不上-github-时)。 |
| **直连** | lip 直接连接 GitHub，不检查也不询问。 |
| 一个网址 | lip 通过这个代理向 GitHub 发送请求。通过代理失败的请求会改为直接发往 GitHub。 |

lip 不会推荐代理，本文档也不会。请使用你信任的代理：lip 从 GitHub 下载的所有内容都会经过它。

在这一行上按 `Enter` 打开它的操作：

![“GitHub 代理”菜单有三行：“设置代理…”“直接连接”和“重置”。“重置”是灰色的。](/screenshots/zh-Hans/settings-06-proxy-menu.svg)

1. **设置代理…**询问代理的网址，并进行测试。
2. **直接连接**把值设为**直连**。值已经是直连时，这一行是灰色的。
3. **重置**去掉这项设置，回到**未设置**。本来就没有设置时，这一行是灰色的。

### 设置代理

1. 在**设置代理…**上按 `Enter`，输入或粘贴代理的网址。网址以 `https://` 开头，通常以 `/` 结尾。

   ![“GitHub 代理”对话框，“代理网址”输入框里是一个以 https 开头的示例网址。](/screenshots/zh-Hans/settings-07-proxy-url.svg)

2. 按 `Enter`。lip 会测试这个代理：通过它向 GitHub 请求 LeviLamina，最多等待 10 秒。按 `Esc` 中止测试，回到网址输入框。

   ![“测试代理”对话框显示“处理中…”。](/screenshots/zh-Hans/settings-08-testing.svg)

3. GitHub 通过代理有了响应，lip 就保存它并显示**已保存。** 这一行显示网址。

   ![“GitHub 代理”一行现在显示刚才输入的网址，最下面一行的末尾是“已保存。”](/screenshots/zh-Hans/settings-09-proxy-saved.svg)

输入的内容必须是网址。前面没有 `https://` 就不算：

![输入框里是 old-proxy.example.com，下面显示“不是网址”。](/screenshots/zh-Hans/settings-10-not-a-url.svg)

### 测试失败时

GitHub 没有通过代理响应时，lip 会说明原因，并且暂时不保存。

![“代理不可用”对话框写着“GitHub answered with the HTTP status 502.”，有“重新输入”“仍然保存”“取消”三个按钮。](/screenshots/zh-Hans/settings-11-proxy-failed.svg)

1. **重新输入**回到网址输入框，原来的网址还在，可以修改。
2. **仍然保存**在测试失败的情况下照样保存这个代理。当你确定代理可用、只是测试过不去时使用。
3. **取消**或 `Esc` 什么也不改：这一行保持原来的值。

原因是英文。502、404 这样的状态码表示代理有响应，但响应的不是 GitHub 该有的内容；超时表示 10 秒内没有任何响应。更多内容见[故障排除](../guides/troubleshooting.md)。

### 直接连接，或重置

**直接连接**适合访问 GitHub 足够顺畅的网络：lip 不再检查，也不再询问。

![“GitHub 代理”一行显示“直连”，最下面一行的末尾是“已保存。”](/screenshots/zh-Hans/settings-12-direct.svg)

**重置**恢复为**未设置**：lip 直接连接，并在下次界面启动时重新检查。

::: details 用命令做同样的事
这项设置就是你的配置里的 `github_proxy` 键，见[配置](../reference/configuration.md)。

```shell
lip config set github_proxy https://proxy.example.com/
lip config set github_proxy ""
lip config delete github_proxy
```

第二行是**直接连接**，第三行是**重置**。命令不会测试代理。
:::

## 自动重启

打开**自动重启**后，当前工作区的服务器崩溃时（也就是它不是被停止、而是出错退出时），lip 会重新启动它。

移到**自动重启**，按 `Space` 或 `Enter`：`[x]` **开**，`[ ]` **关**。

![“自动重启”一行显示打勾的方框和“开”。下面一行写着“此工作区的服务器崩溃时自动重启。”](/screenshots/zh-Hans/settings-13-auto-restart.svg)

1. 这项设置只属于当前工作区。每个工作区各有各的设置；切换工作区就能看到另一个的。

- lip 等待 5 秒后再重新启动服务器。
- 你自己停止的服务器不会被重新启动。
- 不断崩溃的服务器在 10 分钟内最多重新启动 5 次。
- 以服务器退出那一刻的设置为准，所以服务器运行期间也可以切换。

[服务器](server.md)页上有同一个开关。

::: details 用命令做同样的事
```shell
lip config set restart on-failure --workspace
lip config set restart never --workspace
```
:::

## 清理缓存

lip 把下载的内容放在缓存里，这样同一个文件不用下载两次。这一行显示缓存所在的文件夹。清理缓存可以释放磁盘空间；以后需要时，lip 会重新下载。

1. 移到**清理缓存**，按 `Enter`。lip 会先询问。

   ![“清理缓存”对话框询问“要删除所有已下载的文件吗？”，有“清理”和“取消”两个按钮。](/screenshots/zh-Hans/settings-14-clear-cache.svg)

2. 在**清理**上按 `Enter`。

   ![“清理缓存”对话框写着“缓存已清理。”，有“确定”按钮。](/screenshots/zh-Hans/settings-15-cache-cleared.svg)

被删除的是缓存文件夹及其中的所有内容，也就是 lip 下载过的一切：游戏、LeviLamina 和模组的文件，以及下载的 lip 更新。你的工作区、其中的服务器和模组，以及你的设置都不受影响。

对应的命令是 [`lip cache clean`](../reference/commands/cache_clean.md)。

## 更新

lip 可以更新自己。

### 检查更新

移到**检查更新**，按 `Enter`。lip 向 GitHub 查询自己的最新发行版。

没有更新的版本时，这一行会注明：

![“检查更新”一行在版本 0.41.0 后面显示“已是最新”，最下面一行的末尾也是“已是最新”。](/screenshots/zh-Hans/settings-16-up-to-date.svg)

有新版本时，lip 会提出安装：

![“安装更新”对话框写着“有新版本 lip 0.42.0。”，有“安装”和“以后再说”两个按钮。](/screenshots/zh-Hans/settings-04-update-available.svg)

**安装**立即安装。**以后再说**关闭对话框；新版本的信息留在页面上，**检查更新**下面会多出一行**安装更新**：

![“检查更新”一行显示“0.41.0 → 0.42.0”，下面新出现的“安装更新”一行显示 0.42.0。](/screenshots/zh-Hans/settings-05-update-row.svg)

### 安装更新

1. 在**安装更新**上按 `Enter`，再在**安装**上按 `Enter`。

   ![“安装更新”对话框询问“要安装 lip 0.42.0 吗？”，有“安装”和“取消”两个按钮。](/screenshots/zh-Hans/settings-19-install-question.svg)

2. lip 下载发行版、校验并安装。接下来的情况取决于 lip 当初是怎么安装的。

   如果 lip 是用便携版压缩包安装的，它的文件会立即被替换：

   ![“安装更新”对话框写着“lip 0.42.0 已安装。请重启 lip 以使用新版本。”](/screenshots/zh-Hans/settings-20-installed.svg)

   如果 lip 是用 Windows 安装程序安装的，lip 会启动新版本的安装程序。安装程序要修改程序文件，所以 Windows 会请求你的许可：

   ![“安装更新”对话框写着“lip 0.42.0 的安装程序正在运行。请退出 lip 以完成安装。”](/screenshots/zh-Hans/settings-22-installer.svg)

### 安装之后

屏幕上的 lip 仍然是旧版本。这一行会提醒你：

![“检查更新”一行显示“0.42.0 · 请重启 lip”。“安装更新”一行已经消失。](/screenshots/zh-Hans/settings-21-restart-lip.svg)

- **请重启 lip**：退出 lip，再重新启动。这时运行的就是新版本。
- **安装程序运行中**：退出 lip，让安装程序替换它；然后重新启动 lip。

在那之前，在**检查更新**上按 `Enter` 只会再次显示同一条消息，不会重新检查。

对应的命令是 [`lip self-update --check`](../reference/commands/self_update.md) 和 `lip self-update`。更新的完整说明见[安装](../intro/installation.md)。

## 守护进程

**守护进程**是 lip 的后台进程。它执行命令和界面，并运行你启动的服务器，所以关闭终端后服务器仍在运行。详见 [lip 的工作方式](../concepts/how_lip_works.md)。

这一行显示守护进程是否在运行。移到这一行，列表下面会显示更多信息：

![“守护进程”一行显示“运行中 · 0.41.0 · PID 4120”，列表下面的内容以“进行中的命令：1 · 运行中的服务器：0”结尾。](/screenshots/zh-Hans/settings-17-daemon.svg)

1. **运行中**，以及守护进程的 lip 版本。
2. **PID**：守护进程的进程 ID，系统的任务管理器用它来标识进程。
3. **进行中的命令**：守护进程此刻正在执行多少条命令。每个打开的界面算一条，所以这里至少是 1。
4. **运行中的服务器**：守护进程此刻运行着多少个服务器，所有工作区都算在内。只有当你的界面由这个守护进程执行时才显示。

按 `Enter` 重新查询守护进程，并显示新的数字。

**未运行**表示没有守护进程响应。这时界面由 lip 自己执行，你在其中启动的服务器会在退出 lip 时停止。

界面不能停止守护进程，因为界面本身就是由守护进程执行的。请退出 lip，然后运行 [`lip daemon stop`](../reference/commands/daemon.md)。

## 关于

最后一行显示正在运行的 lip 版本。在这里按 `Enter` 没有反应。

![选中了“关于”一行，显示“lip 0.41.0”。下面一行写着“当前运行的 lip 版本。”](/screenshots/zh-Hans/settings-18-about.svg)

报告问题时请提供这个版本号。对应的命令是 `lip --version`。
