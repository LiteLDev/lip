# lip cache clean

Clean the local cache.

## Synopsis

```shell
lip cache clean
```

## Description

Removes all cached data from the local cache directory: `%LOCALAPPDATA%\lip\Cache` on Windows, `$XDG_CACHE_HOME/lip` (by default `~/.cache/lip`) on Linux and macOS. The cache holds only data that lip can download again, so it is safe to clean at any time; installed packages and the lockfile are not affected.
