# 创建包

本指南将带你使用 lip 创建新包。包是根部带有 `lip.toml` 的归档，通常是一个 git 仓库；各字段详见 [lip.toml](../concepts/lip_toml.md)。

## 使用 `lip pkg init`

开始新包最简单的方式是在仓库根目录中使用 `lip pkg init`。

```sh
lip pkg init
```

lip 会询问包 ID，即 [Go 模块路径](https://go.dev/ref/mod#module-path)，例如 `github.com/user/MyTool`，直到回答合法为止。随后它在当前目录创建 `lip.toml`：

```toml
format = 4
id = "github.com/user/MyTool"
version = "0.1.0"

[[variants.""]]
side = "server"
platform = "win-x64"
```

若该目录已存在 `lip.toml`，或无法询问（标准输入或标准错误不是终端），命令会失败。

此时这个包还不会安装任何东西：请按下文为其条目添加 `deps`、`assets` 等字段。

## 发布版本

像 `github.com/user/MyTool` 这样的包 ID 即 git 仓库 `https://github.com/user/MyTool.git`，该仓库中 `lip.toml` 的 `id` 必须恰好是这个路径。要发布一个版本，请推送名为 `v` 加 [SemVer 2.0.0](https://semver.org/) 版本号的标签，例如 `v1.0.0`。标签处 `lip.toml` 中的 `version` 必须是同一版本。不严格符合 `v<SemVer>` 的标签（例如 `v1.0`）不是版本。

## 文件包

文件包在工作区中放置文件。每个资源都是一个取文件的归档：`self` 是包自身的归档（标签处的仓库），`zip` 是 lip 下载的 ZIP 文件。其 `place` 复制一个文件、一个目录或与 glob 匹配的文件：

```toml
format = 4
id = "github.com/user/MyTool"
version = "1.0.0"

[[variants.""]]
side = "server"
platform = "win-x64"
preserve = ["config/mytool/"]

[[variants."".assets]]
type = "self"
place = [{ src = "config/", dest = "config/mytool/" }]

[[variants."".assets]]
type = "zip"
urls = ["https://github.com/user/MyTool/releases/download/v1.0.0/MyTool-win-x64.zip"]
place = [{ src = "MyTool.exe", dest = "MyTool.exe" }]
```

`preserve` 保留用户修改过的配置：已存在的文件不会被覆盖，卸载时也不会被删除。其 glob 相对于工作区。

## mod

mod 是带有 `mcmod` 资源的条目：一个根部带有该 mod 的 LeviLamina `manifest.json` 的 ZIP 归档。lip 把整个归档写入 `mods/<name>/`，其中 `<name>` 是该 `manifest.json` 中的 `name`；lip 不读取 `manifest.json` 的其他内容，并原样安装它。

用 [`lip pkg pack`](../cli/commands/pkg_pack.md) 从构建输出目录（其中有 `manifest.json` 和 mod 的文件）构建该归档，并把它附加到 Release：

```sh
lip pkg pack build/ -o MyMod-server-win-x64.zip
```

然后在 `lip.toml` 中指向它：

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
"github.com/user/MyLib" = "^0.3.0"

[[variants."".assets]]
type = "mcmod"
urls = ["https://github.com/user/MyMod/releases/download/v1.0.0/MyMod-server-win-x64.zip"]
```

`deps` 把每个依赖的包 ID 映射到版本范围，例如 `^0.3.0` 或 `>=1.0.0 <2.0.0`，语法见 [Semver for .NET](https://github.com/WalkerCodeRanger/semver/tree/v3.0.0)。lip 会安装这些依赖。它只读取 mod 的 LeviLamina `manifest.json` 中的 `name`，并原样安装该文件，因此请按 LeviLamina 的要求，自己在其 `deps` 中列出你的 mod 所依赖的 mod。

在客户端上，mod 同样安装到 `mods/<name>/`，由 LeviLauncher 等启动器负责；lip 本身只安装到服务端。

## 多个运行端和平台

一个条目恰好适用于一个运行端（`server` 或 `client`）和一个平台（.NET RID，例如 `win-x64`），且必须与工作区[环境](../concepts/lip_toml.md#环境)的运行端和平台相同。请为包支持的每个运行端和平台各添加一个条目，每个条目有自己的资源：

```toml
[[variants.""]]
side = "server"
platform = "win-x64"
assets = [{ type = "mcmod", urls = ["https://github.com/user/MyMod/releases/download/v1.0.0/MyMod-server-win-x64.zip"] }]

[[variants.""]]
side = "client"
platform = "win-x64"
assets = [{ type = "mcmod", urls = ["https://github.com/user/MyMod/releases/download/v1.0.0/MyMod-client-win-x64.zip"] }]

[[variants.""]]
side = "client"
platform = "win-arm64"
assets = [{ type = "mcmod", urls = ["https://github.com/user/MyMod/releases/download/v1.0.0/MyMod-client-win-arm64.zip"] }]
```

运行端和平台没有对应条目的工作区无法安装该包。除非工作区通过 `--platform` 指定了其他平台，lip 都使用平台 `win-x64`，在 Linux 上也是如此（此时通过 Wine 运行服务器）；因此服务端 mod 需要 `win-x64` 的 `server` 条目，而 `linux-x64` 的条目只用于以 `--platform linux-x64` 创建的工作区。

## 变体

使用变体可在一个仓库中提供多个包。用户通过 `path#variant` 选择其中之一，例如 `lip install github.com/user/repo#lua`；不带 `#` 时使用变体 `""`：

```toml
[[variants.lua]]
side = "server"
platform = "win-x64"
assets = [{ type = "self", place = [{ src = "lua/", dest = "myengine/lua/" }] }]

[[variants.quickjs]]
side = "server"
platform = "win-x64"
assets = [{ type = "self", place = [{ src = "quickjs/", dest = "myengine/quickjs/" }] }]
```

每个变体都作为独立的包安装，身份为 `<包 ID>#<variant>`，因此同一仓库的多个变体可以并存。其他包在 `deps` 中以键 `<包 ID>#<variant>` 依赖某个变体。

## 推荐标签

`info.tags` 供人阅读，也供列出包的网站和启动器使用；lip 本身忽略标签。[levimc.org](https://levimc.org/mods/) 和 LeviLauncher 等模组列表会优先显示下列标签及其本地化名称，并允许用户按它们筛选。请按此处的写法使用这些 ID：小写，且不加 `category:` 之类的前缀；不需要其他拼写或同义词。挑选最能描述你的包的几个，此外也可以添加任何其他标签：

```toml
[info]
tags = ["management", "economy"]
```

| 标签 | 名称 | 适用于 |
| --- | --- | --- |
| `library` | 前置库 / API | 其他 mod 依赖的库或 API，例如事件、表单或导出函数。 |
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

无需为运行端、平台或“这是 LeviLamina mod”添加标签：条目和 `deps` 已经说明了这些，模组列表也会隐藏 `server`、`client`、`levilamina` 和 `mod` 等标签。

## 本地测试

发布之前，你可以从磁盘安装包：从其目录，或从根部带有 `lip.toml` 的 ZIP 文件安装：

```sh
lip install ../MyTool
lip pkg pack ../MyTool -o MyTool.zip
lip install ./MyTool.zip
```

此时 `self` 资源从该目录或 ZIP 文件中取文件。

## lip v0.x 的包

lip 仍会安装 lip v0.x 的包（它们带有格式版本 1、2 或 3 的 `tooth.json`，而不是 `lip.toml`），但不再创建它们。如需转换，请参阅[迁移包](migrating_packages.md)。
