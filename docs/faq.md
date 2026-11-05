# Frequently Asked Questions

### Why does lip provide `lip rpc`?
[`lip rpc`](cli/commands/rpc.md) is designed for **external integrations**. It allows IDEs, GUIs, and other tools to interact with lip programmatically using a standard JSON-RPC interface.

**Note**: Ordinary `lip` commands such as `lip install` run on their own and **do not** go through `lip rpc`. This keeps the CLI simple, with no extra startup overhead for standard operations.

### What is `manifest.json`?
`manifest.json` is the manifest of a lip package, marked by `"format": "bf45b1"`. LeviLamina reads the same file to load a mod, and lip reads what it needs to install it: where to find the package's dependencies, which build to install on which platform, and how to place files. See [Package Manifest](concepts/package_manifest.md).

### Can I still install `tooth.json` packages?
**Yes.** lip still reads `tooth.json` (format versions 1 to 3): it converts each one in memory into a file package and installs it like any other, placing the same files as before, so existing packages keep working. Likewise, a LeviLamina `manifest.json` without the `format` key is installed as a mod. lip no longer writes these formats; use [`lip migrate`](cli/commands/migrate.md) to convert your package to the current `manifest.json`.

### Does `lip install` update `manifest.json`?
**No.** Running `lip install <package>` installs the package into your workspace and updates the [lockfile](concepts/lockfiles.md) to track the state, but it **does not** modify your `manifest.json`. You must manually add dependencies to `manifest.json` if you want them to be permanent parts of your package's requirements.

### What happened to `tooth_lock.json`?
The lock is now `.lip/workspace.json` in the workspace, so it moves with the workspace: a workspace that is moved, renamed or copied keeps its installed packages. lip migrates an existing `tooth_lock.json` automatically the next time you install, uninstall or update a package, and then deletes it. See [Lockfiles](concepts/lockfiles.md#location).

### Can lip install client mods?
No. lip manages server workspaces only. Client workspaces are managed by launchers such as LeviLauncher, which read the same `manifest.json` packages.

### Why is a tag of my repository not a version?
Only tags named exactly `v` followed by a [SemVer 2.0.0](https://semver.org/) version, such as `v1.2.3`, are versions. Tags such as `v1.2` or `1.2.3` are ignored. You can still install any tag, branch or commit with a git ref, e.g. `lip install github.com/user/repo@tag:1.2.3` or `@branch:main`; the package's version is then the `version` its `manifest.json` (or `tooth.json`) declares. See [Git refs](cli/commands/install.md#git-refs).

### How do I install a package from my disk?
Pack it into a `.mcmod` (or `.zip`) file with `manifest.json` at its root, for example with [`lip pack`](cli/commands/pack.md), and run `lip install ./package.mcmod`. lip does not install from a plain directory. The lock records the file's path and SHA-256: uninstalling needs nothing, and `lip update` reads the file again.

### How do I upgrade lip itself?
Reinstall `lip` using the same method you used originally, such as `winget upgrade levimc.lip`, the latest Windows installer (`setup.exe` or `.msi`), or the latest portable archive from the GitHub releases page. There is no built-in `lip self-update` command yet. If you installed `lip` from the old `futrime.lip` winget package or from npm, uninstall it first and then install it again with one of the methods in [Installation](intro/installation.md).

### Does lip require git?
No. lip talks to git repositories over HTTPS with its own built-in client, so the `git` command-line tool does not need to be installed. See [Internal Mechanism](guides/internal_mechanism.md#package-resolution).

### Does lip support private repositories?
Not currently. lip fetches packages anonymously over HTTPS and does not use the `git` command-line tool or its credential helpers, so only public repositories can be installed from.

### Where does lip store installed packages?
lip installs package files **directly into your workspace**. A mod is installed to `mods/<name>/`, and `plugins` is a link to `mods` (see [Installing Dependencies](guides/installing_dependencies.md#server-workspaces-only)); a file package places its files where its `manifest.json` says.

The [lockfile](concepts/lockfiles.md) of a workspace is `.lip/workspace.json` inside it; the operation lock (`.lip/lock`) and journal (`.lip/journal`) are next to it. lip keeps its other files in per-user directories that follow the platform conventions:

| What | Windows | Linux / macOS |
| --- | --- | --- |
| Configuration (`config.yaml`) | `%APPDATA%\lip` | `$XDG_CONFIG_HOME/lip` (by default `~/.config/lip`) |
| Cache | `%LOCALAPPDATA%\lip\Cache` | `$XDG_CACHE_HOME/lip` (by default `~/.cache/lip`) |

The cache is safe to delete at any time, e.g. with `lip cache clean`. Downloads in progress are written to the hidden `.staging` directory inside the cache; lip writes nothing to the system temporary directory.

### What happens if an install fails halfway?
Nothing changes. `lip install`, `lip uninstall` and `lip update` are all or nothing, including the dependencies they install or remove: lip checks and downloads everything before it touches the workspace, and if a file cannot be written, a hook command fails, or you press Ctrl+C, it undoes every change it made and keeps the lock as it was. Only what the hook commands of packages did cannot be undone. If lip is killed in the middle, the next `lip install`, `uninstall` or `update` of the workspace first completes the interrupted operation, if it had already saved the lock, or undoes it otherwise, and tells you so. See [Internal Mechanism](guides/internal_mechanism.md#workspace-state).

### Can I run two lip commands at the same time?
Not on the same workspace: while one `lip install`, `uninstall` or `update` is changing a workspace, another one on that workspace fails at once with "Another process is changing the workspace". Commands on different workspaces, and commands that only read (such as `lip list` or `lip view`), can run at any time.
