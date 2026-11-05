# 创建包

本指南将带你使用 lip 创建新包。包是根部带有 `tooth.json` 的归档，通常是一个 git 仓库；各字段详见 [tooth.json](../concepts/tooth_json.md)。

## 使用 `lip init`

开始新包最简单的方式是在仓库根目录中使用 `lip init`。

```sh
lip init
```

lip 会询问包的 tooth 路径，即 [Go 模块路径](https://go.dev/ref/mod#module-path)，例如 `github.com/user/MyTool`，直到回答合法为止。随后它在当前目录创建 `tooth.json`：

```json
{
  "format_version": 4,
  "format_uuid": "f74a5fda-cb2a-4de8-959f-a1a76bad3102",
  "tooth": "github.com/user/MyTool",
  "version": "0.1.0",
  "variants": {
    "": [
      {
        "platform": "server",
        "runtime": "win-x64"
      }
    ]
  }
}
```

若该目录已存在 `tooth.json`，或无法询问（标准输入或标准错误不是终端），命令会失败。

此时这个包还不会安装任何东西：请按下文为其条目添加 `dependencies`、`assets` 等字段。

## 发布版本

像 `github.com/user/MyTool` 这样的 tooth 路径即 git 仓库 `https://github.com/user/MyTool.git`，该仓库中 `tooth.json` 的 `tooth` 必须恰好是这个路径。要发布一个版本，请推送名为 `v` 加 [SemVer 2.0.0](https://semver.org/) 版本号的标签，例如 `v1.0.0`。标签处 `tooth.json` 中的 `version` 必须是同一版本。不严格符合 `v<SemVer>` 的标签（例如 `v1.0`）不是版本。

## 文件包

文件包在工作区中放置文件。每个资源都是一个取文件的归档：`self` 是包自身的归档（标签处的仓库），`zip` 是 lip 下载的 ZIP 文件。其 `place` 复制一个文件、一个目录或与 glob 匹配的文件：

```json
{
  "format_version": 4,
  "format_uuid": "f74a5fda-cb2a-4de8-959f-a1a76bad3102",
  "tooth": "github.com/user/MyTool",
  "version": "1.0.0",
  "variants": {
    "": [
      {
        "platform": "server",
        "runtime": "win-x64",
        "assets": [
          {
            "type": "self",
            "place": [{ "src": "config/", "dest": "config/mytool/" }]
          },
          {
            "type": "zip",
            "urls": ["https://github.com/user/MyTool/releases/download/v1.0.0/MyTool-win-x64.zip"],
            "place": [{ "src": "MyTool.exe", "dest": "MyTool.exe" }]
          }
        ],
        "preserve": ["config/mytool/"]
      }
    ]
  }
}
```

`preserve` 保留用户修改过的配置：已存在的文件不会被覆盖，卸载时也不会被删除。其 glob 相对于工作区。

## mod

mod 是带有 `mcmod` 资源的条目：一个根部带有该 mod 的 LeviLamina `manifest.json` 的 ZIP 归档。lip 把整个归档写入 `mods/<name>/`，其中 `<name>` 是该 `manifest.json` 中的 `name`；lip 不读取 `manifest.json` 的其他内容，并原样安装它。在服务端，`plugins` 是指向 `mods` 的链接，因此 LeviLamina 也能在 `plugins/<name>/` 下找到该 mod。

用 [`lip pack`](../cli/commands/pack.md) 从构建输出目录（其中有 `manifest.json` 和 mod 的文件）构建该归档，并把它附加到 Release：

```sh
lip pack build/ -o MyMod-server-win-x64.zip
```

然后在 `tooth.json` 中指向它：

```json
{
  "format_version": 4,
  "format_uuid": "f74a5fda-cb2a-4de8-959f-a1a76bad3102",
  "tooth": "github.com/user/MyMod",
  "version": "1.0.0",
  "info": { "name": "My Mod" },
  "variants": {
    "": [
      {
        "platform": "server",
        "runtime": "win-x64",
        "dependencies": {
          "github.com/LiteLDev/LeviLamina": ">=1.0.0 <2.0.0",
          "github.com/user/MyLib": "^0.3.0"
        },
        "assets": [
          {
            "type": "mcmod",
            "urls": ["https://github.com/user/MyMod/releases/download/v1.0.0/MyMod-server-win-x64.zip"]
          }
        ],
        "preserve": ["mods/MyMod/config/"]
      }
    ]
  }
}
```

`dependencies` 把每个依赖的 tooth 路径映射到版本范围，例如 `^0.3.0` 或 `>=1.0.0 <2.0.0`，语法见 [Semver for .NET](https://github.com/WalkerCodeRanger/semver/tree/v3.0.0)。lip 会安装这些依赖。它只读取 mod 的 LeviLamina `manifest.json` 中的 `name`，并原样安装该文件，因此请按 LeviLamina 的要求，自己在其 `dependencies` 中列出你的 mod 所依赖的 mod。

在客户端上，mod 同样安装到 `mods/<name>/`，由 LeviLauncher 等启动器负责；lip 本身只安装到服务端。

## 多个平台和运行时

一个条目恰好适用于一个平台和一个运行时，且必须与工作区的平台和运行时相同。请为包支持的每个平台和运行时各添加一个条目，每个条目有自己的资源：

```json
{
  "variants": {
    "": [
      {
        "platform": "server",
        "runtime": "win-x64",
        "assets": [{ "type": "mcmod", "urls": ["https://github.com/user/MyMod/releases/download/v1.0.0/MyMod-server-win-x64.zip"] }]
      },
      {
        "platform": "client",
        "runtime": "win-x64",
        "assets": [{ "type": "mcmod", "urls": ["https://github.com/user/MyMod/releases/download/v1.0.0/MyMod-client-win-x64.zip"] }]
      },
      {
        "platform": "client",
        "runtime": "win-arm64",
        "assets": [{ "type": "mcmod", "urls": ["https://github.com/user/MyMod/releases/download/v1.0.0/MyMod-client-win-arm64.zip"] }]
      }
    ]
  }
}
```

平台和运行时没有对应条目的工作区无法安装该包。

## 变体

使用变体可在一个仓库中提供多个包。用户通过 `path#variant` 选择其中之一，例如 `lip install github.com/user/repo#lua`；不带 `#` 时使用变体 `""`：

```json
{
  "variants": {
    "lua": [
      {
        "platform": "server",
        "runtime": "win-x64",
        "assets": [{ "type": "self", "place": [{ "src": "lua/", "dest": "myengine/lua/" }] }]
      }
    ],
    "quickjs": [
      {
        "platform": "server",
        "runtime": "win-x64",
        "assets": [{ "type": "self", "place": [{ "src": "quickjs/", "dest": "myengine/quickjs/" }] }]
      }
    ]
  }
}
```

每个变体都作为独立的包安装，身份为 `<tooth 路径>#<variant>`，因此同一仓库的多个变体可以并存。其他包在 `dependencies` 中以键 `<tooth 路径>#<variant>` 依赖某个变体。

## 本地测试

发布之前，你可以从磁盘安装包：从其目录，或从根部带有 `tooth.json` 的 ZIP 文件安装：

```sh
lip install ../MyTool
lip pack ../MyTool -o MyTool.zip
lip install ./MyTool.zip
```

此时 `self` 资源从该目录或 ZIP 文件中取文件。

## 已有的 `tooth.json` 包

lip 仍会安装 `tooth.json` 格式版本为 1、2 或 3 的包，但不再创建它们。如需转换，请参阅[迁移包](migrating_packages.md)。
