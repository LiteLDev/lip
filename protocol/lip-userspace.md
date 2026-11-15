# lip-userspace(7)

## NAME

lip-userspace - the per-user directories, the cache, the data directory, and the state directory

## DESCRIPTION

### Directories

| Directory | Windows | Other | Holds |
|---|---|---|---|
| **config directory** | `%APPDATA%\lip` | `$XDG_CONFIG_HOME/lip` | The user configuration ([config.toml(5)](config.toml.md)). |
| **cache directory** | `%LOCALAPPDATA%\lip\Cache` | `$XDG_CACHE_HOME/lip` | The cache. |
| **data directory** | `%LOCALAPPDATA%\lip\Data` | `$XDG_DATA_HOME/lip` | The system dependencies that `lip` installs ("Data directory"). |
| **state directory** | `%LOCALAPPDATA%\lip\State` | `$XDG_STATE_HOME/lip` | The workspace list of the interface ([lip-tui(1), "Workspaces"](lip-tui.md#workspaces)), and the files of the daemon ([lip-daemon(1)](lip-daemon.md)) when it has no other daemon directory, whose names and content are not defined here. |

On systems other than Windows, an `XDG_*` variable that is unset, empty or not an absolute path takes its [XDG] default.

Besides these directories and the daemon directory ([lip-daemon(1)](lip-daemon.md)), `lip` writes only to the workspace it operates on, to paths given as arguments, and to its installation directory as [lip-self-update(1)](lip-self-update.md) defines, never to the system temporary directory. The programs that `lip` runs ([lip-workspace(7), "Running programs"](lip-workspace.md#running-programs)), the installers it runs ([lip-workspace(7), "System dependencies"](lip-workspace.md#system-dependencies)), and Wine, are exempt.

### Cache

Every process using the cache directory, `lip` or not, follows this section; other processes may read and write it.

Entries:

| Entry | Content |
|---|---|
| `http-<H>` | A regular file: the body of a successful response to an HTTPS GET of URL U, following redirects. `<H>` is the lowercase hexadecimal SHA-256 of U in UTF-8; U is the URL as a source or package file writes it, without its fragment, whatever proxy was used. |
| `<N>.tmp` | A file being written; `<N>` is any name. |
| `self-update` | A directory that only `lip self-update` ([lip-self-update(1)](lip-self-update.md)) writes to, and only while it runs; this section does not apply to its content. |

Any other entry is not defined here; a process must not read meaning into it.

- Writing: A process creates an entry only by writing it atomically, through a `.tmp` file under a name no other process uses. It must not modify an entry in place; if the entry exists, the rename either fails or replaces it with a file of the same meaning.
- Reading: An existing entry is complete. A reader that knows what an `http-<H>` entry's content must be must check it, and may delete it on mismatch.
- Repairing: When `lip` needs the ZIP archive of a URL, and the `http-<H>` entry of that URL exists and is not a ZIP archive, `lip` deletes the entry and downloads the URL again, once.
- Deleting: An `http-<H>` entry may be deleted at any time; a `.tmp` file, when no process is writing it; `self-update`, when no `lip self-update` runs. `lip cache clean` ([lip-cache(1)](lip-cache.md)) is exempt: it may delete the whole cache directory, and a process writing to the cache then may fail.

### Data directory

The data directory holds the Wine of `lip` ([lip-workspace(7), "System dependencies"](lip-workspace.md#system-dependencies)), shared by every workspace, and what `lip` downloads to install the system dependencies. Only `lip` writes to it; its entries are not defined here, and another process must not read meaning into them.

Deleting the data directory while no `lip` process runs loses nothing: `lip` installs the system dependencies again when it needs them.

### State directory

Deleting the state directory while no `lip` process runs loses nothing but the workspace list.

## SEE ALSO

[lip-cache(1)](lip-cache.md), [lip-daemon(1)](lip-daemon.md), [lip-self-update(1)](lip-self-update.md), [lip-tui(1)](lip-tui.md), [config.toml(5)](config.toml.md), [lip-workspace(7)](lip-workspace.md)

[XDG]: https://specifications.freedesktop.org/basedir-spec/latest/
