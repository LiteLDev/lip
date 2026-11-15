# 第一个服务器

读完这一页，你的电脑上会运行着一个装有 LeviLamina 和一个模组的 Minecraft 基岩版服务器。你从一个空文件夹出发，全程在 `lip` 的界面里完成，除了 `lip` 本身不用输入任何命令。

整个过程大约十分钟，大部分时间在等下载。

## 你需要准备

- 已经安装好的 `lip`，见[安装](installation.md)。`lip -v` 应当能输出版本号。
- 一个终端窗口。下面的图宽 100 个字符、高 28 行；只要宽度不少于 80 个字符，用起来都很顺手。
- 网络连接。`lip` 从 Minecraft 官网下载游戏，从 GitHub 下载 LeviLamina 和模组。

::: tip 怎样看图
每张图都是 `lip` 在那一步真实显示的内容。橙色的标记是为这一页加上去的：方框和带数字的圆圈指向正文提到的地方，图下方的按键就是接下来要按的键。
:::

## 启动 lip

1. 建一个文件夹用来放你的服务器，进入它，然后启动 `lip`。文件夹放在哪里都行，这一页用的是 `D:\servers`。

   ::: code-group

   ```powershell [Windows]
   mkdir D:\servers
   cd D:\servers
   lip
   ```

   ```shell [Linux 和 macOS]
   mkdir -p ~/servers
   cd ~/servers
   lip
   ```

   :::

   `lip` 的界面铺满整个终端，显示的是「主页」。

   ![lip 在空文件夹 D:\servers 里的「主页」。左边的侧边栏列出九个页面；页面上写着游戏未安装，并列出三个步骤，其中第一步“创建服务器”已被选中。](/screenshots/zh-Hans/first-server-01-home.svg)

   - **(1)** 侧边栏列出 `lip` 的九个页面，当前显示的页面带有标记。
   - **(2)** 概览告诉你 `lip` 在哪个文件夹里工作、里面有什么。现在什么都没有：没有游戏，没有加载器，也没有模组。
   - **(3)** **下一步**就是这一页要带你做的三件事：**创建服务器**、**安装模组**、**启动服务器**。
   - **(4)** 最下面一行始终列出此刻可用的按键。

   你只需要记住几个键：`↑` 和 `↓` 移动，`Enter` 打开或确认，`Esc` 返回。没有需要背的字母快捷键。

::: details 如果 lip 提示“无法连接 GitHub”
`lip` 启动时会检查能否连上 GitHub，因为 LeviLamina 和模组都来自那里。连不上的话，它会问你怎么办：

![「主页」上方的对话框“无法连接 GitHub”。它说明了连不上的原因，问“lip 应如何连接 GitHub？”，并给出三个回答：“使用代理”“直接连接”和“以后再说”。](/screenshots/zh-Hans/first-server-25-github.svg)

- **(1)** **使用代理**：你手上有 GitHub 代理的网址时选它。`lip` 会让你输入网址，先测试，之后一直使用它。
- **(2)** **直接连接**：你的网络其实连得上 GitHub（比如刚才只是暂时断线）时选它。`lip` 不会再问。
- **(3)** **以后再说**：`lip` 什么都不改，下次启动时再问。按 `Esc` 也是同样的效果。

连不上 GitHub，`lip` 就下载不了 LeviLamina 和模组。你随时可以在「设置」页面改变这个回答，见[设置](../interface/settings.md)。
:::

## 创建服务器

2. 在「主页」上，**创建服务器**已被选中。按 `Enter`。

   `lip` 问你服务器放在哪里。**当前文件夹**会把它直接放进 `D:\servers`。每个服务器都应该有自己的文件夹，所以这里选**其他文件夹…**：按 `↓`，再按 `Enter`。

   ![对话框“新建服务器”有两个选项：“当前文件夹”和“其他文件夹…”。“其他文件夹…”被标了出来。](/screenshots/zh-Hans/first-server-02-where.svg)

   ::: tip
   如果你是在一个专门为这个服务器准备的空文件夹里启动的 `lip`，就选**当前文件夹**，然后直接看第 5 步。
   :::

3. 文件夹浏览器打开了。它显示 **(1)** 当前打开的文件夹 `D:\servers`，以及 **(2)** 里面的内容：只有一个 `..`，用来回到上一级文件夹。

   在这里为服务器新建一个文件夹：按 `Tab` 移到按钮上，按 `→` 移到**新建文件夹…** **(3)**，再按 `Enter`。

   ![文件夹浏览器，打开的是 D:\servers。列表里只有“..”，下方是“选择此文件夹”“新建文件夹…”和“取消”三个按钮。](/screenshots/zh-Hans/first-server-03-folder.svg)

4. 给文件夹起个名字，例如 `survival`，然后按 `Enter`。`lip` 会创建 `D:\servers\survival`，并把它作为服务器的文件夹。

   ![文件夹浏览器上方的小对话框“新建文件夹”，“名称”一栏里输入了“survival”。](/screenshots/zh-Hans/first-server-04-folder-name.svg)

5. 选择**游戏版本**。`lip` 列出基岩版专用服务器的各个版本，最新的在最上面。

   - **(1)** **LeviLamina ✓** 表示 LeviLamina 支持这个版本，可以装模组。
   - **(2)** **无 LeviLamina** 表示这个版本只能开不带模组的纯净服务器。

   选中带有 **LeviLamina ✓** 的最新版本（通常就是第一行），按 `Enter`。

   ![对话框“游戏版本”列出八个版本，大多标着“LeviLamina ✓”，有三个写着“无 LeviLamina”。第一个版本 1.26.60 已被选中，并标着“最新”。](/screenshots/zh-Hans/first-server-05-game-version.svg)

   ::: info 在 Linux 上
   在 x64 的 Linux 上，`lip` 接下来会问**构建**。请选 **windows**：只有它支持 LeviLamina 及其模组，`lip` 会通过 Wine 运行它。见 [Linux 与 macOS](../guides/linux_macos.md)。
   :::

6. 选择**加载器**。有了加载器，服务器才能运行模组。保持选中最新的 LeviLamina，按 `Enter`。

   ![对话框“加载器”列出四个 LeviLamina 版本，最新的已被选中；最后一项是“不安装加载器”。](/screenshots/zh-Hans/first-server-06-loader.svg)

7. 动手之前，`lip` 会先把计划给你看。读一读：

   - **(1)** 以 `Dry run:` 开头的几行说明 `lip` 将要安装什么：游戏（`github.com/LiteLDev/bds`）和 LeviLamina。无论界面是什么语言，这几行都是英文，因为它们就是 `lip setup` 这条命令的原话。
   - **(2)** **应用**会执行这个计划。**取消**和 `Esc` 则保持原样，什么都不改。

   按 `Enter` 应用。

   ![对话框“安装服务器”里有三行以“Dry run:”开头的文字：lip 将安装游戏 1.26.60 和 LeviLamina 26.60.3，并会请你接受 Minecraft EULA。下方是“应用”和“取消”两个按钮。](/screenshots/zh-Hans/first-server-07-plan.svg)

   `lip` 对服务器做的每一次改动都是这个流程：先给计划，等你选**应用**。

8. Minecraft 服务器受《Minecraft 最终用户许可协议》和《Microsoft 隐私政策》约束。请到图中的两个网址阅读它们。要接受并继续，在**接受**上按 `Enter`。

   ![对话框“Minecraft EULA”列出《Minecraft 最终用户许可协议》和《Microsoft 隐私政策》及其网址。“接受”按钮被标了出来，旁边是“拒绝”。](/screenshots/zh-Hans/first-server-08-eula.svg)

   每次安装游戏，`lip` 都会问一遍。如果你拒绝，就什么都不会安装。

9. `lip` 开始下载和安装。等两行都完成。

   ![工作中的对话框“安装服务器”：游戏的下载进度是 100%，LeviLamina 的下载进度是 62%。](/screenshots/zh-Hans/first-server-09-progress.svg)

   第一次使用时，`lip` 可能还会安装服务器对系统的要求：

   - 在 Windows 上，是 Microsoft Visual C++ 可再发行程序包。Windows 会问你是否允许它的安装程序进行更改。
   - 在 Linux 和 macOS 上，是 Wine。下载量比较大，不过你所有的服务器共用这一份。

   按 `Esc` 可以取消，`lip` 会把已经做的改动撤销掉。

10. 看到 **✓ 完成。** 就说明服务器装好了。按 `Enter` 关闭对话框。

    ![完成后的对话框“安装服务器”：两行文字说明游戏和 LeviLamina 已安装，后面是“✓ 完成。”和“确定”按钮。](/screenshots/zh-Hans/first-server-10-result.svg)

11. 现在你在「工作区」页面。工作区就是放着一个服务器的文件夹。

    - **(1)** 新建的服务器已经在列表里。**●** 标出 `lip` 现在所在的工作区。
    - **(2)** 不管在哪个页面，最上面一行始终显示这个工作区。

    回到「主页」：按 `Esc` 移到侧边栏，按六次 `↑` 到**主页**，再按 `Enter`。

    ![「工作区」页面列出 D:\servers 和 D:\servers\survival。后者已被选中并带有圆点，窗口最上面一行显示的也是它。](/screenshots/zh-Hans/first-server-11-workspace.svg)

## 安装模组

12. 「主页」变了：**(1)** 概览里有了游戏和加载器，**(2)** 第一步打上了勾，**安装模组**已被选中。按 `Enter`。

    ![D:\servers\survival 的「主页」：游戏是 1.26.60，加载器是 LeviLamina 26.60.3。第 1 步打了勾，第 2 步“安装模组”已被选中。](/screenshots/zh-Hans/first-server-12-home-server.svg)

13. 这是「获取模组」页面，显示的是 [levimc.org](https://levimc.org/mods/) 上的模组。

    - **(1)** 最上面可以搜索、排序和筛选。
    - **(2)** 列表里是各个模组，以及它的作者、下载量、星标数和最近更新日期。
    - **(3)** 列表下方是所选模组的详情。

    这一页要装的是 LeviOptimize，一个让服务器运行得更快的模组。先搜索它：按三次 `↑`，从列表移到**搜索**。

    ![「获取模组」页面：上面是搜索、排序和筛选，中间是八个模组的列表，带有下载量、星标数和日期，下面是所选模组 LegacyScriptEngine 的详情。](/screenshots/zh-Hans/first-server-13-get-mods.svg)

14. 输入 `optim`。列表随着你的输入不断缩短，直到只剩 LeviOptimize。按 `Enter` 转到结果。

    ![在搜索里输入了“optim”的「获取模组」页面。列表里只有 LeviOptimize 一个模组，下方的详情也是它的。](/screenshots/zh-Hans/first-server-14-search.svg)

15. LeviOptimize 已被选中。按 `Enter` 打开可以对它做的操作，再在**安装…**上按一次 `Enter`。

    ![LeviOptimize 的菜单有三行：“安装…”“README”和“在浏览器中打开”。“安装…”被标了出来。](/screenshots/zh-Hans/first-server-15-menu.svg)

    **README** 显示作者写的说明。安装不熟悉的模组之前，值得先读一读。

16. 选择版本。

    - **(1)** `lip` 只列出与你的游戏和 LeviLamina 相配的版本，最新的在最上面。
    - **(2)** 最后一行是 `lip` 将要安装的内容。

    保持选中最新版本，按 `Enter`。

    ![LeviOptimize 的版本对话框列出 1.8.1 和 1.8.0 两个版本及其日期，还有“自定义…”。最下面一行是 github.com/LiteLDev/LeviOptimize@1.8.1。](/screenshots/zh-Hans/first-server-16-versions.svg)

17. `lip` 同样先给出计划：它将安装 LeviOptimize 1.8.1，并创建存放模组的 `mods/` 文件夹。如果一个模组还需要别的模组，它们也会列在这里。在**应用**上按 `Enter`。

    ![安装计划：“Dry run: would install LeviOptimize 1.8.1 (5 files)”和“Dry run: would create the directory mods/”，下方是“应用”和“取消”两个按钮，“应用”被标了出来。](/screenshots/zh-Hans/first-server-17-mod-plan.svg)

18. 看到 **✓ 完成。** 就说明模组装好了。按 `Enter`。

    ![安装结果：lip 安装了 github.com/LiteLDev/LeviOptimize 这个包的 1.8.1 版本，后面是“✓ 完成。”和“确定”按钮。](/screenshots/zh-Hans/first-server-18-mod-result.svg)

19. 列表里的 LeviOptimize 前面多了一个 **✓**：它已经安装。

    回到「主页」：按 `Esc` 移到侧边栏，按三次 `↑`，再按 `Enter`。

    ![安装之后的「获取模组」页面：LeviOptimize 那一行以对勾开头。](/screenshots/zh-Hans/first-server-19-mod-installed.svg)

## 启动服务器

20. 前两步都打了勾 **(1)**，**启动服务器**已被选中 **(2)**。按 `Enter`。

    ![「主页」：已安装一个模组，第 1 步和第 2 步打了勾，第 3 步“启动服务器”已被选中。](/screenshots/zh-Hans/first-server-20-home-mods.svg)

21. `lip` 启动服务器，并显示「服务器」页面。

    - **(1)** 第一行说明服务器正在运行，以及已经运行了多久。最上面一行和侧边栏里也有一个绿色圆点。
    - **(2)** **输出**是服务器写出的内容。等到出现 `Server started.` 这一行，玩家就可以进服了。带 `[LeviLamina]` 的几行显示模组正在加载。
    - **(3)** 最下面一行用来输入给服务器的命令。

    ![服务器运行时的「服务器」页面。输出里可以看到服务器启动、LeviLamina 加载 LeviOptimize、端口 19132 和 19133，以及“Server started.”。下方是输入行。](/screenshots/zh-Hans/first-server-21-running.svg)

    想进去玩，就在 Minecraft 里添加一个服务器，地址填这台电脑的地址，端口填 `19132`，也就是输出里写的那个端口。

22. 给服务器发一条命令。输入 `list`（游戏自带的命令，用来列出在线玩家），按 `Enter`。

    ![输入行里输入了“list”的「服务器」页面，输入行被标了出来。](/screenshots/zh-Hans/first-server-22-command.svg)

23. 输出里出现了 **(1)** 你发出的命令和 **(2)** 服务器的回答。现在还没有人加入。

    游戏和模组的命令在这里的用法和在服务器控制台里一样，不用加开头的 `/`。

    ![服务器输出的最后是“> list”和回答“There are 0/10 players online:”。](/screenshots/zh-Hans/first-server-23-answer.svg)

24. 停止服务器：按 `Ctrl+C`。`lip` 向服务器发送它的 `stop` 命令，然后等待。服务器结束后，第一行变成**已退出，退出码 0**，表示它是正常结束的。

    ![服务器停止后的「服务器」页面。第一行是“已退出，退出码 0”，输出以“Quit correctly”结尾，“启动”按钮被标了出来。](/screenshots/zh-Hans/first-server-24-stopped.svg)

    被标出来的**启动**按钮可以再次启动服务器。

要离开 `lip`，按 `Esc` 直到回到侧边栏，再按一次 `Esc`，然后确认。如果还有服务器在运行，询问时会告诉你它是否继续运行。以后在任何文件夹里再次启动 `lip`，都能在「工作区」页面找到你的服务器。

## 你现在有了什么

- 一个文件夹 `D:\servers\survival`，里面有游戏、LeviLamina 和一个模组。它就是个普通文件夹：可以打开看，可以备份，也可以挪走。
- 一个可以在「服务器」页面启动和停止的服务器。

::: warning 改动之前先停止服务器
服务器运行期间，`lip` 不会在它的文件夹里安装、更新或移除任何东西，也不会保存 `server.properties`、白名单和权限。请先停止服务器，再做改动。
:::

## 接下来做什么

- 修改服务器名称、游戏模式或玩家人数：[游戏配置](../interface/game_config.md)。
- 找更多模组：[获取模组](../interface/get_mods.md)。更新或移除已有的模组：[模组](../interface/mods.md)。
- 修改某个模组的设置：[模组配置](../interface/mod_config.md)。
- 让服务器崩溃后自动重启，了解「服务器」页面的其他功能：[服务器](../interface/server.md)。
- 再开一个服务器，或更换游戏版本：[工作区](../interface/workspaces.md)。
- 系统地了解界面：[界面导览](../interface/tour.md)。
- 改用命令完成这一切：[用命令开服](../guides/server_from_commands.md)。
- 出了问题？见[故障排查](../guides/troubleshooting.md)。
