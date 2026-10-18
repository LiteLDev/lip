# 安装

`lip` 是一个通用包安装器。开始使用前，你需要先在系统中安装 `lip` 可执行文件。

## npm

所有平台的默认安装方式是 npm：

```shell
npm install -g @futrime/lip
```

该方式会同时安装 `lip` 和 `lipd`，适用于 Windows、Linux 与 macOS。

如果你只想运行 `lip` 而不全局安装，也可以使用 `npx`：

```shell
npx @futrime/lip --help
```

这适合试用 `lip` 或执行一次性命令。

## winget

```shell
winget install futrime.lip
```

## Windows 安装包

从 [Releases 页面](https://github.com/LiteLDev/lip/releases/latest) 下载最新的 `lip-<version>-<runtime>-setup.exe` 并运行。
