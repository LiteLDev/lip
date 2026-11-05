# Userspace

## 1. Directories

| Directory | Windows | Other |
|---|---|---|
| **config directory** | `%APPDATA%\lip` | `$XDG_CONFIG_HOME/lip` |
| **cache directory** | `%LOCALAPPDATA%\lip\Cache` | `$XDG_CACHE_HOME/lip` |

On systems other than Windows, an `XDG_*` variable that is unset, empty or not an absolute path takes its [XDG] default.

Besides these directories, `lip` writes only to the workspace it operates on and to paths given as arguments, never to the system temporary directory. Hook commands are exempt.

Earlier formats. lip v0.36 also kept files in `%LOCALAPPDATA%\lip\workspaces` on Windows and `$XDG_DATA_HOME/lip/workspaces` elsewhere; they are neither read nor deleted.

## 2. Configuration

The **configuration** is `config.yaml` in the config directory: a [YAML] mapping of configuration keys to values, valid against `config.schema.json`. Absent keys take their default; keys that are not configuration keys are ignored. If the file is missing or unreadable, every key takes its default.

The only **configuration key** is `github_proxy`: a URI prefix, default none, through which `lip` sends network requests to GitHub (git repositories and downloads); a request that fails through it is sent to GitHub directly.

`lip config set` and `lip config delete` rewrite the file, leaving out keys at their default.

Earlier formats. If `config.yaml` is missing and the config directory has the `liprc.json` written by lip v0.x, `lip` writes the configuration in it to `config.yaml` and deletes `liprc.json`. An unreadable `liprc.json` is kept, and every key takes its default.

## 3. Cache

Every process using the cache directory, `lip` or not, follows this section; other processes MAY read and write it.

Entries.

| Entry | Content |
|---|---|
| `http-<H>` | A regular file: the body of a successful response to an HTTPS GET of URL U, following redirects. `<H>` is the lowercase hexadecimal SHA-256 of U in UTF-8; U is the URL as a source or manifest writes it, without its fragment, whatever proxy was used. |
| `git-<O>` | A regular file: the content of the git blob with lowercase hexadecimal object id `<O>` [GIT-OBJECTS], without the object header. |
| `.staging` | A directory of files being written. |

Any other entry is not defined here; a process MUST NOT read meaning into it.

Writing. A process creates an entry only by writing the whole file below `.staging` under a name no other process uses, then renaming it to the entry's name. It MUST NOT create a `git-<O>` whose content does not have object id `<O>`, and MUST NOT modify an entry in place; if the entry exists, the rename either fails or replaces it with a file of the same meaning.

Reading. An existing entry is complete. A reader that knows what an `http-<H>` entry's content must be MUST check it, and MAY delete it on mismatch.

Deleting. Any entry MAY be deleted. A file below `.staging` MAY be deleted when no process is writing it.
