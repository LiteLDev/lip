# 创建包

本指南带你从一个空仓库出发，做出任何人都能用 lip 安装的包：编写 `lip.toml`，在自己的服务器上测试，再用 git 标签发布。

**包**是 lip 能安装到服务器里的东西：模组、库、工具、一组文件。从技术上说，它是一个根部带有 `lip.toml` 文件的归档，通常是一个 git 仓库。[lip.toml](../concepts/lip_toml.md) 是所有字段的参考；本指南讲常见的用法。

## 开始之前

你需要：

- [已安装](../intro/installation.md)的 lip；
- 一个放包的 git 仓库，例如 `https://github.com/user/MyMod`；
- 一个用来测试的服务器。请在另一个目录中[创建一个](../intro/first_server.md)。

仓库地址去掉 `https://` 和 `.git` 就是**包 ID**：`github.com/user/MyMod`。用户通过这个 ID 安装你的包。

## 从 `lip pkg init` 开始

在仓库的根目录中运行：

```shell
lip pkg init
```

lip 会询问包 ID，回答不是有效的 ID 时会一直重新询问。然后它写入 `lip.toml`：

```toml
format = 4
id = "github.com/user/MyMod"
version = "0.1.0"

[[variants.""]]
side = "server"
platform = "win-x64"
```

如果已经存在 `lip.toml`，或者命令没有运行在终端中而无法询问，命令会失败。在界面中，[工具](../interface/tools.md)页的**创建 lip.toml** 对当前工作区做同样的事。

## `lip.toml` 的结构

```toml
format = 4                       # 始终为 4
id = "github.com/user/MyMod"     # 包 ID：仓库的路径
version = "1.0.0"                # 版本，不带前导 "v"

[info]                           # 给人和模组列表看的；lip 忽略它
name = "My Mod"
description = "A short description"
tags = ["utility"]

[[variants.""]]                  # 默认变体的一个条目
side = "server"                  # "server" 或 "client"
platform = "win-x64"             # lip 只安装 win-x64 的条目

[variants."".deps]               # 这个条目依赖什么
"github.com/LiteLDev/LeviLamina" = ">=1.0.0 <2.0.0"

[[variants."".assets]]           # 文件从哪里来
type = "mcmod"
urls = ["https://github.com/user/MyMod/releases/download/v{{version}}/MyMod-server-win-x64.zip"]
```

需要知道三件事：

- **条目。** `variants` 把变体名映射到一组条目。每个条目只对应一个运行端和一个平台。`[[variants.""]]` 向默认变体 `""` 添加一个条目。
- **lip 只安装 `win-x64` 的 `server` 条目**，在任何系统上都是如此。在 Linux 和 macOS 上，它通过 Wine 运行 Windows 版服务器。因此服务端模组需要一个 `platform = "win-x64"` 的 `server` 条目。
- **模板。** 每个字符串都是模板：`{{version}}` 会变成文件中的 `version`。把它用在 URL 里，这样发布新版本时只需要改 `version`。

## 模组包

**模组**是带有 `mcmod` 类型资源的条目：这个资源是一个 ZIP 归档，根部有该模组的 LeviLamina `manifest.json`。lip 把整个归档写入 `mods/<name>/`，其中 `<name>` 是该 `manifest.json` 中的 `name`。

1. 构建你的模组，让 `manifest.json` 和模组的文件位于同一个目录中。
2. 打包这个目录：

   ```shell
   lip pkg pack build/ -o MyMod-server-win-x64.zip
   ```

3. 在你的 git 托管平台上，把这个 ZIP 文件附加到该版本的发布中。
4. 在 `lip.toml` 中指向它：

   ```toml
   format = 4
   id = "github.com/user/MyMod"
   version = "1.0.0"

   [info]
   name = "My Mod"

   [[variants.""]]
   side = "server"
   platform = "win-x64"
   preserve = ["mods/MyMod/config/"]

   [variants."".deps]
   "github.com/LiteLDev/LeviLamina" = ">=1.0.0 <2.0.0"

   [[variants."".assets]]
   type = "mcmod"
   urls = ["https://github.com/user/MyMod/releases/download/v{{version}}/MyMod-server-win-x64.zip"]
   ```

lip 只读取 `manifest.json` 中的 `name`，并原样安装这个文件。它不读取其中的 `dependencies`：你的模组需要什么，请自己写进 `deps`。

`preserve` 保留用户改过的内容：安装或更新包时，已存在的文件不会被覆盖；卸载时也不会被删除。它的模式相对于服务器目录，所以要写 `mods/MyMod/config/`，而不是 `config/`。

## 文件包

**文件包**是所有不是模组的包：它把文件放到服务器目录中。每个资源指明从哪个归档取文件，它的 `place` 说明哪些文件放到哪里：

```toml
format = 4
id = "github.com/user/MyTool"
version = "1.0.0"

[[variants.""]]
side = "server"
platform = "win-x64"
preserve = ["plugins/mytool/config.json"]

[[variants."".assets]]
type = "self"
place = [{ src = "config/", dest = "plugins/mytool/" }]

[[variants."".assets]]
type = "zip"
urls = ["https://github.com/user/MyTool/releases/download/v{{version}}/MyTool-win-x64.zip"]
place = [{ src = "MyTool.exe", dest = "MyTool.exe" }]
```

| 资源类型 | 文件来自 |
| --- | --- |
| `self` | 包自身的归档：标签处的仓库。 |
| `zip` | lip 从 `urls` 下载的 ZIP 文件。 |

| `place` 的形式 | `src` | 作用 |
| --- | --- | --- |
| 文件 | `MyTool.exe` | 把这个文件写到 `dest`。 |
| 目录 | `config/`（以 `/` 结尾） | 把它之下的每个文件写到 `dest` 之下。 |
| glob | `plugins/*.dll` | 把每个匹配的文件写到 `dest` 之下。 |

一个条目可以混用多种资源：模组可以在 `mcmod` 资源之外再带一个 `zip` 资源，用来放置 `mods/` 之外的文件。

## 依赖

`deps` 把每个依赖的包 ID 映射到一个版本范围。lip 会连同你的包一起安装这些依赖；如果一组包的范围无法同时满足，lip 会拒绝安装。

```toml
[variants."".deps]
"github.com/LiteLDev/LeviLamina" = ">=1.0.0 <2.0.0"
"github.com/user/MyLib" = "^0.3.0"
"github.com/LiteLDev/LegacyScriptEngine#lua" = "^0.9.0"
```

- 键是 `<包 ID>`；依赖某个包的某个变体时，写 `<包 ID>#<变体>`。
- 范围的写法与 [Semver for .NET](https://github.com/WalkerCodeRanger/semver/tree/v3.0.0) 相同：`^1.2.0`、`~1.2.0`、`>=1.0.0 <2.0.0`、`1.2.*`。单独写 `1.2.3` 表示恰好这个版本。
- 模组应当依赖 `github.com/LiteLDev/LeviLamina`，并写明它能配合的版本范围。lip 就是靠这个知道你的模组支持哪些 LeviLamina，进而支持哪些游戏版本。范围要如实填写：如果服务器的 LeviLamina 不在范围内，lip 会拒绝把模组装进去。

## 变体

用变体可以从一个仓库提供多个包。用户用 `#<variant>` 选择其中一个，例如 `lip install github.com/user/MyEngine#lua`；不写 `#` 时得到默认变体 `""`。

```toml
[[variants.lua]]
side = "server"
platform = "win-x64"
assets = [{ type = "self", place = [{ src = "lua/", dest = "plugins/myengine/lua/" }] }]

[[variants.quickjs]]
side = "server"
platform = "win-x64"
assets = [{ type = "self", place = [{ src = "quickjs/", dest = "plugins/myengine/quickjs/" }] }]
```

变体名由小写字母、数字和 `_` 组成，中间最多有一个 `/`。每个变体作为一个独立的包 `<包 ID>#<变体>` 安装，所以多个变体可以同时安装。其他包用键 `<包 ID>#<变体>` 来依赖某个变体。

## 运行端和平台

一个条目对应一个运行端和一个平台。lip 自己只使用 `server` 和 `win-x64`；其他启动器（例如客户端的 LeviLauncher）会读取其他条目。你支持的每个运行端和平台各加一个条目，每个条目有自己的资源：

```toml
[[variants.""]]
side = "server"
platform = "win-x64"
assets = [{ type = "mcmod", urls = ["https://github.com/user/MyMod/releases/download/v{{version}}/MyMod-server-win-x64.zip"] }]

[[variants.""]]
side = "client"
platform = "win-x64"
assets = [{ type = "mcmod", urls = ["https://github.com/user/MyMod/releases/download/v{{version}}/MyMod-client-win-x64.zip"] }]
```

同一个变体的两个条目不能有相同的运行端和平台。

## 钩子

`hooks` 是 lip 在安装和卸载前后、在服务器目录中运行的命令：

```toml
[variants."".hooks]
post_install = ["echo MyTool installed"]
pre_uninstall = ["MyTool.exe --unregister"]
```

| 钩子 | 运行时机 |
| --- | --- |
| `pre_install` | 写入文件之前。 |
| `post_install` | 写入文件之后。 |
| `pre_uninstall` | 删除文件之前。 |
| `post_uninstall` | 删除文件之后。 |

使用之前需要知道：

- 命令由 `cmd` 运行，在 Linux 和 macOS 上也是如此，那里的 `cmd` 运行在 Wine 中。请写 Windows 命令，并避免在其中使用 `"`：在 Wine 下，含有 `"` 的命令无法按原样到达 `cmd`。
- 以非 0 退出码退出的命令会让整个操作失败。
- 操作失败时，lip 会撤销它对文件的更改，但不会撤销你的命令做过的事。请写可以安全地运行两次的命令。
- 更新会先运行旧版本的卸载钩子，再运行新版本的安装钩子。

`preserve` 和 `purge` 够用的时候，优先用它们而不是钩子：`purge` 列出卸载时要额外删除的路径，例如你的模组写出的日志。

## 本地测试

发布之前，请在真实的服务器中测试你的包。

::: tip
`mcmod` 和 `zip` 资源始终从它的 `urls` 下载，即使包来自一个目录也是如此。要在发布存在之前测试，请先把归档上传到某个能通过 HTTPS 访问的地方，或者在开发期间使用 `self` 资源。
:::

在一个服务器的目录中：

1. 检查 lip 能否读取你的 `lip.toml`。它会以 JSON 输出包文件（模板已填好），或者说明哪里有问题：

   ```shell
   lip pkg view ../MyMod
   ```

2. 看看安装会做什么，而不做任何更改：

   ```shell
   lip install ../MyMod --dry-run
   ```

3. 从目录安装它：

   ```shell
   lip install ../MyMod
   lip list
   ```

4. 用 `lip run` 启动服务器，试用模组。
5. 修改之后，重新读取这个目录，或者移除这个包：

   ```shell
   lip update ../MyMod
   lip uninstall ../MyMod
   ```

`lip update` 只在 `version` 变了的时候才替换已安装的包。测试期间，请提高 `version`，或者卸载后重新安装。

要测试 ZIP 文件的使用者实际得到的内容，请打包这个包并安装归档：

```shell
lip pkg pack ../MyMod -o ../MyMod.zip
lip install ../MyMod.zip
```

界面的[工具](../interface/tools.md)页做同样的事：**打包 ZIP…** 就是 `lip pkg pack`，**查看包信息…** 就是 `lip pkg view`。

## 发布版本

包的一个版本就是一个名为 `v` 加版本号的 git 标签：

1. 把 `lip.toml` 中的 `version` 设为新版本，例如 `1.0.0`，然后提交。
2. 创建 `urls` 指向的发布资源，例如 `MyMod-server-win-x64.zip`。
3. 给这个提交打标签，并推送标签：

   ```shell
   git tag v1.0.0
   git push origin v1.0.0
   ```

4. 在任意目录中检查 lip 能否看到这个版本：

   ```shell
   lip pkg view github.com/user/MyMod
   ```

   `versions` 中列出了 `1.0.0`，`manifest` 就是你打标签的那个文件。

lip 检查的规则：

- `lip.toml` 中的 `id` 必须是仓库的路径。否则这个提交不是包。
- 在标签 `v1.0.0` 处，`version` 必须是 `1.0.0`。否则 lip 跳过这个标签。
- 标签必须是 `v` 加完整的 [SemVer](https://semver.org/) 版本。`v1.0` 不是版本。`v1.1.0-beta.1` 是预发布版本。
- 仓库必须能在不登录的情况下通过 `https://<包 ID>.git` 访问。

之后用户就可以用 `lip install github.com/user/MyMod` 安装它。不发布版本时，他们仍然可以安装某个分支或提交：`lip install github.com/user/MyMod@branch:main`。

## 上架

界面的**获取模组**页和 [levimc.org/mods](https://levimc.org/mods/) 显示的是 LeviMC 目录中的模组。这个目录由 levimc.org 维护，而不是 lip：包怎样进入目录，请查看 [levimc.org](https://levimc.org/)。

不在目录中的包同样可以通过它的 ID 安装。

`info.tags` 帮助人们在这类列表中找到你的包；lip 自己忽略标签。列表会优先显示下面这些标签，配上翻译后的名称，并允许用户按它们筛选。请按这里的写法填写 ID，使用小写；还可以再加上你喜欢的其他标签：

```toml
[info]
tags = ["management", "economy"]
```

### 推荐标签

| 标签 | 名称 | 适用于 |
| --- | --- | --- |
| `library` | 前置库 / API | 其他模组依赖的库或 API，例如事件、表单或导出函数。 |
| `utility` | 实用工具 | 玩家日常使用的工具，例如背包辅助、信息显示、物品提示和配方查看。 |
| `management` | 服务器管理 | 运营服务器，例如备份、封禁、白名单、登录、命令和领地。 |
| `game-mechanics` | 游戏机制 | 改变游戏玩法，例如原版增强功能和成就。 |
| `adventure` | 内容与冒险 | 可供探索的新内容，例如世界生成、地形和生物群系。 |
| `visual` | 视觉与界面 | 玩家看到的内容，例如 HUD、侧边栏、光影、回放和录制。 |
| `economy` | 经济 | 货币、商店和交易。 |
| `teleport` | 传送 | 传送命令、家园、传送点和传送石碑。 |
| `anticheat` | 反作弊 | 检测和阻止作弊，例如反矿透。 |
| `optimization` | 性能优化 | 提升性能，例如实体和掉落物清理。 |
| `bugfix` | 错误修复 | 修复游戏的错误。 |
| `technology` | 技术与红石 | 红石、活塞和自动化。 |
| `protocol` | 协议与多版本 | 网络协议，例如让多个游戏版本的客户端加入。 |

不需要用标签标明运行端或平台，也不需要标明这个包是 LeviLamina 模组：条目和 `deps` 已经说明了这些。

## lip v0.x 的包

lip v0.x 的包用 `tooth.json` 而不是 `lip.toml`。lip 仍然能安装它们，但不再创建它们。要转换这样的包，参见[迁移包](migrating_packages.md)。

## 另请参阅

- [lip.toml](../concepts/lip_toml.md)：每一个字段。
- [`lip pkg init`](../reference/commands/pkg_init.md)、[`lip pkg pack`](../reference/commands/pkg_pack.md)、[`lip pkg view`](../reference/commands/pkg_view.md)
- [工具](../interface/tools.md)：界面中同样的操作。
- [迁移包](migrating_packages.md)
