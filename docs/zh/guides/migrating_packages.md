# 迁移包

随着 lip 演进，`tooth.json` 格式可能变化。`lip migrate` 命令可帮助你将清单升级到最新版本。

## 使用 `lip migrate`

将旧版 `tooth.json` 迁移到当前格式（v3）：

```sh
lip migrate <FILE> <OUTPUT>
```

- `<FILE>`：旧格式 `tooth.json`（或同类文件）路径。
- `<OUTPUT>`：迁移后新清单的写入路径。

### 示例

假设你有一个名为 `tooth.old.json` 的 v2 清单：

```sh
lip migrate tooth.old.json tooth.json
```

该命令会读取 `tooth.old.json`，执行必要转换升级到 v3，并将结果保存为 `tooth.json`。

## 迁移逻辑

lip 会自动处理：
- 更新 `format_version`；
- 重组已移动或重命名的字段；
- 为新增必填字段补充默认值。

建议你始终检查输出的 `tooth.json`，确认迁移结果符合预期。
