# Managing Configuration

lip allows you to configure global settings that affect how it interacts with remote services.

## Viewing Configuration

To see all current configuration settings:

```sh
lip config list
```

To get the value of a specific key:

```sh
lip config get <key>
```

Example:
```sh
lip config get github_proxy
```

## Setting Configuration

To set a configuration value:

```sh
lip config set <key> <value>
```

### Common Settings

-   `github_proxy`: A URL to use as a proxy for GitHub requests.

Example:
```sh
lip config set github_proxy https://ghproxy.com
```

## Deleting Configuration

To remove a setting and revert to its default (if any):

```sh
lip config delete <key>
```
