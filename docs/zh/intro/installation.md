# 安装

`lip` 是一个通用包安装器。开始使用前，你需要先在系统中安装 `lip` 可执行文件。

## npm

所有平台的默认安装方式是 npm：

```shell
npm install -g @levimc/lip
```

该方式会同时安装 `lip` 和 `lipd`，适用于 Windows、Linux 与 macOS。

如果你只想运行 `lip` 而不全局安装，也可以使用 `npx`：

```shell
npx @levimc/lip --help
```

这适合试用 `lip` 或执行一次性命令。

::: tip
该包此前以 `@futrime/lip` 名称发布。如果你使用旧名称安装过，请先执行 `npm uninstall -g @futrime/lip` 卸载。
:::

## winget <Badge platform="windows" />

```shell
winget install levimc.lip
```

::: tip
该包此前以 `futrime.lip` ID 发布。如果你使用旧 ID 安装过，请先执行 `winget uninstall futrime.lip` 卸载。
:::

## Windows 安装包 <Badge platform="windows" />

从 [Releases 页面](https://github.com/LiteLDev/lip/releases/latest) 下载最新的 `lip-<version>-<runtime>-setup.exe` 并运行。
