# Frequently Asked Questions

### Why does lip provide a daemon (`lipd`)?
The daemon (`lipd`) is designed for **external integrations**. It allows IDEs, GUIs, and other tools to interact with lip programmatically using a standard JSON-RPC interface.

**Note**: The standard `lip` CLI runs independently and **does not** use the daemon. This ensures the CLI remains simple and has zero startup overhead for standard operations.

### What is `manifest.json`?
`manifest.json` is the manifest of a lip package. It is the manifest of a LeviLamina mod, extended with what lip needs: where to find the package's dependencies, which build to install on which platform, and how to place files. See [Package Manifest](concepts/package_manifest.md).

### Can I still install `tooth.json` packages?
**Yes.** lip still reads `tooth.json` (format versions 1 to 3), so existing packages keep working. lip no longer writes `tooth.json`; use [`lip migrate`](cli/commands/migrate.md) to convert your package to `manifest.json`.

### Does `lip install` update `manifest.json`?
**No.** Running `lip install <package>` installs the package into your workspace and updates the [lockfile](concepts/lockfiles.md) to track the state, but it **does not** modify your `manifest.json`. You must manually add dependencies to `manifest.json` if you want them to be permanent parts of your package's requirements.

### What happened to `tooth_lock.json` and `lip_lock.json`?
The lock no longer lives in the workspace: lip keeps it in its per-user data directory, in `%LOCALAPPDATA%\lip\workspaces\` on Windows or `$XDG_DATA_HOME/lip/workspaces/` (by default `~/.local/share/lip/workspaces/`) on Linux and macOS, one file per workspace. lip migrates an existing `lip_lock.json` or `tooth_lock.json` automatically the next time you install, uninstall or update a package, and then deletes it from the workspace. See [Lockfiles](concepts/lockfiles.md#location).

### Can lip install client mods?
No. lip manages server workspaces only. Client workspaces are managed by launchers such as LeviLauncher, which read the same `manifest.json` packages.

### Why is a tag of my repository not a version?
Only tags named exactly `v` followed by a [SemVer 2.0.0](https://semver.org/) version, such as `v1.2.3`, are versions. Tags such as `v1.2` or `1.2.3` are ignored.

### How do I upgrade lip itself?
Reinstall `lip` using the same method you used originally, such as `npm install -g @levimc/lip`, `winget upgrade levimc.lip`, or the latest Windows `setup.exe` from the GitHub releases page. There is no built-in `lip self-update` command yet. If you installed `lip` from the old `@futrime/lip` npm package or the `futrime.lip` winget package, uninstall it first and then install `@levimc/lip` or `levimc.lip`.

### Does lip require git?
No. lip talks to git repositories over HTTPS with its own built-in client, so the `git` command-line tool does not need to be installed. See [Internal Mechanism](guides/internal_mechanism.md#package-resolution).

### Does lip support private repositories?
Not currently. lip fetches packages anonymously over HTTPS and does not use the `git` command-line tool or its credential helpers, so only public repositories can be installed from.

### Where does lip store installed packages?
lip installs package files **directly into your workspace**. A mod is installed to `mods/<name>/`, and `plugins` is a link to `mods` (see [Installing Dependencies](guides/installing_dependencies.md#server-workspaces-only)); a file package places its files where its `manifest.json` says.

lip keeps its own files in per-user directories that follow the platform conventions:

| What | Windows | Linux / macOS |
| --- | --- | --- |
| Configuration (`config.yaml`) | `%APPDATA%\lip` | `$XDG_CONFIG_HOME/lip` (by default `~/.config/lip`) |
| Lockfiles (`workspaces/<hash>.json`) | `%LOCALAPPDATA%\lip` | `$XDG_DATA_HOME/lip` (by default `~/.local/share/lip`) |
| Cache | `%LOCALAPPDATA%\lip\Cache` | `$XDG_CACHE_HOME/lip` (by default `~/.cache/lip`) |

The cache is safe to delete at any time, e.g. with `lip cache clean`. Downloads in progress are written to the hidden `.staging` directory inside the cache; lip writes nothing to the system temporary directory.
