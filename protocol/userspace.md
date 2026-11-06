# Userspace

## 1. Directories

| Directory | Windows | Other |
|---|---|---|
| **config directory** | `%APPDATA%\lip` | `$XDG_CONFIG_HOME/lip` |
| **cache directory** | `%LOCALAPPDATA%\lip\Cache` | `$XDG_CACHE_HOME/lip` |

On systems other than Windows, an `XDG_*` variable that is unset, empty or not an absolute path takes its [XDG] default.

Besides these directories, `lip` writes only to the workspace it operates on and to paths given as arguments, never to the system temporary directory. Hook commands are exempt.

## 2. Configuration

The **configuration** is `config.yaml` in the config directory: a [YAML] mapping of configuration keys to values, valid against `config.schema.json`. Absent keys take their default; keys that are not configuration keys are ignored. If the file is missing or unreadable, every key takes its default.

The only **configuration key** is `github_proxy`: a URI prefix, default none, through which `lip` sends network requests to GitHub (git repositories and downloads); a request that fails through it is sent to GitHub directly.

`lip config set` and `lip config delete` write the file atomically, leaving out keys at their default.

## 3. Cache

Every process using the cache directory, `lip` or not, follows this section; other processes MAY read and write it.

Entries.

| Entry | Content |
|---|---|
| `http-<H>` | A regular file: the body of a successful response to an HTTPS GET of URL U, following redirects. `<H>` is the lowercase hexadecimal SHA-256 of U in UTF-8; U is the URL as a source or tooth file writes it, without its fragment, whatever proxy was used. |
| `<N>.tmp` | A file being written; `<N>` is any name. |

Any other entry is not defined here; a process MUST NOT read meaning into it.

Writing. A process creates an entry only by writing it atomically, through a `.tmp` file under a name no other process uses. It MUST NOT modify an entry in place; if the entry exists, the rename either fails or replaces it with a file of the same meaning.

Reading. An existing entry is complete. A reader that knows what an `http-<H>` entry's content must be MUST check it, and MAY delete it on mismatch.

Deleting. An `http-<H>` entry MAY be deleted at any time; a `.tmp` file, when no process is writing it.
