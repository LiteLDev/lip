# 工具

读完本页，你就能使用界面里为制作包的人准备的四个工具：创建 `lip.toml`、迁移旧的 `tooth.json`、打包 ZIP，以及查看任意一个包的包文件。

如果你只是运行服务器，用不到这个页面。想了解什么是包、怎样发布包，请阅读[创建包](../authors/creating_packages.md)。

## 页面

这些工具在当前工作区里操作。要处理你自己的模组，请在模组的文件夹里启动 `lip`，或者在[工作区](workspaces.md)页添加那个文件夹。

打开页面：在侧边栏移到**工具**，按 `Enter`。

![文件夹 D:\projects\WelcomeMod 里的“工具”页：四个操作，其中“迁移 tooth.json”是灰色的；下面是关于所选操作的一句话，再下面是工作区，以及它有没有 lip.toml 和 tooth.json，两者都是“不存在”。](/screenshots/zh-Hans/tools-05-status.svg)

1. 所选操作的作用。用 `↑` `↓` 移动，按 `Enter` 执行。
2. 当前工作区：这些操作所在的文件夹。
3. 这个文件夹里有没有 `lip.toml` 和 `tooth.json`：**已存在**或**不存在**。lip 每两秒查看一次，所以这两行会跟上你在 lip 之外做的改动。
4. 灰色的操作：按文件夹现在的情况做不了的操作。见[灰色的操作](#灰色的操作)。

| 操作 | 作用 | 命令 |
| --- | --- | --- |
| **创建 lip.toml** | 为新的包写出包文件。 | [`lip pkg init`](../reference/commands/pkg_init.md) |
| **迁移 tooth.json** | 把 lip 0.x 的包文件转换为 `lip.toml`。 | [`lip pkg migrate`](../reference/commands/pkg_migrate.md) |
| **打包 ZIP…** | 把一个文件夹打包成 ZIP 压缩包。 | [`lip pkg pack`](../reference/commands/pkg_pack.md) |
| **查看包信息…** | 显示一个包的包文件和可用版本。 | [`lip pkg view`](../reference/commands/pkg_view.md) |

名称以 `…` 结尾的操作会先询问，然后才动手。

## 灰色的操作

哪些操作做得了，由状态行里的两个文件决定。被它们排除的操作显示为灰色。选中这样的操作时，说明下面会多出一行原因，屏幕最下面一行也不再列出 `Enter`。

![选中了灰色的“迁移 tooth.json”操作。它的说明下面写着“这里没有 tooth.json。”](/screenshots/zh-Hans/tools-06-migrate-disabled.svg)

1. 原因。

| 操作 | 何时变灰 | 页面上的说明 |
| --- | --- | --- |
| **创建 lip.toml** | 文件夹里已有 `lip.toml`。lip 不会替换它。 | **这里已有 lip.toml。** |
| **迁移 tooth.json** | 文件夹里已有 `lip.toml`。 | **这里已有 lip.toml。** |
| **迁移 tooth.json** | 文件夹里没有 `tooth.json`。 | **这里没有 tooth.json。** |

**打包 ZIP…**和**查看包信息…**从不变灰。服务器是否在运行对这个页面没有影响：四个操作都不会改动服务器。

文件的情况一变，操作就恢复可用。想用新的 `lip.toml` 重新开始，就在文件管理器里删掉旧的；两秒之内页面会显示 `lip.toml` **不存在**，**创建 lip.toml** 又可以选择了。

## 创建 lip.toml

1. 移到**创建 lip.toml**，按 `Enter`。lip 会询问包 ID。

   ![“创建 lip.toml”的进度对话框上面有一个标题为“lip 询问”的对话框，询问“the package id of the package (a Go module path, e.g. github.com/owner/repo)”，输入框里是 github.com/steve/WelcomeMod。](/screenshots/zh-Hans/tools-07-init-question.svg)

2. 输入 ID，按 `Enter`。ID 就是包的仓库地址，例如 `github.com/owner/repo`。不是这种地址的回答会被重新询问。按 `Esc` 取消，不写入任何内容。问题本身是命令的原话，是英文。

3. lip 在工作区里写出 `lip.toml` 并告知。按 `Enter`。

   ![“创建 lip.toml”对话框写着“已创建 lip.toml。”，有“确定”按钮。](/screenshots/zh-Hans/tools-08-init-result.svg)

文件里有你给的 ID、版本 `0.1.0`，以及一个针对 Windows x64 服务器的条目：

```toml
format = 4
id = "github.com/steve/WelcomeMod"
version = "0.1.0"

[[variants.""]]
side = "server"
platform = "win-x64"
```

之后请用文本编辑器编辑它；每个字段的含义见 [lip.toml](../concepts/lip_toml.md)。

文件有了之后，这个操作就变灰了：

![选中了灰色的“创建 lip.toml”操作。它的说明下面写着“这里已有 lip.toml。”，lip.toml 的状态行是“已存在”。](/screenshots/zh-Hans/tools-09-init-disabled.svg)

1. lip 不会替换已有的 `lip.toml`。

## 迁移 tooth.json

lip 0.x 的包用 `tooth.json` 描述自己。这个操作写出内容相同的 `lip.toml`，并删除 `tooth.json`。

1. 移到**迁移 tooth.json**。状态行是 `lip.toml` **不存在**、`tooth.json` **已存在**时，它才可以选择。

   ![选中了“迁移 tooth.json”操作。状态行显示 lip.toml 不存在、tooth.json 已存在。](/screenshots/zh-Hans/tools-10-migrate.svg)

   1. 让这个操作可以进行的状态行。

2. 按 `Enter`。如果 `tooth.json` 缺少 `lip.toml` 需要的内容，lip 会逐项询问。这里旧文件没有写明适用的平台：

   ![标题为“lip 询问”的对话框询问“the platforms (RIDs, comma-separated, e.g. win-x64) the variants without a platform apply to”，输入框里是 win-x64。](/screenshots/zh-Hans/tools-11-migrate-question.svg)

   输入回答，按 `Enter`。无效的回答会被重新询问。

3. lip 显示结果。按 `Enter`。

   ![“迁移 tooth.json”对话框里有一行以警告符号开头、关于 preserve_files 的内容，最后是“已将 tooth.json 迁移为 lip.toml。”和“确定”按钮。](/screenshots/zh-Hans/tools-12-migrate-result.svg)

   1. 以 `⚠` 标出的提示：新文件里换了一种写法的内容，或者新文件里没有位置放的内容。请逐条阅读，并检查 `lip.toml` 里对应的部分。提示是英文。
   2. 结果：`lip.toml` 已写入，`tooth.json` 已删除。

4. 状态行现在反过来了，这个操作也变灰了：已经没有可迁移的东西。

   ![状态行显示 lip.toml 已存在、tooth.json 不存在。页面上写着“这里已有 lip.toml。”](/screenshots/zh-Hans/tools-13-migrated.svg)

两种格式之间有哪些变化，见[迁移包](../authors/migrating_packages.md)。

## 打包 ZIP…

这个操作把一个文件夹打包成 ZIP 压缩包，用来附在包的发行版上。它会问两件事：打包哪个文件夹，以及写到哪个文件。

1. 移到**打包 ZIP…**，按 `Enter`。文件夹浏览器从工作区打开，标题是**要打包的文件夹**。

   ![“要打包的文件夹”浏览器显示 D:\projects\WelcomeMod，里面有文件夹 WelcomeMod，以及“选择此文件夹”“新建文件夹…”“取消”三个按钮。](/screenshots/zh-Hans/tools-14-pack-folder.svg)

   1. 将被打包的文件夹。要打包工作区本身就不用动；否则打开别的文件夹。
   2. 按 `Tab` 到按钮，在**选择此文件夹**上按 `Enter`。

2. 第二个浏览器打开，标题是**保存 ZIP**。它默认使用工作区里的 `package.zip` 这个名字。

   ![“保存 ZIP”浏览器显示同一个文件夹，里面有文件 package.zip；“文件名”输入框里是 package.zip，下面有“文件：.zip”的说明，以及“保存”“新建文件夹…”“取消”三个按钮。](/screenshots/zh-Hans/tools-15-pack-name.svg)

   1. ZIP 写入的文件夹。想存到别处，就打开别的文件夹。
   2. ZIP 的文件名。按 `Tab` 到这里可以改名；没写 `.zip` 会自动补上。在这个输入框里按 `Enter` 就是保存。

   按 `Esc` 回到第一步。

3. 如果同名文件已经存在，lip 会先询问再替换。`Enter` 默认按下的是**取消**，它会回到文件名那一步；要替换，先按 `←` 再按 `Enter`。

   ![“打包 ZIP”对话框写着“package.zip 已存在。要替换它吗？”，有“替换”和“取消”两个按钮。](/screenshots/zh-Hans/tools-16-pack-replace.svg)

4. lip 写出压缩包并显示它的路径。按 `Enter`。

   ![“打包 ZIP”对话框写着压缩包已经打包好，并给出它的路径 D:\projects\WelcomeMod\package.zip，有“确定”按钮。](/screenshots/zh-Hans/tools-17-pack-result.svg)

压缩包里是文件夹中的所有文件，路径与在文件夹里相同；压缩包自身，以及文件夹顶层的 `.git` 和 `.lip` 两个文件夹除外。

浏览器的完整用法见[文件夹和文件浏览器](tour.md#文件夹和文件浏览器)。

## 查看包信息…

这个操作在安装之前显示 lip 对一个包的了解：它的包文件，以及有哪些版本。它不会改动工作区。

移到**查看包信息…**，按 `Enter`。lip 会问包在哪里：

![“查看包信息”对话框有三行：“已安装的模组…”“输入包…”和“浏览…”。下面一行说明所选项显示什么。](/screenshots/zh-Hans/tools-02-view-choice.svg)

在这三项里按 `Esc` 都会回到这个选择。

### 已安装的模组

1. 在**已安装的模组…**上按 `Enter`。lip 列出工作区里安装的内容：先是你安装的模组，然后是它们的依赖，最后是游戏和加载器。

   ![“已安装的模组”对话框列出七个包及其版本，下面一行是所选包的完整名称。](/screenshots/zh-Hans/tools-18-view-installed.svg)

   1. 已安装的版本。
   2. 灰色的行：只是因为别的包需要才安装的包。

   太长的名称会从中间截断，列表下面的一行显示完整名称。

2. 在某个包上按 `Enter`。lip 读取它的包文件（可能需要联网）并显示结果。按 `Esc` 停止等待。

   ![标题为 github.com/LiteLDev/LegacyScriptEngine#lua@0.13.1 的查看器显示以 "manifest" 开头的 JSON。](/screenshots/zh-Hans/tools-19-view-result.svg)

   1. `manifest`：已安装版本的包文件，以 JSON 显示。再往下，`versions` 列出仓库里的所有版本。

   用 `↑`、`↓`、`PgUp`、`PgDn`、`Home`、`End` 滚动，按 `Esc` 关闭查看器。

没有安装任何包的工作区里没有可列出的内容：

![“已安装的模组”对话框写着“这里还没有安装任何包。”，有“关闭”按钮。](/screenshots/zh-Hans/tools-22-view-nothing.svg)

### 输入一个包

在**输入包…**上按 `Enter`，像在命令行里那样输入包：一个仓库（想看特定版本就加上 `@` 和版本号）、一个 ZIP 文件的网址，或者一个路径。

![“查看包信息”对话框，“包”输入框里是 github.com/LiteLDev/LeviOptimize@1.8.1。](/screenshots/zh-Hans/tools-20-view-typed.svg)

按 `Enter`。结果的显示方式与已安装的模组相同。

### 磁盘上的文件或文件夹

在**浏览…**上按 `Enter`。浏览器列出文件夹和 `.zip` 文件。

![“选择包”浏览器显示 D:\servers\survival，里面有文件夹 mods 和文件 LeviAntiCheat-windows-x64.zip，下面有“文件：.zip”的说明，以及“选择此文件夹”和“取消”两个按钮。](/screenshots/zh-Hans/tools-21-view-browse.svg)

1. ZIP 文件：按 `Enter` 显示其中的包。
2. **选择此文件夹**把地址栏里的文件夹当作包来显示：也就是含有 `lip.toml` 的文件夹，比如你自己模组的文件夹。
3. 列表显示的文件类型。

## 操作失败时

页面只根据两个状态行来判断操作要不要变灰。操作仍然可能因为别的原因失败：`tooth.json` 写坏了、找不到包、文件夹刚刚被删掉。这时 lip 会显示一个对话框，标题是操作的名称加上**失败**，并且不改动任何东西。

![“迁移 tooth.json · 失败”对话框以“错误: Cannot read tooth.json”开头，后面是三行以“caused by:”开头的内容，并给出一个报告文件。](/screenshots/zh-Hans/tools-23-failed.svg)

1. **错误**：出了什么问题，通常还有该怎么办。每一行 **caused by:** 都把原因再往深处说一层，最后一行往往最具体。这里是 `tooth.json` 里没有 `version`。这段文字来自命令，是英文。
2. **完整报告**：记录详细信息的文件，位于工作区的 `.lip/logs` 下。求助时请附上它，见[故障排除](../guides/troubleshooting.md)。

按 `Enter` 或 `Esc` 关闭对话框。
