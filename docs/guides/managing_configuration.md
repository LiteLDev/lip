# Managing Configuration

lip has two kinds of configuration: the user configuration, with settings that affect how lip interacts with remote services, and the workspace configuration, with settings of one workspace. Add `--workspace` to a `lip config` command to use the configuration of the workspace in the current directory.

## Viewing Configuration

To see all current configuration settings:

```sh
lip config list
lip config list --workspace
```

To get the value of a specific key:

```sh
lip config get <KEY>
```

Example:
```sh
lip config get github_proxy
```

## Setting Configuration

To set a configuration value:

```sh
lip config set <KEY> <VALUE>
```

### Common Settings

-   `github_proxy` (user): A URL to use as a proxy for GitHub requests, or an empty string to connect to GitHub directly.
-   `restart` (workspace): `never` or `on-failure`, whether the server of the workspace is restarted when it exits.

Examples:
```sh
lip config set github_proxy https://mirror.ghproxy.com/
lip config set github_proxy ""
lip config set restart on-failure --workspace
```

## Deleting Configuration

To remove a setting and revert to its default (if any):

```sh
lip config delete <KEY>
```
