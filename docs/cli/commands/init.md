# lip init

Initialize a new project by creating a `tooth.json` manifest in the current directory.

## Synopsis

```shell
lip init
```

## Description

Creates a `tooth.json` file with default values:

- `tooth`: `"github.com/user/repo"` (placeholder, should be updated)
- `version`: `"0.0.0"`

If a `tooth.json` already exists in the current directory, it is overwritten.
