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
  "format": "bf45b1",
  "id": "MyMod",
  "version": "0.1.0",
  "dependencies": {},
  "mod": { "loader": "native", "entry": "MyMod.dll" }
}
```

它既是 LeviLamina 的 mod 清单，也是 lip 包。请按你的 mod 修改 `id` 和 `mod`。

## 发布版本

像 `github.com/user/MyMod` 这样的包路径即 git 仓库 `https://github.com/user/MyMod.git`。要发布一个版本，请推送名为 `v` 加 [SemVer 2.0.0](https://semver.org/) 版本号的标签，例如 `v1.0.0`。该标签处 `manifest.json` 中的 `version` 必须是同一版本。名称不完全是 `v<SemVer>` 的标签（例如 `v1.0`）会被忽略。

## mod

mod 是带有 `mod` 键的包。无论在服务端（由 lip 安装）还是客户端（由 LeviLauncher 等启动器安装），整个归档都会写入 `mods/<id>/`。服务端的 `plugins` 是指向 `mods` 的链接，因此 LeviLamina 也能在 `plugins/<id>/` 下找到该 mod。

`dependencies` 把每个依赖的 id 映射到一个版本范围，或一个带 `version` 和 `source` 的对象。带 `source` 的依赖由 lip 安装；LeviLamina 读取全部依赖，并要求每个依赖都是已加载的 mod。对于不是由 LeviLamina 加载的 mod 的依赖（例如文件包），请加上 `"mod": false`：

```json
{
  "format": "bf45b1",
  "id": "LegacyMoney",
  "version": "0.22.0",
  "platform": "server",
  "runtime": "win-x64",
  "info": { "name": "Legacy Money", "authors": ["LiteLDev"] },
  "dependencies": {
    "LeviLamina": { "version": ">=26.51.0 <26.52.0", "source": "github.com/LiteLDev/LeviLamina" },
    "iListenAttentively": { "version": "^0.17.0", "source": "github.com/MiracleForest/iListenAttentively" }
  },
  "mod": { "loader": "native", "entry": "LegacyMoney.dll" },
  "preserve": ["config/", "data/"]
}
```

版本范围使用 [node-semver](https://github.com/npm/node-semver#ranges) 的语法，例如 `^0.17.0` 或 `>=26.51.0 <26.52.0`。不带运算符的版本号（例如 `1.2.3`）只匹配该版本。

## 文件包

文件包（不带 `mod`）通过 `files` 把文件放到工作区中。每一项复制一个文件、一个目录或匹配某个 glob 的文件，来源可以是包本身，也可以是 `archives` 中列出、由 lip 下载的归档：

```json
{
  "format": "bf45b1",
  "id": "PeEditor",
  "version": "3.9.0",
  "runtime": "win-x64",
  "files": [{ "src": "PeEditor.exe", "dest": "PeEditor.exe" }]
}
```

## 打包 `.mcmod`

`.mcmod` 文件是一个 ZIP 归档，根部是 `manifest.json`，旁边是包的文件。[`lip pack`](../cli/commands/pack.md) 可以用当前目录中的 `manifest.json` 和某个目录（例如构建的输出目录）中的文件生成它：

```sh
lip pack build/ -o LegacyMoney-server-win-x64.mcmod
```

发布前可以先测试：

```sh
lip install ./LegacyMoney-server-win-x64.mcmod
```

## 指向发布资源

构建好的 mod 通常不放在仓库里。此时仓库在每个标签处的 `manifest.json` 是一个元文档，其 `targets` 指向附加在 release 上的 `.mcmod` 文件，每个平台和 RID 一个。它的其余键是这些包的模板：

```json
{
  "format": "bf45b1",
  "id": "LegacyMoney",
  "version": "0.22.0",
  "info": { "name": "Legacy Money", "authors": ["LiteLDev"] },
  "dependencies": {
    "LeviLamina": { "version": ">=26.51.0 <26.52.0", "source": "github.com/LiteLDev/LeviLamina" },
    "iListenAttentively": { "version": "^0.17.0", "source": "github.com/MiracleForest/iListenAttentively" }
  },
  "mod": { "loader": "native", "entry": "LegacyMoney.dll" },
  "preserve": ["config/", "data/"],
  "targets": [
    {
      "platform": ["server", "client"],
      "runtime": "win-x64",
      "source": "https://github.com/LiteLDev/LegacyMoney/releases/download/v{{version}}/{{id}}-{{platform}}-{{runtime}}.mcmod"
    }
  ]
}
```

在 `win-x64` 服务端上，`lip install github.com/LiteLDev/LegacyMoney` 会读取最新标签处的这个文档，并安装 `LegacyMoney-server-win-x64.mcmod`。

要为每个平台和 RID 生成 `.mcmod`，请在仓库中运行 `lip pack`：它用选中的 target 把模板补全为包文档，并与你的文件一起打包。target 还可以覆盖模板中的键，或用 `null` 删除它们：

```sh
lip pack build/ --platform server --rid win-x64 -o LegacyMoney-server-win-x64.mcmod
lip pack build/ --platform client --rid win-x64 -o LegacyMoney-client-win-x64.mcmod
```

## 变体

使用变体可以在一个仓库中提供多个包。用户通过 `path#variant` 选择，例如 `lip install github.com/user/repo#lua`：

```json
{
  "format": "bf45b1",
  "targets": [
    { "variant": "quickjs", "source": "./quickjs/" },
    { "variant": "lua", "source": "./lua/" }
  ]
}
```

`./quickjs/` 和 `./lua/` 都是仓库中的目录，各自带有包文档 `manifest.json`。

## 已有的 `tooth.json` 包

lip 仍可安装使用 `tooth.json` 的包，但不再创建它们。如需转换，请参见[迁移包](migrating_packages.md)。
