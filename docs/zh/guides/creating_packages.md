# 创建包

本指南将带你使用 lip 创建新包。包是位于归档根部的 `manifest.json`；各字段详见[包清单](../concepts/package_manifest.md)。

## 使用 `lip init`

开始新项目最简单的方式是使用 `lip init`。

```sh
lip init
```

该命令会在当前目录创建 `manifest.json`。若该目录已存在 `manifest.json`，命令会失败。

对于名为 `MyMod` 的目录，生成的 `manifest.json` 如下：

```json
{
  "formatVersion": 4,
  "formatUuid": "0c6b977a-64ad-441f-9a29-c7f4b196882f",
  "name": "MyMod",
  "entry": "MyMod.dll",
  "type": "native",
  "version": "0.1.0",
  "dependencies": []
}
```

它既是 LeviLamina 的 mod 清单，也是 lip 包。请按你的 mod 修改 `name`、`entry` 和 `type`。

## 发布版本

像 `github.com/user/MyMod` 这样的包路径即 git 仓库 `https://github.com/user/MyMod.git`。要发布一个版本，请推送名为 `v` 加 [SemVer 2.0.0](https://semver.org/) 版本号的标签，例如 `v1.0.0`。该标签处 `manifest.json` 中的 `version` 必须是同一版本。名称不完全是 `v<SemVer>` 的标签（例如 `v1.0`）会被忽略。

## mod

mod 是 `type` 不为 `"files"` 的具体包。无论在服务端（由 lip 安装）还是客户端（由 LeviLauncher 等启动器安装），整个归档都会写入 `mods/<name>/`。服务端的 `plugins` 是指向 `mods` 的链接，因此 LeviLamina 也能在 `plugins/<name>/` 下找到该 mod。

带 `refs` 的依赖由 lip 安装；LeviLamina 仍照常读取 `name` 和 `version`。只应由 lip 安装的包（例如 LeviLamina 本身）请写在 `installDependencies` 中：

```json
{
  "formatVersion": 4,
  "formatUuid": "0c6b977a-64ad-441f-9a29-c7f4b196882f",
  "name": "LegacyMoney",
  "entry": "LegacyMoney.dll",
  "type": "native",
  "version": "0.22.0",
  "platform": "server",
  "rid": "win-x64",
  "refs": ["github.com/LiteLDev/LegacyMoney"],
  "dependencies": [
    {
      "name": "iListenAttentively",
      "version": "^0.17.0",
      "refs": ["github.com/MiracleForest/iListenAttentively"]
    }
  ],
  "installDependencies": [
    { "version": ">=26.51.0 <26.52.0", "refs": ["github.com/LiteLDev/LeviLamina"] }
  ]
}
```

版本范围使用 LeviLamina 的语法。不带运算符的版本号（例如 `1.2.3`）会被拒绝，请写成 `=1.2.3`。

## 文件包

文件包（`"type": "files"`）通过 `placements` 把文件放到工作区中：

```json
{
  "formatVersion": 4,
  "formatUuid": "0c6b977a-64ad-441f-9a29-c7f4b196882f",
  "name": "PeEditor",
  "type": "files",
  "version": "3.9.0",
  "rid": "win-x64",
  "placements": [{ "src": "PeEditor.exe", "dest": "PeEditor.exe" }]
}
```

## 打包 `.mcmod`

`.mcmod` 文件是一个 ZIP 归档，根部是 `manifest.json`，旁边是包的文件。发布前可以先测试：

```sh
lip install ./LegacyMoney-server-win-x64.mcmod
```

## 路由到发布资源

构建好的 mod 通常不放在仓库里。此时仓库在每个标签处的 `manifest.json` 是一个路由文档，指向附加在 release 上的 `.mcmod` 文件，每个平台和 RID 一个：

```json
{
  "formatVersion": 4,
  "formatUuid": "0c6b977a-64ad-441f-9a29-c7f4b196882f",
  "name": "LegacyMoney",
  "version": "0.22.0",
  "artifacts": [
    {
      "platform": ["server", "client"],
      "rid": "win-x64",
      "refs": [
        "https://github.com/LiteLDev/LegacyMoney/releases/download/v{{version}}/{{name}}-{{platform}}-{{rid}}.mcmod"
      ]
    }
  ]
}
```

在 `win-x64` 服务端上，`lip install github.com/LiteLDev/LegacyMoney` 会读取最新标签处的这个文档，并安装 `LegacyMoney-server-win-x64.mcmod`。

## 变体

使用变体可以在一个仓库中提供多个包。用户通过 `path#variant` 选择，例如 `lip install github.com/user/repo#lua`：

```json
{
  "formatVersion": 4,
  "formatUuid": "0c6b977a-64ad-441f-9a29-c7f4b196882f",
  "artifacts": [
    { "variant": "quickjs", "refs": ["./quickjs/"] },
    { "variant": "lua", "refs": ["./lua/"] }
  ]
}
```

`./quickjs/` 和 `./lua/` 都是仓库中的目录，各自带有具体的 `manifest.json`。

## 已有的 `tooth.json` 包

lip 仍可安装使用 `tooth.json` 的包，但不再创建它们。如需转换，请参见[迁移包](migrating_packages.md)。
