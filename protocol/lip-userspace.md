# lip-userspace(7)

## NAME

lip-userspace - the per-user directories, and the cache

## DESCRIPTION

### Directories

| Directory | Windows | Other | Holds |
|---|---|---|---|
| **config directory** | `%APPDATA%\lip` | `$XDG_CONFIG_HOME/lip` | The configuration ([config.toml(5)](config.toml.md)). |
| **cache directory** | `%LOCALAPPDATA%\lip\Cache` | `$XDG_CACHE_HOME/lip` | The cache. |

On systems other than Windows, an `XDG_*` variable that is unset, empty or not an absolute path takes its [XDG] default.

Besides these directories, `lip` writes only to the workspace it operates on and to paths given as arguments, never to the system temporary directory. The programs that `lip` runs ([lip-workspace(7), "Running programs"](lip-workspace.md#running-programs)), and Wine, are exempt.

### Cache

Every process using the cache directory, `lip` or not, follows this section; other processes may read and write it.

Entries:

| Entry | Content |
|---|---|
| `http-<H>` | A regular file: the body of a successful response to an HTTPS GET of URL U, following redirects. `<H>` is the lowercase hexadecimal SHA-256 of U in UTF-8; U is the URL as a source or package file writes it, without its fragment, whatever proxy was used. |
| `<N>.tmp` | A file being written; `<N>` is any name. |

Any other entry is not defined here; a process must not read meaning into it.

- Writing: A process creates an entry only by writing it atomically, through a `.tmp` file under a name no other process uses. It must not modify an entry in place; if the entry exists, the rename either fails or replaces it with a file of the same meaning.
- Reading: An existing entry is complete. A reader that knows what an `http-<H>` entry's content must be must check it, and may delete it on mismatch.
- Repairing: When `lip` needs the ZIP archive of a URL, and the `http-<H>` entry of that URL exists and is not a ZIP archive, `lip` deletes the entry and downloads the URL again, once.
- Deleting: An `http-<H>` entry may be deleted at any time; a `.tmp` file, when no process is writing it. `lip cache clean` ([lip-cache(1)](lip-cache.md)) is exempt: it may delete the whole cache directory, and a process writing to the cache then may fail.

## SEE ALSO

[lip-cache(1)](lip-cache.md), [config.toml(5)](config.toml.md), [lip-workspace(7)](lip-workspace.md)

[XDG]: https://specifications.freedesktop.org/basedir-spec/latest/
