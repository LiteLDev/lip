# Frequently Asked Questions

### What is `lip.toml`?
`lip.toml` is the package file of a lip package: its package id, its version, and for each variant, side and platform, the dependencies, the files to place, the mod to install and the commands to run. See [lip.toml](concepts/lip_toml.md).

### Can I still install packages of lip v0.x, with a `tooth.json`?
**Yes.** lip still reads `tooth.json` of format versions 1 to 3: it reads each one as a package file for the workspace's side and platform, and installs it like any other package, placing the same files as before, so existing packages keep working. lip no longer writes `tooth.json`; use [`lip pkg migrate`](cli/commands/pkg_migrate.md) to convert your package to `lip.toml`. A workspace of lip v0.x (with a `tooth_lock.json`) and a `liprc.json` are migrated automatically.

### Does `lip install` update `lip.toml`?
**No.** Running `lip install <package>` installs the package into your workspace and updates the [state file](concepts/lockfiles.md) to track the state, but it **does not** modify any `lip.toml`. You must manually add dependencies to the `deps` of your `lip.toml` if you want them to be permanent parts of your package's requirements.

### Can lip install client mods?
No. lip manages server workspaces only. Client workspaces are managed by launchers such as LeviLauncher, which read the same packages.

### Can I run a server on Linux?
**Yes.** lip installs the packages of the platform `win-x64` by default on every system, and on Linux and macOS runs their hook commands and the server through Wine, which you install yourself. To install Linux-native packages instead, give `--platform linux-x64` on the first `lip install` of the workspace; `lip run` cannot start a Linux-native server, so you start that one yourself. See [Running a Server](guides/running_server.md#linux-native-packages).

### Why is a tag of my repository not a version?
Only tags named exactly `v` followed by a [SemVer 2.0.0](https://semver.org/) version, such as `v1.2.3`, are versions. Tags such as `v1.2` or `1.2.3` are ignored. You can still install any tag, branch or commit with a git ref, e.g. `lip install github.com/user/repo@tag:1.2.3` or `@branch:main`; the package's version is then the `version` its `lip.toml` declares. See [Git refs](cli/commands/install.md#git-refs).

### How do I install a package from my disk?
Run `lip install` with the path of a directory or a ZIP file that has a `lip.toml` at its root, e.g. `lip install ../MyTool` or `lip install ./package.zip`. The state file records the path: uninstalling needs nothing, and `lip update` reads it again.

### How do I upgrade lip itself?
Reinstall `lip` using the same method you used originally, such as the latest Windows installer (`setup.exe` or `.msi`), or the latest portable archive from the GitHub releases page. There is no built-in `lip self-update` command yet. If you installed `lip` from the old `futrime.lip` winget package or from npm, uninstall it first and then install it again with one of the methods in [Installation](intro/installation.md).

### Does lip require git?
No. lip talks to git repositories over HTTPS with its own built-in client, so the `git` command-line tool does not need to be installed. See [Internal Mechanism](guides/internal_mechanism.md#package-resolution).

### Does lip support private repositories?
Not currently. lip fetches packages anonymously over HTTPS and does not use the `git` command-line tool or its credential helpers, so only public repositories can be installed from.

### Where does lip store installed packages?
lip installs package files **directly into your workspace**. A mod is installed to `mods/<name>/`, `<name>` being the `name` in its LeviLamina `manifest.json`; a file package places its files where its `lip.toml` says.

The [state file](concepts/lockfiles.md) of a workspace is `.lip/state.json` inside it; the operation lock (`.lip/lock`) and journal (`.lip/journal`) are next to it. lip keeps its other files in per-user directories that follow the platform conventions:

| What | Windows | Linux / macOS |
| --- | --- | --- |
| Configuration (`config.toml`) | `%APPDATA%\lip` | `$XDG_CONFIG_HOME/lip` (by default `~/.config/lip`) |
| Cache | `%LOCALAPPDATA%\lip\Cache` | `$XDG_CACHE_HOME/lip` (by default `~/.cache/lip`) |

The cache is safe to delete at any time, e.g. with `lip cache clean`. Downloads in progress are written to `.tmp` files inside the cache and renamed into place when complete; lip writes nothing to the system temporary directory.

### What happens if an install fails halfway?
Nothing changes. `lip install`, `lip uninstall` and `lip update` are all or nothing, including the dependencies they install or remove: lip checks and downloads everything before it touches the workspace, and if a file cannot be written, a hook command fails, or you press Ctrl+C, it undoes every change it made and keeps the state file as it was. Only what the hook commands of packages did cannot be undone. If lip is killed in the middle, the next `lip install`, `uninstall`, `update` or `run` in the workspace first completes the interrupted operation, if it had already saved the state file, or undoes it otherwise, and tells you so. See [Internal Mechanism](guides/internal_mechanism.md#workspace-state).

### Can I run two lip commands at the same time?
Not on the same workspace: while one `lip install`, `uninstall` or `update` is changing a workspace, another one on that workspace fails at once, saying that another lip command is changing the workspace. Commands on different workspaces, and commands that only read (such as `lip list` or `lip pkg view`), can run at any time. A server started by [`lip run`](cli/commands/run.md) does not hold the workspace: you can install, update and uninstall packages while it runs, and the changes take effect when you start it again.

### A command failed. Where can I find out why?
lip prints what went wrong, each cause, and what you can do about it. Unless the mistake is in the command line itself, it also writes a failure report to `.lip/logs/` in the current directory and prints its path. The report holds everything lip knows about the failure: the lip version, the command, the system, the workspace state, the full error with every cause and stack trace, and a trace of the run (network requests, git operations, cache use, version resolution, file changes and the output of hook commands). Credentials in URLs are masked. lip keeps the newest 20 reports. Attach the report when you [report a problem](https://github.com/LiteLDev/lip/issues).
