# Configuration

After this page you can read and change the settings of lip with `lip config`, and you know what to do when GitHub is slow or blocked where you are. In the interface, the same settings are on the [Settings](../interface/settings.md) page.

## Two sets of settings

lip has few settings. They come in two sets, called scopes:

| Scope | Applies to | Keys | Command |
| --- | --- | --- | --- |
| **User** | You, in every workspace | `github_proxy`, `language` | `lip config …` |
| **Workspace** | One server folder | `restart` | `lip config … --workspace` |

A command with `--workspace` works on the folder you are in. A command without it works on your user settings, wherever you are.

## Read and change settings

[`lip config`](../reference/commands/config.md) has four commands.

**See every setting** of a scope:

```sh
lip config list
```

```text
┌──────────────┬───────────┐
│ Key          │ Value     │
├──────────────┼───────────┤
│ github_proxy │ (not set) │
│ language     │ (not set) │
└──────────────┴───────────┘
```

```sh
lip config list --workspace
```

```text
┌─────────┬───────┐
│ Key     │ Value │
├─────────┼───────┤
│ restart │ never │
└─────────┴───────┘
```

`(not set)` means the key has no value. `(empty)` means it is set to nothing, which for `github_proxy` is a choice of its own (see below).

**See one setting:**

```sh
lip config get language
```

**Change a setting:**

```sh
lip config set language zh-Hans
```

```text
✓ Config key 'language' set to 'zh-Hans'.
```

lip refuses a value that the key does not allow, and changes nothing.

**Remove a setting**, so that it goes back to its default:

```sh
lip config delete language
```

```text
✓ Config key 'language' deleted.
```

`lip config get` and `lip config list` take `--json` for scripts. A key used with the wrong scope fails, and the message tells you whether to add or drop `--workspace`.

## The keys

The complete description of both files is in [Configuration files](../reference/configuration.md).

### github_proxy

Scope: user. Value: a URL, or empty. Default: not set.

How lip reaches GitHub, where most packages are downloaded from.

| Value | lip |
| --- | --- |
| not set | connects to GitHub directly. The interface may ask you to choose when it cannot reach GitHub. |
| empty | connects to GitHub directly, and the interface does not ask. |
| a URL | sends requests for GitHub through that address first, and directly when that fails. |

See [When GitHub is slow or blocked](#when-github-is-slow-or-blocked).

### language

Scope: user. Value: `en`, `zh-Hans` or `zh-Hant`. Default: not set.

The language of the interface: English, Simplified Chinese or Traditional Chinese. Without a value, the interface follows the language of your system, and uses English when that is neither English nor Chinese.

```sh
lip config set language zh-Hant
```

This changes the interface only. What the commands write to the terminal is always English.

### restart

Scope: workspace. Value: `never` or `on-failure`. Default: `never`.

Whether lip starts the server of this workspace again when it crashes, that is, exits with a code other than 0.

```sh
lip config set restart on-failure --workspace
```

With `on-failure`, lip waits 5 seconds and starts the server again, at most 5 times within 10 minutes. See [Restart the server when it crashes](server_from_commands.md#restart-the-server-when-it-crashes).

## When GitHub is slow or blocked

lip downloads most packages from GitHub. Where GitHub is slow or cannot be reached, installing fails with:

```text
hint: lip could not reach the server. Check your network connection. If GitHub is slow or blocked where you are, set a GitHub proxy with 'lip config set github_proxy <URL>'.
```

### What a GitHub proxy is

A GitHub proxy is a web service that fetches from GitHub on your behalf. You give lip its address, and lip puts that address in front of what it asks GitHub for.

With `github_proxy` set to `https://proxy.example.com/`, a request for

```text
https://github.com/LiteLDev/LeviLamina.git/info/refs
```

is sent to

```text
https://proxy.example.com/LiteLDev/LeviLamina.git/info/refs
```

So the address you set must be one where adding `<owner>/<repository>/…` leads to the same thing on GitHub. Some services want the whole GitHub address after their own. For those, include it in the setting, as in `https://proxy.example.com/https://github.com/`.

Three things to know:

- **Only GitHub goes through it.** Requests to `github.com`, for repositories and for files, use the proxy. Downloads from other sites, such as the game from minecraft.net, do not.
- **lip falls back.** When a request through the proxy fails, lip sends it to GitHub directly.
- **lip does not recommend a proxy.** A proxy sees everything you download through it and could change it. Use one that you or someone you trust runs.

### Set a proxy

```sh
lip config set github_proxy https://proxy.example.com/
```

Replace the address with that of your proxy. The setting applies to every command from then on.

### Test a proxy

lip checks that it can reach GitHub by asking for the list of versions of LeviLamina. You can ask for the same through your proxy. A proxy that works answers `200`:

::: code-group

```powershell [Windows]
curl.exe -s -o NUL -w "%{http_code}" "https://proxy.example.com/LiteLDev/LeviLamina.git/info/refs?service=git-upload-pack"
```

```sh [Linux / macOS]
curl -s -o /dev/null -w "%{http_code}\n" "https://proxy.example.com/LiteLDev/LeviLamina.git/info/refs?service=git-upload-pack"
```

:::

The interface does this test for you: when you enter a proxy on the Settings page, it tests it before it saves it. See [Settings](../interface/settings.md).

A command that works does not prove that the proxy works, because lip falls back to GitHub. If installs are still slow with a proxy set, test the proxy.

### Remove a proxy

To stop using the proxy and have the interface ask again when GitHub cannot be reached:

```sh
lip config delete github_proxy
```

To stop using it and tell lip that you connect directly, so that the interface does not ask, set it to an empty value:

::: code-group

```powershell [Windows]
lip config set github_proxy --% ""
```

```sh [Linux / macOS]
lip config set github_proxy ""
```

:::

In PowerShell, `--%` is needed: without it, Windows PowerShell leaves the empty value out. In the interface, choose "Connect directly" for the GitHub proxy on the Settings page.

### Your system's proxy

`github_proxy` is not an ordinary HTTP proxy. If your network has one, lip uses it without a setting of its own:

- lip follows the environment variables `HTTPS_PROXY`, `HTTP_PROXY`, `ALL_PROXY` and `NO_PROXY` (or the same names in lower case) of the terminal you run it in.
- Without them, it uses the proxy that your system is set up with.

::: code-group

```powershell [Windows]
$env:HTTPS_PROXY = "http://proxy.example.com:8080"
lip install LegacyScriptEngine#lua
```

```sh [Linux / macOS]
HTTPS_PROXY=http://proxy.example.com:8080 lip install LegacyScriptEngine#lua
```

:::

Replace the address with that of your proxy. Such a proxy applies to every download, not only to GitHub.

## Where the settings are kept

| Scope | File |
| --- | --- |
| User, on Windows | `%APPDATA%\lip\config.toml` |
| User, on Linux and macOS | `$XDG_CONFIG_HOME/lip/config.toml`, by default `~/.config/lip/config.toml` |
| Workspace | `.lip/config.toml` in the server folder |

Both are small text files that you may edit by hand. A file that is missing or cannot be read counts as having no settings. The format is in [Configuration files](../reference/configuration.md), and every file lip uses is listed in [Files and directories](../reference/files.md).

The workspace settings travel with the server folder when you move or copy it. The user settings do not.
