# 工作区

读完本页，你就能同时管理多个服务器：新建服务器、在它们之间切换、添加已有的服务器，以及更改某个服务器的游戏版本或 LeviLamina 版本。

## 什么是工作区

**工作区**就是放着一个服务器的文件夹：游戏、LeviLamina、模组、存档，以及 lip 自己保存在 `.lip/` 里的记录。界面的每个页面同一时间只操作一个工作区，即**当前工作区**，顶栏会显示它的路径。

lip 会记住你用过的工作区，列成一张列表。启动 `lip` 时，你所在的文件夹如果不在列表里，就会被加进去，并成为当前工作区。列表在两次运行之间保留。

## 工作区列表

打开页面：在侧边栏移到**工作区**，按 `Enter`。

![“工作区”页：先是“新建服务器…”和“添加已有文件夹…”两行，然后是三个文件夹。第一个前面有圆点，最后一个标着“已丢失”。](/screenshots/zh-Hans/workspaces-01-list.svg)

1. 两个操作：**新建服务器…**创建一个服务器，**添加已有文件夹…**添加一个已有服务器的文件夹。
2. 当前工作区，用 `●` 标出。
3. 文件夹已不存在的工作区，标着**已丢失**。

用 `↓` 向下移到列表里，或者按 `Tab`。列表下方显示焦点所在行的完整路径，以及一句说明。

![焦点在第一个文件夹上的列表。第二个文件夹标着“服务器运行中”，第三个标着“已丢失”。列表下方是第一个文件夹的路径和“当前工作区。”](/screenshots/zh-Hans/workspaces-03-marks.svg)

1. `●`：当前工作区。
2. **服务器运行中**：这个工作区的服务器正在运行，不管是谁启动的。
3. **已丢失**：文件夹被删除、移动或改名了。lip 只能把它从列表里移除。
4. 完整路径（路径太长时，列表里会截掉开头），以及这一行是什么。

在工作区上按 `Enter` 打开它的操作。有哪些操作取决于工作区：

| 操作 | 何时提供 | 作用 |
| --- | --- | --- |
| **切换** | 除当前工作区和已丢失的之外。 | [切换到这个工作区](#切换到另一个工作区)。 |
| **更改游戏或加载器版本…** | 除已丢失的之外。 | 用别的版本[重新安装服务器](#更改游戏或加载器的版本)。 |
| **从列表移除** | 除当前工作区之外。 | [把它从列表里去掉](#从列表移除工作区)，不改动任何文件。 |

## 切换到另一个工作区

1. 移到那个工作区，按 `Enter`。

   ![文件夹 creative 的菜单，有三行：“切换”“更改游戏或加载器版本…”和“从列表移除”。](/screenshots/zh-Hans/workspaces-02-menu.svg)

2. 在**切换**上按 `Enter`。

   ![切换之后：顶栏和右下角的消息都显示 D:\servers\creative，列表里圆点移到了这个文件夹前面。](/screenshots/zh-Hans/workspaces-09-switched.svg)

   1. 顶栏显示新的工作区。此后每个页面显示的都是它：它的服务器、它的模组、它的配置。
   2. `●` 移到了它上面。

切换不会停止任何东西。你离开的那个工作区里如果有服务器在运行，它会继续运行，列表里标着**服务器运行中**。

如果[游戏配置](game_config.md)或[模组配置](mod_config.md)页有未保存的更改，lip 会先问你怎么处理，然后再切换。

## 添加已有文件夹

lip 的列表里还没有的服务器可以这样加进来：比如用命令安装的服务器，或者从别的电脑复制过来的服务器。

1. 移到**添加已有文件夹…**，按 `Enter`。

   ![选中了“添加已有文件夹…”一行。列表下方写着“添加一个已有服务器的文件夹。”](/screenshots/zh-Hans/workspaces-10-add-row.svg)

2. 文件夹浏览器从当前工作区打开。前往服务器所在的文件夹：`←` 返回上一级，`↑` `↓` 移动，`Enter` 或 `→` 打开文件夹。

   ![标题为“添加文件夹”的文件夹浏览器显示 D:\servers，里面有 creative、lobby、skyblock 和 survival 四个文件夹，选中的是 lobby。](/screenshots/zh-Hans/workspaces-11-browse.svg)

   1. 列表当前显示的文件夹。也可以在这里输入或粘贴路径。
   2. 其中的一个文件夹。

3. 浏览器显示的就是服务器的文件夹时，按 `Tab` 到按钮，在**选择此文件夹**上按 `Enter`。

   ![浏览器显示 D:\servers\lobby，里面有 behavior_packs、mods、resource_packs 和 worlds 四个文件夹。“选择此文件夹”按钮被标出。](/screenshots/zh-Hans/workspaces-12-select-folder.svg)

4. lip 把这个文件夹加到列表末尾，并切换到它。

   ![列表末尾现在是 D:\servers\lobby，前面有圆点。顶栏显示的也是它。](/screenshots/zh-Hans/workspaces-13-added.svg)

lip 不检查文件夹里有什么：任何存在的文件夹都可以添加。已经在列表里的文件夹不会重复添加，而是切换过去。浏览器的完整用法见[文件夹和文件浏览器](tour.md#文件夹和文件浏览器)。

## 从列表移除工作区

移除只是把工作区从列表里去掉，仅此而已。**不会删除任何文件**：服务器还在你的磁盘上，以后可以再把它的文件夹添加回来。

1. 移到那个工作区，按 `Enter`。对于已丢失的文件夹，**从列表移除**是唯一的操作。

   ![已丢失的文件夹 skyblock 的菜单，只有“从列表移除”一行。](/screenshots/zh-Hans/workspaces-06-menu-missing.svg)

2. 在**从列表移除**上按 `Enter`。lip 会先询问。

   ![skyblock 的对话框询问“要从列表中移除吗？不会删除任何文件。”，有“从列表移除”和“取消”两个按钮。](/screenshots/zh-Hans/workspaces-07-remove.svg)

3. 再在**从列表移除**上按 `Enter`。

   ![列表里已经没有 skyblock。最下面一行的末尾是“已从列表移除。”](/screenshots/zh-Hans/workspaces-08-removed.svg)

当前工作区不能移除：它的菜单里没有这一行。请先切换到别的工作区。

![当前工作区 survival 的菜单，只有“更改游戏或加载器版本…”一行。](/screenshots/zh-Hans/workspaces-04-menu-selected.svg)

## 新建服务器

**新建服务器…**是一个向导：它用一个接一个的对话框问你几个问题，然后安装服务器。按 `Esc` 返回上一步；在第一步按 `Esc` 则关闭向导，什么都不会改变。

[主页](home.md)的第一个步骤打开的也是这个向导。[第一个服务器](../intro/first_server.md)带你从头到尾走一遍；本节说明其中的每一个选择。

### 第 1 步：服务器放在哪里

在**新建服务器…**上按 `Enter`。

如果当前工作区里还没有服务器，lip 会先提供这个文件夹：

![“新建服务器”对话框有“当前文件夹”和“其他文件夹…”两行。选中的是“当前文件夹”，下面一行显示 D:\servers\survival。](/screenshots/zh-Hans/new-server-11-this-folder.svg)

1. **当前文件夹**把服务器安装在当前工作区里。
2. 它的路径。

**其他文件夹…**打开文件夹浏览器。当前工作区已经有服务器时，会直接打开浏览器。

![“新建服务器”对话框，“其他文件夹…”一行被标出。](/screenshots/zh-Hans/new-server-01-where.svg)

在浏览器里前往要放服务器的位置。

![标题为“新建服务器”的文件夹浏览器显示 D:\servers，里面有 backups 和 creative 两个文件夹，以及“选择此文件夹”“新建文件夹…”“取消”三个按钮。](/screenshots/zh-Hans/new-server-02-folder.svg)

1. 当前显示的文件夹。**选择此文件夹**把服务器直接装在这里。
2. **新建文件夹…**会询问名称，然后把服务器装在当前文件夹里以该名称命名的文件夹中。

![“新建文件夹”对话框，“名称”输入框里是“survival”。](/screenshots/zh-Hans/new-server-03-folder-name.svg)

也可以在顶部的地址栏里输入路径，包括还不存在的路径。只有在你做完所有选择之后，lip 才会创建文件夹；在那之前不会创建任何东西。

服务器正在运行的文件夹（**请先停止服务器。**）和指向文件的路径（**这是一个文件**）会被 lip 拒绝。

### 第 2 步：游戏版本

lip 读取可以下载的游戏版本（需要一点时间）并列出来：先是正式版，从新到旧；然后是标着**预览版**的预览版本。

![“游戏版本”对话框列出 1.26.60 到 1.21.130。大多数行显示“LeviLamina ✓”，第一行标着“最新”。](/screenshots/zh-Hans/new-server-04-game-version.svg)

版本后面的一栏说明这个版本能不能装模组：

![同一个列表，选中的是显示“无 LeviLamina”的 1.26.30。下面一行写着“模组需要 LeviLamina，而此版本没有。”](/screenshots/zh-Hans/new-server-12-no-levilamina.svg)

1. **LeviLamina ✓**：有支持此版本的 LeviLamina。要装模组，就选这样的版本。
2. **无 LeviLamina**：没有为这个版本制作的 LeviLamina。这样的版本只能作为纯净服务器。
3. 对所选行的文字说明。

::: info 这一栏为什么重要
模组不能直接在游戏上运行。它们需要加载器 **LeviLamina**，而每个 LeviLamina 版本只对应一个游戏版本。最新的游戏版本不一定已经有对应的 LeviLamina。
:::

这一栏的内容来自 levimc.org 的模组目录。如果目录几秒钟内没有响应，列表就不显示这一栏；到加载器那一步仍然会告诉你哪些版本合适。

用 `↑` `↓` 选择版本，按 `Enter`。

### 第 3 步：构建（仅限 x64 的 Linux）

游戏有两种**构建**：Windows 服务器和 Linux 服务器。LeviLamina 及其模组需要 Windows 服务器。

只有在两种都能运行的地方（x64 处理器上的 Linux），并且所选版本两种构建都有时，lip 才会询问构建。其他情况下构建就是 `windows`，你不会看到这一步。在 Linux 上，只有其中一种构建的版本会在版本列表里注明：

![Linux 上的游戏版本列表。1.26.30 一行在“无 LeviLamina”后面标着“仅 windows”。](/screenshots/zh-Hans/new-server-16-linux-games.svg)

1. 没有 Linux 服务器的版本：选它时 lip 不会询问构建。

![“构建”对话框有 windows 和 linux 两行。选中的是 linux，下面一行写着“没有加载器；仅适用于 x64 的 Linux。”](/screenshots/zh-Hans/new-server-17-build.svg)

1. **windows**：Windows 服务器，lip 通过 Wine 运行它。只有它支持 LeviLamina 及其模组。
2. **linux**：Linux 服务器。它不需要 Wine，没有加载器，也不能运行模组。向导会跳过加载器这一步。

关于 Wine 和 Linux 服务器，见 [Linux 与 macOS](../guides/linux_macos.md)。

### 第 4 步：加载器

lip 查找适合所选游戏版本的 LeviLamina 版本（可能需要一点时间）并列出来，从新到旧，最后是**不安装加载器**。

![“加载器”对话框列出标着“最新”的 LeviLamina 26.60.3 到 26.60.0，然后是“不安装加载器”。下面一行写着“让服务器可以运行模组。”](/screenshots/zh-Hans/new-server-05-loader.svg)

要装模组，就在第一行（最新的 LeviLamina）上按 `Enter`。

**不安装加载器**只安装游戏：一个不能运行模组的纯净服务器。

![同一个对话框，选中的是“不安装加载器”。下面一行写着“纯净服务器，不带模组。”](/screenshots/zh-Hans/new-server-14-no-loader.svg)

如果没有适合该游戏版本的 LeviLamina，这一步会说明，但不会让你无路可走：

![“加载器”对话框以“没有支持此游戏版本的 LeviLamina。”开头，有“不安装加载器”和“返回”两行。](/screenshots/zh-Hans/new-server-13-no-loader-fits.svg)

1. **不安装加载器**继续安装纯净服务器。
2. **返回**回到游戏版本列表，你可以改选带 **LeviLamina ✓** 的版本。按 `Esc` 也一样。

### 第 5 步：计划

这时 lip 创建文件夹（如果它还不存在），把它加入列表并切换过去。然后显示将要安装的内容，此时还没有安装任何东西。

![“安装服务器”对话框列出三行以“Dry run:”开头的内容，有“应用”和“取消”两个按钮。](/screenshots/zh-Hans/new-server-06-plan.svg)

这些行是命令的原话，是英文：这里是游戏、LeviLamina，以及 lip 会请你接受许可协议。

- **应用**开始安装。
- **取消**什么也不安装。文件夹会留下（是空的），并且仍是当前工作区；你可以在里面重新运行向导，或者[把它从列表移除](#从列表移除工作区)。

### 第 6 步：许可协议

游戏属于 Microsoft。在 lip 下载它之前，你必须接受《Minecraft 最终用户许可协议》和《Microsoft 隐私政策》。请到显示的两个网址阅读它们。

![“Minecraft EULA”对话框写出两份文件的名称和网址，有“接受”和“拒绝”两个按钮。](/screenshots/zh-Hans/new-server-07-eula.svg)

**接受**继续。**拒绝**关闭对话框，不安装任何东西：

![同一个对话框，“拒绝”按钮被标出。](/screenshots/zh-Hans/new-server-15-eula-decline.svg)

### 第 7 步：下载与结果

lip 下载并安装游戏和 LeviLamina。按 `Esc` 取消，已经做的部分会被撤销。

![“安装服务器”对话框显示两项下载，一项是 100%，另一项是 62%。](/screenshots/zh-Hans/new-server-08-progress.svg)

完成后，对话框列出安装了什么，最后一行是 **✓ 完成。** 按 `Enter`。

![“安装服务器”对话框列出已安装的两个包，最后是“完成。”和“确定”按钮。](/screenshots/zh-Hans/new-server-09-result.svg)

新服务器成为当前工作区，焦点落在列表里它的那一行上。

![工作区列表里有了新文件夹 D:\servers\survival，前面有圆点。](/screenshots/zh-Hans/new-server-10-workspace.svg)

到[服务器](server.md)页启动它，或者先去[获取模组](get_mods.md)。

::: details 用命令做同样的事
向导在新文件夹里按你的选择执行 [`lip setup`](../reference/commands/setup.md)，例如：

```shell
lip setup --game 1.26.60 --loader levilamina@26.60.3
```

选**不安装加载器**时是 `--no-loader`，选 Linux 服务器时是 `--build linux`。
:::

## 更改游戏或加载器的版本

要把服务器换到另一个游戏版本或另一个 LeviLamina 版本，就重新安装一次。向导和新建时一样，只是不问文件夹，而且每一步都从服务器现有的版本开始。

1. 移到那个工作区，按 `Enter`，选择**更改游戏或加载器版本…**。

2. 选择游戏版本。服务器现有的版本标着**已安装**，并且已被选中。

   ![“游戏版本”对话框，选中的 1.26.60 标着“最新”和“已安装”。](/screenshots/zh-Hans/workspaces-14-change-game.svg)

3. 选择加载器。对话框一开始选中服务器现有的 LeviLamina；没有加载器时选中**不安装加载器**。

   ![“加载器”对话框，选中的是最新版本下面一行的 LeviLamina 26.60.2。](/screenshots/zh-Hans/workspaces-15-change-loader.svg)

4. 阅读计划，选择**应用**。

   ![“安装服务器”对话框写着 lip 将卸载 LeviLamina 26.60.3 并安装 LeviLamina 26.60.2，有“应用”和“取消”两个按钮。](/screenshots/zh-Hans/workspaces-16-change-plan.svg)

   1. lip 将要移除和安装的内容。
   2. 选择**应用**之前不会有任何更改。

如果这个工作区不是当前工作区，lip 会在你做完选择之后、显示计划之前切换到它。

::: warning 不适合新版本的模组
模组是为特定的 LeviLamina 版本制作的。如果某个已安装的模组没有适合你所选游戏或 LeviLamina 的版本，计划里会点名，并说明这次安装会卸载它。这时选择**应用**就会卸载这些模组。想保持原样，请选**取消**。
:::

如果选的就是服务器现有的版本，那就没有事情可做，计划里只有**关闭**：

![“安装服务器”对话框写着“不会有任何更改。”，只有“关闭”一个按钮。](/screenshots/zh-Hans/plan-02-no-changes.svg)

更换游戏版本会保留 `server.properties`、`allowlist.json`、`permissions.json` 和你的存档。旧游戏的其他文件会被删除，你复制到 `behavior_packs/` 或 `resource_packs/` 里的资源包也会随之删除；完整的清单见[更换游戏后保留什么](../reference/files.md#更换游戏后保留什么)。请先复制一份文件夹：旧版本的游戏可能打不开新版本保存的存档。

### 在别的系统上打开使用 Linux 构建的服务器

用 Linux 服务器安装的工作区只能在 x64 的 Linux 上运行。在其他系统上，向导无法提供这种构建。它会在你做任何选择之前说明这一点，然后按 Windows 服务器继续：

![“游戏版本”对话框开头有两行：“此工作区使用 linux 构建，它无法在本系统上运行。安装时会改用 windows 构建。”](/screenshots/zh-Hans/workspaces-17-linux-build.svg)

1. 提示，在你做任何选择之前就显示。

随后计划的标题会写明构建：**改用 windows 构建**，并把 Linux 服务器列在要卸载的内容里：

![“改用 windows 构建”对话框列出 lip 将卸载 Linux 版游戏，并安装 Windows 版游戏和 LeviLamina。](/screenshots/zh-Hans/workspaces-18-switch-to-windows.svg)

## 有服务器在运行时

服务器在运行并不妨碍你使用列表：

- 列表把该工作区标为**服务器运行中**，说明里写着**它的服务器正在运行。**
- **切换**和**从列表移除**照常可用，两者都不会停止服务器。
- **更改游戏或加载器版本…**是灰色的，菜单里写着**请先停止服务器。** lip 不会在服务器运行时更换游戏。

![服务器正在运行的文件夹 creative 的菜单。“更改游戏或加载器版本…”一行是灰色的，分隔线下面写着“请先停止服务器。”](/screenshots/zh-Hans/workspaces-05-menu-running.svg)

新服务器也不能装进服务器正在运行的文件夹。要停止服务器，先切换到它的工作区，再到[服务器](server.md)页操作。各个页面在这期间的行为见[界面参考](../reference/interface.md#服务器运行期间)。

## 同时打开多个界面

你可以在多个终端里打开 `lip`。它们共用同一张列表，即文件 `workspaces.json`（见[文件](../reference/files.md)）：

- 每个界面有自己的当前工作区，所以两个终端可以分别显示两个服务器。
- 一个界面添加或移除的工作区，几秒钟内就会出现在其他界面里（它们的“工作区”页打开时）。
- 多个界面同时做的更改都会保留。
