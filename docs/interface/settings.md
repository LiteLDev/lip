# Settings

After this page you can change the language of the interface, tell lip how to reach GitHub, make a server restart itself after a crash, free disk space, and update lip.

## The page

Open the page: in the sidebar, move to **Settings** and press `Enter`.

![The Settings page: seven rows, each with its value, from Language to About. The line below the list says "The language of the interface."](/screenshots/en/settings-01-list.svg)

1. A row and its value. Move with `↑` and `↓`; `Enter` changes the row or carries it out.
2. What the selected row does. A value that is too long for its row is shown whole here.

| Row | Does | Belongs to |
| --- | --- | --- |
| [Language](#language) | Chooses the language of the interface. | You, on this computer. |
| [GitHub proxy](#github-proxy) | Says how lip reaches GitHub. | You, on this computer. |
| [Auto restart](#auto-restart) | Restarts the server when it crashes. | The selected workspace. |
| [Clear cache](#clear-cache) | Deletes the downloaded files. | You, on this computer. |
| [Check for updates](#updates) | Looks for a newer lip, and installs it. | lip itself. |
| [Daemon](#daemon) | Shows the background process of lip. | You, on this computer. |
| [About](#about) | Shows the version of lip. | lip itself. |

Every change on this page is made at once. There is no Save button.

## Language

The interface speaks English, Simplified Chinese and Traditional Chinese.

1. Move to **Language** and press `Enter`.

   ![The Language dialog lists "Follow system", English, 简体中文 and 繁體中文.](/screenshots/en/settings-02-language.svg)

2. Choose a language with `↑` and `↓`, and press `Enter`. The whole interface changes at once; nothing needs a restart.

   ![The Settings page in Simplified Chinese, just after the language was chosen. The row of the language reads 简体中文.](/screenshots/en/settings-23-language-changed.svg)

   1. The language that you chose.
   2. Every text of the interface is in it, the sidebar and the bottom line included.

**Follow system** uses the language of your system:

- On Windows, the display language of your user.
- Elsewhere, the first of the environment variables `LC_ALL`, `LC_MESSAGES` and `LANG` that names a language.
- A Chinese locale of Taiwan, Hong Kong or Macao, or one marked `Hant`, gives Traditional Chinese; any other Chinese locale gives Simplified Chinese; anything else gives English.

Only the interface itself is translated. What a command says is in English in every language: the lines of a plan, the messages of an installation, and errors.

::: details The same with a command
The choice is the key `language` of your configuration; see [Configuration](../reference/configuration.md).

```shell
lip config set language zh-Hans
lip config delete language
```

The values are `en`, `zh-Hans` and `zh-Hant`. Deleting the key is **Follow system**.
:::

## GitHub proxy

lip downloads LeviLamina and most mods from GitHub. Where GitHub is slow or cannot be reached, a **GitHub proxy** helps: a web address that lip puts before the GitHub address of every download.

The row has one of three values:

| Value | Means |
| --- | --- |
| **Not set** | lip connects to GitHub directly. Each time the interface starts, lip checks in the background whether GitHub answers, and asks you what to do when it does not; see [When GitHub cannot be reached](home.md#when-github-cannot-be-reached). |
| **Direct** | lip connects to GitHub directly, and does not check or ask. |
| A URL | lip sends its requests to GitHub through this proxy. A request that fails through the proxy is sent to GitHub directly. |

lip does not suggest a proxy, and these pages do not either. Use one that you trust: everything lip downloads from GitHub passes through it.

`Enter` on the row opens its actions:

![The menu "GitHub proxy" with three rows: "Set proxy…", "Connect directly" and "Reset". "Reset" is dimmed.](/screenshots/en/settings-06-proxy-menu.svg)

1. **Set proxy…** asks for the URL of a proxy, and tests it.
2. **Connect directly** sets the value **Direct**. It is dimmed when that is the value already.
3. **Reset** takes the setting away, back to **Not set**. It is dimmed when nothing is set.

### Set a proxy

1. Press `Enter` on **Set proxy…**, and type or paste the URL of the proxy. It starts with `https://` and usually ends with `/`.

   ![The dialog "GitHub proxy" with the field "Proxy URL", which holds an example address that starts with https.](/screenshots/en/settings-07-proxy-url.svg)

2. Press `Enter`. lip tests the proxy: it asks GitHub for LeviLamina through it, and waits up to 10 seconds. `Esc` stops the test and goes back to the URL.

   ![The dialog "Testing proxy" shows "Working…".](/screenshots/en/settings-08-testing.svg)

3. When GitHub answers through the proxy, lip saves it and says **Saved.** The row shows the URL.

   ![The row "GitHub proxy" now shows the address that was typed, and the bottom line ends with "Saved."](/screenshots/en/settings-09-proxy-saved.svg)

What you type must be a URL. Without `https://` in front, it is not one:

![The field holds old-proxy.example.com, and below it the message "Not a URL" is shown.](/screenshots/en/settings-10-not-a-url.svg)

### When the test fails

When GitHub does not answer through the proxy, lip says why and saves nothing yet.

![The dialog "Proxy failed" says "GitHub answered with the HTTP status 502." and has the buttons "Enter another", "Save anyway" and Cancel.](/screenshots/en/settings-11-proxy-failed.svg)

1. **Enter another** goes back to the URL, which is still there to correct.
2. **Save anyway** saves the proxy although the test failed. Use it when you know that the proxy works and only the test does not get through.
3. **Cancel**, or `Esc`, changes nothing: the row keeps the value it had.

The reason is in English. A status such as 502 or 404 means that the proxy answered, but not as GitHub does; a timeout means that nothing answered within 10 seconds. [Troubleshooting](../guides/troubleshooting.md) has more.

### Connect directly, or reset

**Connect directly** is for a network that reaches GitHub well enough: lip stops checking and asking.

![The row "GitHub proxy" shows "Direct", and the bottom line ends with "Saved."](/screenshots/en/settings-12-direct.svg)

**Reset** brings back **Not set**: lip connects directly, and checks again the next time the interface starts.

::: details The same with a command
The setting is the key `github_proxy` of your configuration; see [Configuration](../reference/configuration.md).

```shell
lip config set github_proxy https://proxy.example.com/
lip config set github_proxy ""
lip config delete github_proxy
```

The second line is **Connect directly**, and the third is **Reset**. The commands do not test the proxy.
:::

## Auto restart

With **Auto restart** on, lip starts the server of the selected workspace again when it crashes, that is, when it ends with an error instead of being stopped.

Move to **Auto restart** and press `Space` or `Enter`: `[x]` **On**, `[ ]` **Off**.

![The row "Auto restart" shows a checked box and "On". The line below says "Restarts the server of this workspace when it crashes."](/screenshots/en/settings-13-auto-restart.svg)

1. The setting is of the selected workspace only. Each workspace has its own; switch workspaces to see another.

- lip waits 5 seconds before it starts the server again.
- A server that you stop is not restarted.
- A server that keeps crashing is restarted at most 5 times within 10 minutes.
- The setting counts as it is when the server ends, so you can switch it while the server runs.

The [Server](server.md) page has the same switch.

::: details The same with a command
```shell
lip config set restart on-failure --workspace
lip config set restart never --workspace
```
:::

## Clear cache

lip keeps what it downloads in a cache, so that it need not download the same file twice. The row shows the folder of the cache. Clearing it frees disk space; lip downloads again what it needs later.

1. Move to **Clear cache** and press `Enter`. lip asks first.

   ![The dialog "Clear cache" asks "Delete all downloaded files?" with the buttons Clear and Cancel.](/screenshots/en/settings-14-clear-cache.svg)

2. Press `Enter` on **Clear**.

   ![The dialog "Clear cache" says "Cache cleared." and has the button OK.](/screenshots/en/settings-15-cache-cleared.svg)

What is deleted is the cache folder and everything in it, which is all that lip downloaded: the files of the game, of LeviLamina and of the mods, and the download of an update of lip. Your workspaces, their servers and mods, and your settings are not touched.

The command is [`lip cache clean`](../reference/commands/cache_clean.md).

## Updates

lip updates itself.

### Check for updates

Move to **Check for updates** and press `Enter`. lip asks GitHub for its newest release.

When there is none newer, the row says so:

![The row "Check for updates" shows "Up to date" after the version 0.41.0, and the bottom line ends with "Up to date" too.](/screenshots/en/settings-16-up-to-date.svg)

When there is one, lip offers to install it:

![The dialog "Install update" says "lip 0.42.0 is available." and has the buttons Install and Later.](/screenshots/en/settings-04-update-available.svg)

**Install** installs it now. **Later** closes the dialog; the newer version stays on the page, and a row **Install update** appears below **Check for updates**:

![The row "Check for updates" shows "0.41.0 → 0.42.0", and below it a new row "Install update" shows 0.42.0.](/screenshots/en/settings-05-update-row.svg)

### Install the update

1. Press `Enter` on **Install update**, and again on **Install**.

   ![The dialog "Install update" asks "Install lip 0.42.0?" with the buttons Install and Cancel.](/screenshots/en/settings-19-install-question.svg)

2. lip downloads the release, checks it, and installs it. What happens next depends on how lip was installed.

   When lip was installed from a portable archive, its files are replaced at once:

   ![The dialog "Install update" says "lip 0.42.0 is installed. Restart lip to use it."](/screenshots/en/settings-20-installed.svg)

   When lip was installed with the Windows installer, lip starts the installer of the new version. Windows asks for your permission, since the installer changes a program:

   ![The dialog "Install update" says "The installer of lip 0.42.0 is running. Quit lip so that it can finish."](/screenshots/en/settings-22-installer.svg)

### After installing

The lip on your screen is still the old one. The row reminds you:

![The row "Check for updates" shows "0.42.0 · Restart lip". The row "Install update" is gone.](/screenshots/en/settings-21-restart-lip.svg)

- **Restart lip**: quit lip, and start it again. The new version is then the one that runs.
- **Installer running**: quit lip, so that the installer can replace it; then start lip again.

Until then, `Enter` on **Check for updates** shows the same message again instead of checking.

The commands are `lip self-update --check` and `lip self-update`. [`lip self-update`](../reference/commands/self_update.md) describes updating in full.

## Daemon

The **daemon** is the background process of lip. It carries out the commands and the interface, and it runs the servers that you start, so that they keep running when you close the terminal. [How lip works](../concepts/how_lip_works.md) explains it.

The row shows whether a daemon runs. Move to it to read more below the list:

![The row "Daemon" shows "Running · 0.41.0 · PID 4120", and the lines below the list end with "Commands in progress: 1 · Servers running: 0".](/screenshots/en/settings-17-daemon.svg)

1. **Running**, and the version of lip that the daemon is.
2. **PID**: the process ID of the daemon, by which your system's task manager knows it.
3. **Commands in progress**: how many commands the daemon carries out right now. Each open interface is one of them, so the number is at least 1 here.
4. **Servers running**: how many servers the daemon runs right now, of all workspaces. It is shown only when this daemon carries out your interface.

`Enter` asks the daemon again and shows the new numbers.

**Not running** means that no daemon answers. lip then carries out the interface itself, and the servers that you start in it are stopped when you quit lip.

The interface cannot stop the daemon, because the daemon carries out the interface. Quit lip and run [`lip daemon stop`](../reference/commands/daemon.md).

## About

The last row shows the version of lip that runs. `Enter` does nothing here.

![The row "About" is selected and shows "lip 0.41.0". The line below says "The version of lip that is running."](/screenshots/en/settings-18-about.svg)

Give this version when you report a problem. The command is `lip --version`.
