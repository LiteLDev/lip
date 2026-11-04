# lip

This document defines the files of the program `lip` that belong to the operating system user rather than to a workspace: the [configuration], the [data directory] and the [cache directory]. The configuration file format is defined by the JSON Schema [`config.schema.json`](config.schema.json).

## Conventions

The key words "MUST", "MUST NOT", "REQUIRED", "SHOULD", "SHOULD NOT", "RECOMMENDED", "MAY" and "OPTIONAL" are to be interpreted as described in BCP 14 ([RFC 2119], [RFC 8174]) when, and only when, they appear in all capitals.

## 1. Directories

`lip` uses three directories of the operating system user:

| Directory | Windows | Other |
|---|---|---|
| <a id="t-config-directory"></a>**config directory** | `%APPDATA%\lip` | `$XDG_CONFIG_HOME/lip` |
| <a id="t-data-directory"></a>**data directory** | `%LOCALAPPDATA%\lip` | `$XDG_DATA_HOME/lip` |
| <a id="t-cache-directory"></a>**cache directory** | `%LOCALAPPDATA%\lip\Cache` | `$XDG_CACHE_HOME/lip` |

On systems other than Windows, an `XDG_*` variable that is unset, empty or not an absolute path takes its default from the [XDG Base Directory Specification][XDG]: `~/.config`, `~/.local/share` and `~/.cache`.

- The [config directory] holds the [configuration] (§2).
- The [data directory] holds the state `lip` needs to finish or undo an operation on a workspace that was interrupted, and to keep two operations from changing one workspace at the same time. Its contents are not defined by this document. They MUST NOT be changed by anything other than `lip`; deleting them while no `lip` process is running loses only the ability to recover an interrupted operation.
- The [cache directory] holds only data that `lip` can obtain again. Its contents are not defined by this document. It MAY be deleted at any time while no `lip` process is running; `lip cache clean` deletes it.

Besides these directories, `lip` writes only to the workspace it operates on and to paths given to it as arguments. It does not write to the system temporary directory. Commands run by hooks are not bound by this.

## 2. Configuration

<a id="t-configuration"></a>The **configuration** is the file `config.yaml` in the [config directory]. It is a [YAML] mapping of [configuration keys][configuration key] to values, valid against [`config.schema.json`](config.schema.json). A [configuration key] absent from the file has its default value. Keys that are not [configuration keys][configuration key] are ignored. When the file cannot be read, `lip` uses the default values.

<a id="t-configuration-key"></a>The **configuration keys** are:

| Key | Type | Default | Meaning |
|---|---|---|---|
| `github_proxy` | URI prefix | none | The prefix through which `lip` sends requests to GitHub (git repositories and downloads). When a request through it fails, `lip` sends the request to GitHub directly. |

`lip config set` and `lip config delete` rewrite the file, leaving out keys at their default value.

**Earlier formats.** When `config.yaml` does not exist and the file `liprc.json` written by lip v0.x exists in the [config directory], `lip` reads its configuration from `liprc.json`, writes it to `config.yaml` and deletes `liprc.json`. A `liprc.json` that cannot be read is kept, and the default values are used.

[configuration]: #t-configuration
[configuration key]: #t-configuration-key
[config directory]: #t-config-directory
[data directory]: #t-data-directory
[cache directory]: #t-cache-directory

[RFC 2119]: https://www.rfc-editor.org/rfc/rfc2119
[RFC 8174]: https://www.rfc-editor.org/rfc/rfc8174
[XDG]: https://specifications.freedesktop.org/basedir-spec/latest/
[YAML]: https://yaml.org/spec/1.2.2/
