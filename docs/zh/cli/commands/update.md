# lip update

更新包。

## 概要

```shell
lip update [PACKAGES] [options]
```

## 参数

| 参数 | 说明 |
| --- | --- |
| `[PACKAGES]` | 要更新的包。支持与 [`lip install`](./install.md#参数) 相同的形式：不带版本时安装最新版本；带 `@<version>` 时安装恰好该版本；带 git 引用时安装它当前所指的提交。省略时，所有显式安装的包按下文所述更新。 |

## 说明

这些包必须已经安装。更新一个包即先卸载旧版本，再安装新版本；其依赖会按需更新。只有结果与已安装的不同时才会重新安装：版本不同、归档不同，或归档相同但内容不同。

不带参数时，每个显式安装的包都按[锁](../../concepts/lockfiles.md)中记录的安装来源重新请求：

| 安装来源 | `lip update` 安装 |
| --- | --- |
| `<path>` 或 `<path>@<version>` | 最新版本。 |
| `<path>@branch:<branch>` | 分支当前的头。 |
| `<path>@tag:<tag>` | 标签当前所指的提交（只有标签被移动过才会变化）。 |
| `<path>@rev:<commit>` | 同一个提交：不会变化。 |
| `https://` URL | 同一个 URL。下载内容按 URL 缓存，因此使用缓存副本；如需重新下载，请先运行 [`lip cache clean`](cache_clean.md)。 |
| 本地 `.mcmod` 或 `.zip` 文件 | 同一个文件；若其内容已变化，则从它重新安装。若文件已不存在，则保持该包不变，并给出警告。 |

如需把从 git 引用安装的包改回正式发布的版本，请用其不带 git 引用的包路径更新，例如 `lip update github.com/LiteLDev/LeviLamina`。

## 选项

| 选项 | 说明 |
| --- | --- |
| `-n, --dry-run` | 仅模拟执行，不做实际更改。 |
| `--ignore-scripts` | 更新时跳过安装与卸载生命周期脚本。 |
| `--rid <RID>` | 工作区的 .NET RID，例如 `win-x64`。 |
