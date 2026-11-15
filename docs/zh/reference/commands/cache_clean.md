# lip cache clean

删除缓存。

## 概要

```
lip cache clean [-h | --help]
```

## 说明

`lip cache clean` 删除[缓存目录](../files.md#缓存目录)：在 Windows 上是 `%LOCALAPPDATA%\lip\Cache`，在其他系统上是 `$XDG_CACHE_HOME/lip`，默认即 `~/.cache/lip`。

缓存里只有 lip 下载过、并且可以重新下载的内容。工作区、已安装的包和配置都不受影响。lip 安装的 Wine 在数据目录中，不在缓存里，也会保留。

恰好在这时向缓存写入的 lip 命令可能失败。请在没有其他 lip 命令运行时执行 `lip cache clean`。

## 选项

| 选项 | 说明 |
| --- | --- |
| `-h`、`--help` | 输出帮助并退出。 |

## 输出

`lip cache clean` 没有结果：它不向标准输出写任何内容。最后一行是写到标准错误的消息。

## 退出状态

| 退出码 | 含义 |
| --- | --- |
| `0` | 缓存已删除。 |
| 非 `0` | 用法错误，或无法删除该目录。 |

## 示例

```shell
lip cache clean
```

## 另请参阅

- [文件](../files.md#缓存目录)：缓存里有什么。
- [设置](../../interface/settings.md)：界面中的**清理缓存**。
- [故障排除](../../guides/troubleshooting.md)
