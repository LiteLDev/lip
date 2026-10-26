# Frequently Asked Questions

### Why does lip provide a daemon (`lipd`)?
The daemon (`lipd`) is designed for **external integrations**. It allows IDEs, GUIs, and other tools to interact with lip programmatically using a standard JSON-RPC interface.

**Note**: The standard `lip` CLI runs independently and **does not** use the daemon. This ensures the CLI remains simple and has zero startup overhead for standard operations.

### What is `tooth.json`?
`tooth.json` is the manifest file for a lip package. It defines the package's metadata (name, version, description), its dependencies, and how it should be installed on different platforms. Think of it like `package.json` for Node.js or `Cargo.toml` for Rust.

### Does `lip install` update `tooth.json`?
**No.** Running `lip install <package>` installs the package into your workspace and updates the lockfile (`tooth_lock.json`) to track the state, but it **does not** modify your `tooth.json` manifest. You must manually add dependencies to `tooth.json` if you want them to be permanent parts of your project's requirements.

### How do I upgrade lip itself?
Reinstall `lip` using the same method you used originally, such as `npm install -g @levimc/lip`, `winget upgrade levimc.lip`, or the latest Windows `setup.exe` from the GitHub releases page. There is no built-in `lip self-update` command yet. If you installed `lip` from the old `@futrime/lip` npm package or the `futrime.lip` winget package, uninstall it first and then install `@levimc/lip` or `levimc.lip`.

### Does lip require git?
No. lip talks to git repositories over HTTPS with its own built-in client, so the `git` command-line tool does not need to be installed. See [Internal Mechanism](guides/internal_mechanism.md#package-resolution).

### Does lip support private repositories?
Not currently. lip fetches packages anonymously over HTTPS and does not use the `git` command-line tool or its credential helpers, so only public repositories can be installed from.

### Where does lip store installed packages?
lip installs package files **directly into your workspace** based on the rules defined in `tooth.json`. Unlike some package managers, it does not isolate dependencies in a separate folder unless the package manifest explicitly specifies those locations.

The global cache is stored in your system's local application data directory (e.g., `%LOCALAPPDATA%\lip\cache` on Windows, or `~/.local/share/lip/cache` on Linux).
