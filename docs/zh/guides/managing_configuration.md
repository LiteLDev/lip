# 管理配置

lip 支持配置全局设置，以影响其与远程服务的交互方式。

## 查看配置

查看当前全部配置：

```sh
lip config list
```

查看某个键的值：

```sh
lip config get <KEY>
```

示例：
```sh
lip config get github_proxy
```

## 设置配置

设置配置值：

```sh
lip config set <KEY> <VALUE>
```

### 常见配置

- `github_proxy`：GitHub 请求使用的代理 URL。

示例：
```sh
lip config set github_proxy https://mirror.ghproxy.com/
```

## 删除配置

移除某项配置并恢复默认值（如果有）：

```sh
lip config delete <KEY>
```
