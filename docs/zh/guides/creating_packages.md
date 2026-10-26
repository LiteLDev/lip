# 创建包

本指南将带你使用 lip 创建新包。

## 使用 `lip init`

开始新项目最简单的方式是使用 `lip init`。

```sh
lip init
```

该命令会在当前目录创建一个基础 `tooth.json`。若该目录已存在 `tooth.json`，它会被覆盖。

生成的 `tooth.json` 如下：

```json
{
  "format_version": 3,
  "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
  "tooth": "github.com/user/repo",
  "version": "0.0.0",
  "info": {
    "name": "",
    "description": "",
    "tags": [],
    "avatar_url": ""
  },
  "variants": []
}
```

你应将 `"tooth"` 字段改为实际仓库路径或包标识。

## 手动创建

你也可以手动创建 `tooth.json`，并确保包含以下必填字段：

- `format_version`：当前为 `3`。
- `format_uuid`：必须为 `"289f771f-2c9a-4d73-9f3f-8492495a924d"`。
- `tooth`：包路径，必须是合法的 [Go module path](https://go.dev/ref/mod#module-path)，例如 `github.com/user/repo`。
- `version`：合法的语义化版本。

示例：

```json
{
  "format_version": 3,
  "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
  "tooth": "github.com/user/my-package",
  "version": "1.0.0",
  "info": {
    "description": "My awesome package"
  }
}
```

创建完成后，你可以使用 `lip install` 开始添加依赖。
