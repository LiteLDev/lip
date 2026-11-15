# lip uninstall

卸载包。

## 概要

```shell
lip uninstall <PACKAGES> [options]
```

## 参数

| 参数 | 说明 |
| --- | --- |
| `<PACKAGES>` | 要卸载的包，可以是身份（例如 `github.com/LiteLDev/LegacyScriptEngine#lua`），也可以是安装时使用的来源（例如 `github.com/user/repo@branch:main` 或 `./package.zip`）。不带变体的来源匹配从它安装的所有变体，不带 git 引用的来源匹配所有 git 引用。 |

身份可通过 [`lip list`](list.md) 查看。

## 说明

删除包拥有的文件，并执行其卸载钩子。只能卸载显式安装的包。

不再被任何显式安装的包需要的依赖也会被卸载。仍被其他包需要的包会保留，并改为依赖。

[`lip setup`](setup.md) 安装的游戏和加载器与其他包一样卸载，例如 `lip uninstall github.com/LiteLDev/LeviLamina`。游戏被卸载后，工作区就没有游戏了：在安装 mod 或运行服务器之前，请再用 `lip setup` 设置它。

## 选项

| 选项 | 说明 |
| --- | --- |
| `-n, --dry-run` | 仅模拟执行，不做实际更改。 |
