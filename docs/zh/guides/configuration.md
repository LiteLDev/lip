# 配置

读完本页，你就能用 `lip config` 查看和修改 lip 的设置，并且知道在 GitHub 很慢或被屏蔽的地方该怎么办。在界面里，同样的设置在[设置](../interface/settings.md)页面。

## 两组设置

lip 的设置不多，分成两组，称为作用域：

| 作用域 | 适用于 | 键 | 命令 |
| --- | --- | --- | --- |
| **用户** | 你本人，在每个工作区里 | `github_proxy`、`language` | `lip config …` |
| **工作区** | 某一个服务器文件夹 | `restart` | `lip config … --workspace` |

带 `--workspace` 的命令作用于你当前所在的文件夹。不带它的命令作用于你的用户设置，与你在哪个文件夹无关。

## 查看和修改设置

[`lip config`](../reference/commands/config.md) 有四条命令。

**查看某个作用域的全部设置：**

```sh
lip config list
```

```text
┌──────────────┬───────────┐
│ Key          │ Value     │
├──────────────┼───────────┤
│ github_proxy │ (not set) │
│ language     │ (not set) │
└──────────────┴───────────┘
```

```sh
lip config list --workspace
```

```text
┌─────────┬───────┐
│ Key     │ Value │
├─────────┼───────┤
│ restart │ never │
└─────────┴───────┘
```

`(not set)` 表示这个键没有值。`(empty)` 表示它被设成了空值，对 `github_proxy` 来说这是一种单独的选择（见下文）。

**查看一项设置：**

```sh
lip config get language
```

**修改一项设置：**

```sh
lip config set language zh-Hans
```

```text
✓ Config key 'language' set to 'zh-Hans'.
```

键不允许的值，lip 会拒绝，并且什么都不改。

**删除一项设置**，让它回到默认值：

```sh
lip config delete language
```

```text
✓ Config key 'language' deleted.
```

`lip config get` 和 `lip config list` 可以加 `--json` 供脚本使用。键用错了作用域会失败，错误信息会告诉你该加上还是去掉 `--workspace`。

## 各个键

两个配置文件的完整说明见[配置文件](../reference/configuration.md)。

### github_proxy

作用域：用户。值：一个 URL，或空。默认：未设置。

lip 如何连接 GitHub。大多数包都是从那里下载的。

| 值 | lip 的做法 |
| --- | --- |
| 未设置 | 直接连接 GitHub。界面连不上 GitHub 时，可能会请你做出选择。 |
| 空 | 直接连接 GitHub，界面也不再询问。 |
| 一个 URL | 发往 GitHub 的请求先走这个地址，失败后再直接连接。 |

见 [GitHub 很慢或被屏蔽时](#github-很慢或被屏蔽时)。

### language

作用域：用户。值：`en`、`zh-Hans` 或 `zh-Hant`。默认：未设置。

界面的语言：英语、简体中文或繁体中文。没有值时，界面跟随系统语言；系统语言既不是英语也不是中文时，使用英语。

```sh
lip config set language zh-Hant
```

这只改变界面。命令在终端里输出的内容始终是英语。

### restart

作用域：工作区。值：`never` 或 `on-failure`。默认：`never`。

这个工作区的服务器崩溃（也就是以非 0 的退出码退出）时，lip 是否重新启动它。

```sh
lip config set restart on-failure --workspace
```

设为 `on-failure` 后，lip 会等 5 秒再启动服务器，10 分钟内最多 5 次。见[崩溃后自动重启](server_from_commands.md#崩溃后自动重启)。

## GitHub 很慢或被屏蔽时

lip 的大多数包都从 GitHub 下载。在 GitHub 很慢或连不上的地方，安装会失败，并提示：

```text
hint: lip could not reach the server. Check your network connection. If GitHub is slow or blocked where you are, set a GitHub proxy with 'lip config set github_proxy <URL>'.
```

### 什么是 GitHub 代理

GitHub 代理是一个替你从 GitHub 取东西的网络服务。你把它的地址告诉 lip，lip 就会把这个地址放在它要向 GitHub 请求的内容前面。

把 `github_proxy` 设为 `https://proxy.example.com/` 之后，对

```text
https://github.com/LiteLDev/LeviLamina.git/info/refs
```

的请求会发往

```text
https://proxy.example.com/LiteLDev/LeviLamina.git/info/refs
```

所以，你设置的地址必须满足：在它后面加上 `<所有者>/<仓库>/…`，得到的就是 GitHub 上对应的内容。有些服务要求在自己的地址后面跟完整的 GitHub 地址。对这类服务，把它一并写进设置里，例如 `https://proxy.example.com/https://github.com/`。

需要知道三点：

- **只有 GitHub 走代理。** 发往 `github.com` 的请求，无论是仓库还是文件，都使用代理。从其他网站下载的内容，例如从 minecraft.net 下载的游戏，不走代理。
- **lip 会回退。** 通过代理的请求失败时，lip 会直接向 GitHub 发送请求。
- **lip 不推荐任何代理。** 代理能看到你通过它下载的所有内容，也有可能篡改它。请使用你自己或你信任的人运行的代理。

### 设置代理

```sh
lip config set github_proxy https://proxy.example.com/
```

把地址换成你的代理地址。此后每条命令都会使用这项设置。

### 测试代理

lip 检查能否连上 GitHub 的方法，是请求 LeviLamina 的版本列表。你可以通过代理请求同样的内容。可用的代理会返回 `200`：

::: code-group

```powershell [Windows]
curl.exe -s -o NUL -w "%{http_code}" "https://proxy.example.com/LiteLDev/LeviLamina.git/info/refs?service=git-upload-pack"
```

```sh [Linux / macOS]
curl -s -o /dev/null -w "%{http_code}\n" "https://proxy.example.com/LiteLDev/LeviLamina.git/info/refs?service=git-upload-pack"
```

:::

界面会替你做这项测试：你在设置页面输入代理后，它会先测试、再保存。见[设置](../interface/settings.md)。

命令能成功，并不说明代理可用，因为 lip 会回退到直接连接 GitHub。如果设置了代理后安装仍然很慢，请测试一下代理。

### 移除代理

不再使用代理，并让界面在连不上 GitHub 时重新询问：

```sh
lip config delete github_proxy
```

不再使用代理，并告诉 lip 你就是直接连接，这样界面不会再询问。做法是把它设成空值：

::: code-group

```powershell [Windows]
lip config set github_proxy --% ""
```

```sh [Linux / macOS]
lip config set github_proxy ""
```

:::

在 PowerShell 里需要 `--%`：不加的话，Windows PowerShell 会把空值丢掉。在界面里，可以在设置页面为 GitHub 代理选择“直接连接”。

### 系统代理

`github_proxy` 不是普通的 HTTP 代理。如果你的网络有普通的 HTTP 代理，lip 不需要专门的设置就会使用它：

- lip 会遵循你运行它的那个终端里的环境变量 `HTTPS_PROXY`、`HTTP_PROXY`、`ALL_PROXY` 和 `NO_PROXY`（或同名的小写变量）。
- 没有这些变量时，它使用系统设置的代理。

::: code-group

```powershell [Windows]
$env:HTTPS_PROXY = "http://proxy.example.com:8080"
lip install LegacyScriptEngine#lua
```

```sh [Linux / macOS]
HTTPS_PROXY=http://proxy.example.com:8080 lip install LegacyScriptEngine#lua
```

:::

把地址换成你的代理地址。这样的代理对所有下载都生效，不只是 GitHub。

## 设置保存在哪里

| 作用域 | 文件 |
| --- | --- |
| 用户，Windows | `%APPDATA%\lip\config.toml` |
| 用户，Linux 和 macOS | `$XDG_CONFIG_HOME/lip/config.toml`，默认是 `~/.config/lip/config.toml` |
| 工作区 | 服务器文件夹里的 `.lip/config.toml` |

两者都是很小的文本文件，可以手动编辑。文件不存在或读不出来时，视为没有任何设置。格式见[配置文件](../reference/configuration.md)，lip 用到的每个文件都列在[文件和目录](../reference/files.md)里。

移动或复制服务器文件夹时，工作区设置会跟着走。用户设置不会。
