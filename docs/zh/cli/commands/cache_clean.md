# lip cache clean

清理本地缓存。

## 概要

```shell
lip cache clean
```

## 说明

删除本地缓存目录中的所有缓存数据：Windows 为 `%LOCALAPPDATA%\lip\Cache`，Linux 和 macOS 为 `$XDG_CACHE_HOME/lip`（默认 `~/.cache/lip`）。缓存中只有 lip 可以重新下载的数据，因此随时可以清理；已安装的包和锁文件不受影响。
