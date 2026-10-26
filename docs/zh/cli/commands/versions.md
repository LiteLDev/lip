# lip versions

显示某个包的可用版本。

版本来自包仓库（`https://<包路径>.git`）的 git 标签。只会列出形如 `v<semver>`（例如 `v1.2.3`）的标签。

## 概要

```sh
lip versions <package>
```

## 示例

列出包的可用版本：

```sh
lip versions github.com/user/repo
```
