# Frequently Asked Questions

### Why does lip provide `lip rpc`?
[`lip rpc`](cli/commands/rpc.md) is designed for **external integrations**. It allows IDEs, GUIs, and other tools to interact with lip programmatically using a standard JSON-RPC interface.

**Note**: Ordinary `lip` commands such as `lip install` run on their own and **do not** go through `lip rpc`. This keeps the CLI simple, with no extra startup overhead for standard operations.

### What is `tooth.json`?
`tooth.json` describes a lip package: its tooth path, its version, and for each variant, platform and RID, the dependencies, the files to place, the mod to install and the commands to run. The current format is format version 4. See [tooth.json](concepts/tooth_json.md).

### Can I still install packages with an older `tooth.json`?
**Yes.** lip still reads `tooth.json` of format versions 1 to 3: it reads each one as format 4 for the workspace's platform and RID, and installs it like any other package, placing the same files as before, so existing packages keep working. lip no longer writes these formats; use [`lip migrate`](cli/commands/migrate.md) to convert your package to format 4.

### Does `lip install` update `tooth.json`?
**No.** Running `lip install <package>` installs the package into your workspace and updates the [workspace file](concepts/lockfiles.md) to track the state, but it **does not** modify any `tooth.json`. You must manually add dependencies to your `tooth.json` if you want them to be permanent parts of your package's requirements.

### What happened to `tooth_lock.json`?
The state of a workspace is now `.lip/workspace.json` in the workspace, so it moves with the workspace: a workspace that is moved, renamed or copied keeps its installed packages. lip does not take over a `tooth_lock.json`, or the workspace file of another earlier lip version: in such a workspace it fails with a message. Uninstall the packages with the lip version that installed them, or delete the file to start over. See [Workspace File](concepts/lockfiles.md#earlier-formats).

### Can lip install client mods?
No. lip manages server workspaces only. Client workspaces are managed by launchers such as LeviLauncher, which read the same `tooth.json` packages.

### Why is a tag of my repository not a version?
Only tags named exactly `v` followed by a [SemVer 2.0.0](https://semver.org/) version, such as `v1.2.3`, are versions. Tags such as `v1.2` or `1.2.3` are ignored. You can still install any tag, branch or commit with a git ref, e.g. `lip install github.com/user/repo@tag:1.2.3` or `@branch:main`; the package's version is then the `version` its `tooth.json` declares. See [Git refs](cli/commands/install.md#git-refs).

### How do I install a package from my disk?
Run `lip install` with the path of a directory or a ZIP file that has a `tooth.json` at its root, e.g. `lip install ../MyTool` or `lip install ./package.zip`. The workspace file records the path: uninstalling needs nothing, and `lip update` reads it again.

### How do I upgrade lip itself?
Reinstall `lip` using the same method you used originally, such as `winget upgrade levimc.lip`, the latest Windows installer (`setup.exe` or `.msi`), or the latest portable archive from the GitHub releases page. There is no built-in `lip self-update` command yet. If you installed `lip` from the old `futrime.lip` winget package or from npm, uninstall it first and then install it again with one of the methods in [Installation](intro/installation.md).

### Does lip require git?
No. lip talks to git repositories over HTTPS with its own built-in client, so the `git` command-line tool does not need to be installed. See [Internal Mechanism](guides/internal_mechanism.md#package-resolution).

### Does lip support private repositories?
Not currently. lip fetches packages anonymously over HTTPS and does not use the `git` command-line tool or its credential helpers, so only public repositories can be installed from.

### Where does lip store installed packages?
lip installs package files **directly into your workspace**. A mod is installed to `mods/<name>/`, `<name>` being the `name` in its LeviLamina `manifest.json`, and `plugins` is a link to `mods` (see [Installing Dependencies](guides/installing_dependencies.md#server-workspaces-only)); a file package places its files where its `tooth.json` says.

The [workspace file](concepts/lockfiles.md) of a workspace is `.lip/workspace.json` inside it; the operation lock (`.lip/lock`) and journal (`.lip/journal`) are next to it. lip keeps its other files in per-user directories that follow the platform conventions:

| What | Windows | Linux / macOS |
| --- | --- | --- |
| Configuration (`config.yaml`) | `%APPDATA%\lip` | `$XDG_CONFIG_HOME/lip` (by default `~/.config/lip`) |
| Cache | `%LOCALAPPDATA%\lip\Cache` | `$XDG_CACHE_HOME/lip` (by default `~/.cache/lip`) |

The cache is safe to delete at any time, e.g. with `lip cache clean`. Downloads in progress are written to the hidden `.staging` directory inside the cache; lip writes nothing to the system temporary directory.

### What happens if an install fails halfway?
Nothing changes. `lip install`, `lip uninstall` and `lip update` are all or nothing, including the dependencies they install or remove: lip checks and downloads everything before it touches the workspace, and if a file cannot be written, a hook command fails, or you press Ctrl+C, it undoes every change it made and keeps the workspace file as it was. Only what the hook commands of packages did cannot be undone. If lip is killed in the middle, the next `lip install`, `uninstall` or `update` of the workspace first completes the interrupted operation, if it had already saved the workspace file, or undoes it otherwise, and tells you so. See [Internal Mechanism](guides/internal_mechanism.md#workspace-state).

### Can I run two lip commands at the same time?
Not on the same workspace: while one `lip install`, `uninstall` or `update` is changing a workspace, another one on that workspace fails at once with "Another process is changing the workspace". Commands on different workspaces, and commands that only read (such as `lip list` or `lip view`), can run at any time.
