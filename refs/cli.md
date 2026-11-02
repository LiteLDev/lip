# lip Command Line Interface Specification

This specification defines the observable behavior of the `lip` command. Package formats, resolution, the dependency graph, installation, uninstallation and the lock are defined by the [package specification](package_format.md), cited below as "PF §N". "must / must not / may / should" correspond to MUST / MUST NOT / MAY / SHOULD of RFC 2119.

## 1. Invocation

```
lip [-h | --help] [-v | --version]
lip <command> [arguments] [options]
```

- `-h`, `--help` prints help for `lip` or for the command it follows, and exits with code 0.
- `-v`, `--version` prints the version of lip (SemVer 2.0.0) and exits with code 0.
- `lip` without arguments prints help and exits with code 0.
- An unknown command or option, or a missing required argument, is a usage error.

### 1.1 Workspace

The **workspace** (PF §1) of every command is the current working directory. Relative paths in arguments are relative to it.

### 1.2 Output

- **Results** — the data a command is asked for (`list`, `versions`, `view`, `config get`, `config list`) — are written to standard output.
- **Messages** — progress, information, warnings, errors and the final success line — are written to standard error.
- With `--json`, the result is written to standard output as JSON in the shape the command defines, and nothing else is written to standard output. Without `--json`, the layout of a result is meant for people and is not part of this specification.

### 1.3 Exit Status

A command exits with code 0 when it succeeds and with a non-zero code when it fails, including on a usage error. On failure lip writes a message describing the cause to standard error.

## 2. Package Arguments

`install`, `update` and `view` take **package arguments**. Each argument is classified by the first rule below that applies:

1. **URL** — starts with `https://`: `url [ "#" variant ]`, a url ref (PF §3). Any other scheme is not accepted.
2. **Local file** — the argument (after removing a trailing `#variant`, unless the whole argument names an existing path) starts with `./`, `../`, `.\` or `..\`, is `.` or `..`, is an absolute path, or contains no `@` and either ends with `.mcmod` or `.zip` (ignoring case) or names an existing file or directory. It names a local file request (PF §6), with the variant after the last `#`.
   - When the path names a directory, the argument is invalid: a local directory is not an archive.
   - When the path does not exist, the argument is invalid.
3. **Package path** — anything else: `spec [ "#" variant ] [ "@" ( version / git-ref ) ]`, a spec ref (PF §3).
   - Without `@`, the request has no range: any version, the newest preferred (PF §5.2, §6).
   - With `@<version>`, where `<version>` is a SemVer 2.0.0 version without a leading `v`, the range accepts exactly that version.
   - With `@branch:<name>`, `@tag:<name>` or `@rev:<commit>`, the request is a pinned spec ref (PF §3).
   - Anything else after `@` makes the argument invalid.

An invalid argument fails the command before the workspace is changed.

## 3. Environment

lip manages server workspaces only. The environment (PF §5.1) of every workspace operation is:

- `platform`: always `"server"`, as if explicitly specified by the user. When the lock records another platform, the operation fails.
- `runtime`: the `--rid` option when given, otherwise the value recorded in the lock, otherwise the RID lip itself runs on.

Client workspaces are out of scope of lip.

## 4. Workspace Operations

`install`, `uninstall` and `update` are **workspace operations**. Each is one operation in the sense of PF §7: it succeeds or fails as a whole, and on success the lock (PF §9) records the result.

Common options:

| Option | Behavior |
|---|---|
| `-n`, `--dry-run` | Performs every step that does not change anything and reports the changes the operation would make (packages it would install, uninstall or mark as explicitly installed). Neither the workspace nor the lock changes, and no command (PF §7) runs. |
| `--ignore-scripts` | Commands (`hooks`, PF §4.1, and the scripts of tooth.json packages, PF §10.2) are not run. Everything else is unchanged. |
| `--rid <RID>` | Sets the `runtime` of the environment (§3). |

**Cancellation**: when the user interrupts lip (Ctrl+C) while an operation is changing the workspace, the operation is cancelled and its changes are undone (PF §7, "Atomicity" 2). A second interruption ends lip at once; the next workspace operation then undoes or completes the interrupted one (PF §7, "Atomicity" 5). Before the workspace starts to change, an interruption ends lip at once.

**Concurrency**: when another lip process is running a workspace operation on the same workspace, a workspace operation fails at once without changing anything (PF §7, "Atomicity" 6). It does not wait.

**Interrupted operations**: when a previous operation on the workspace was interrupted, a workspace operation first completes or undoes it and reports what it did. When it cannot undo it, the operation fails and says what is left to undo. A dry run only reports that an interrupted operation exists.

**Explicit packages**: the lock marks each installed package as **explicitly installed** (requested by the user) or as a **dependency**.

## 5. Commands

### 5.1 `lip install <PACKAGES>... [options]`

Options: the common options of §4, and `--no-dependencies`.

Installs the packages the arguments (§2) request, together with their dependencies, by finding a set S (PF §6) that contains the installed packages and satisfies the requests.

- The requested packages become explicitly installed; a package already installed as a dependency becomes explicitly installed.
- Installed packages are kept at the version they have when that satisfies every condition of PF §6; otherwise dependencies may change version. Packages no explicitly installed package needs any more are uninstalled.
- When a requested package is already explicitly installed, the command fails.
- When two arguments request the same identity, the command fails.

With `--no-dependencies`, for each argument lip takes the first candidate of its request (PF §5.2) and installs exactly that package as explicitly installed, replacing an installed package of the same identity; no install edges are followed and no other package is installed or uninstalled. When the installed package already is that candidate (same identity, version, archive key and archive identifier), it is only marked as explicitly installed. lip warns that the workspace may be left broken.

### 5.2 `lip uninstall <PACKAGES>... [options]`

Options: the common options of §4, and `--no-dependencies`.

Each argument names installed packages by one of:

1. an identity, compared ignoring ASCII case;
2. otherwise, the first ref of the resolution chain the package was installed from (PF §9), compared ignoring ASCII case in the target: a ref without a variant matches every variant, and a ref without a git-ref matches every git-ref. A local file path is resolved as in §2.

When an argument names no installed package, the command fails.

Without `--no-dependencies`:
- Every named package must be explicitly installed; otherwise the command fails.
- The named packages stop being explicitly installed. Every package that no remaining explicitly installed package needs is uninstalled. A named package that other packages still need is kept as a dependency, and lip reports it.

With `--no-dependencies`, exactly the named packages are uninstalled, dependencies included, and no other package is; lip warns that the workspace may be left broken.

### 5.3 `lip update [PACKAGES]... [options]`

Options: the common options of §4.

With arguments, each argument (§2) is requested again: without `@`, its newest version; with `@<version>`, that version; with a git-ref, the commit it names now. Every requested package must already be installed (as explicitly installed or as a dependency); otherwise the command fails. The requested packages become explicitly installed.

Without arguments, every explicitly installed package is requested again by the first ref of its resolution chain, as defined in PF §9 ("Updating"). When that ref is a local file that no longer exists, the package is kept as it is and lip warns.

A package is replaced only when the new candidate differs from the installed one (PF §9). Dependencies are updated as the dependency graph requires. A package installed from a git-ref is moved back to released versions by updating it with its package path without `@`.

### 5.4 `lip list [--json]`

Lists the packages of the workspace lock as `<identity>@<version>`, in installation order, in two groups: explicitly installed packages and dependencies. It changes nothing.

With `--json`, the result is:

```json
{ "ExplicitInstalled": ["<identity>@<version>", ...], "ImplicitInstalled": ["<identity>@<version>", ...] }
```

### 5.5 `lip versions <PACKAGE> [--json]`

`<PACKAGE>` must be a package path (`spec [ "#" variant ]`) without `@`; otherwise the command fails. The variant does not affect the result. Lists the versions of the spec (PF §3: its `v<SemVer 2.0.0>` tags), without the leading `v`, in ascending SemVer precedence.

With `--json`, the result is a JSON array of strings.

### 5.6 `lip view <PACKAGE>`

`<PACKAGE>` is a package argument (§2). Writes the root document of the archive it points to as JSON: `tooth.json` when the archive has one, otherwise `manifest.json`. For a package path the archive is that of its newest version, of the given version, or of the commit the git-ref names. A meta document is shown as it is and not followed. It changes nothing.

### 5.7 `lip init`

Writes `manifest.json` in the current directory: a package document (PF §4) describing a mod:

- `format`: `"bf45b1"`;
- `id`: the name of the current directory, with every character other than ASCII letters, digits, `.`, `_` and `-` replaced by `-`;
- `version`: `"0.1.0"`;
- `dependencies`: `{}`;
- `mod`: `{ "loader": "native", "entry": "<id>.dll" }`.

When `manifest.json` already exists, or the resulting `id` is not a valid id (PF §4.1), the command fails and writes nothing.

### 5.8 `lip migrate`

Converts the package repository in the current directory to a `manifest.json` of format `bf45b1` (PF §4): from `tooth.json` (format 1, 2 or 3) when it exists, otherwise from a LeviLamina manifest (PF §10.1) in `manifest.json`. The result is written with an indentation of two spaces.

**Questions**: when a value the result needs is missing or invalid, lip asks for it on the terminal, again until the answer is valid. When standard input or standard error is not a terminal, the command fails instead, naming the value it would ask for, and changes nothing.

**Warnings**: everything that is dropped or converted with another meaning is reported as a warning naming the field it comes from; warnings do not fail the command.

**From tooth.json** (formats 1 and 2 are first migrated to format 3, PF §10.2). When `manifest.json` also exists, the command fails and changes nothing. `tooth.json` is deleted after `manifest.json` has been written.

1. The document has `id`, the identity PF §10.2 rule 1 gives the unlabeled variant (lip asks for it when it is not an id); `version`; and `info` with `name`, `description`, `tags` and, as `icon`, `avatar_url` of the tooth.json, each only when not empty.
2. When the tooth.json has a single variant, it is unlabeled, its assets are all `self`, and its `platform` is empty or contains none of `*`, `?` and `[`, the document is a package document: the content of that variant (rule 4) is written at its top level, and its `platform`, when not empty, as `runtime`.
3. Otherwise the document is a meta document whose Targets hold the content:
   - For every variant whose `platform` contains `*`, `?` or `[`, lip asks for the runtimes (RIDs) it stands for, as a comma-separated list.
   - For every label L, the runtimes of L are the non-empty `platform` values of its variants, a pattern standing for the runtimes given for it. For every runtime R of L, the variants labeled L whose `platform` is empty or stands for R are merged as PF §10.2 rule 2 merges them; runtimes whose merged content is the same make one Target, with `runtime` the list of them. When L has variants with an empty `platform`, they make one more Target, without `runtime`, holding their merged content.
   - A Target has `variant` L (omitted when L is `""`), the merged content (rule 4), and for L other than `""` the `id` PF §10.2 rule 1 gives L.
   - When the tooth.json has a variant labeled `client`, every Target with the variant `""` also has `"platform": "server"`, and every Target with the variant `client` is written once more, keeping its `id`, with the variant `""` and `"platform": "client"` (PF §10.2 rule 2).
   - The `source` of a Target is `https://github.com/<owner>/<repo>/releases/download/v{{version}}/<file>` for a tooth path `github.com/<owner>/<repo>[/...]`; for other tooth paths lip asks for the URL prefix the packages will be published under, and the `source` is `<prefix>/<file>`. `<file>` is `{{id}}`, followed by `-{{platform}}` when the Target has `platform` and by `-{{runtime}}` when it has `runtime`, and `.mcmod`.
4. The content of a (merged) variant:
   - `dependencies`: every `"<path>[#label]": range` becomes the key the identity of that package would have by PF §10.2 rule 1, with `{ "version": range, "source": "<path>[#label]" }`. The range is written in node-semver syntax; a range that cannot be, or that then accepts other versions, is reported, and a range accepting every version is omitted.
   - `archives`: every asset other than `self` becomes an Archive named `asset<i>`, i being its index, with its `urls` as `url` and its type as `format` (`"file"` for `uncompressed`).
   - `files`: every placement becomes a File, with `from` naming the Archive of its asset (none for a `self` asset). A `dir` placement becomes the directory form (`src` `""` for `""` and `"."`, otherwise `src` followed by `/`). A `file` placement becomes the file form when its `src` names a file, and the glob form when `src` is a pattern; lip warns when the glob form places files at other paths than tooth.json did (tooth.json kept only the file name), checking `self` assets against the files of the repository. For an `uncompressed` asset, `src` is the name of its Archive. `\` in `dest` becomes `/`, and `.` and `..` segments are applied.
   - `preserve` and `remove`: `preserve_files` and `remove_files` mapped as PF §10.2 rule 6.
   - `hooks`: the scripts mapped as PF §10.2 rule 7; other scripts are dropped.
   - Empty values are omitted.

**From a LeviLamina manifest**: `manifest.json` is rewritten with `format` and:

| LeviLamina key | Becomes |
|---|---|
| `name` | `id` |
| `version` | `version` |
| `platform` | `platform` when it is `"server"` or `"client"` |
| `description` | `info.description` |
| `author` | `info.authors`: `[author]` |
| `type`, `entry`, `passive`, `extraInfo` | `mod.loader`, `mod.entry`, `mod.passive`, `mod.extra` |
| `dependencies` | `dependencies`: `name` mapped to its `version`, or `"*"` |
| `conflicts` | `conflicts`: `name` mapped to its `version`, or `"*"` |
| `optionalDependencies`, `loadAfter` | `mod.loadAfter`: their `name`s, without duplicates |
| `loadBefore` | `mod.loadBefore`: their `name`s, `__before_all_mods__` becoming `"*"` |

Versions in `optionalDependencies`, `loadAfter` and `loadBefore` are dropped. In all of these keys, entries whose name is not an id are dropped, and a `version` that is not a node-semver range becomes `"*"`. Other keys are moved into `mod.extra`. lip asks for `id`, `version` (SemVer 2.0.0), `mod.loader` and `mod.entry` when they would be missing or invalid.

When neither `tooth.json` nor `manifest.json` exists, when `manifest.json` already has the `format` key, or when a file cannot be read, the command fails and changes nothing.

### 5.9 `lip cache clean`

Deletes all data lip has cached. The cache holds only data lip can obtain again; installed packages, the lock and the configuration are not affected.

### 5.10 `lip config`

```
lip config get <KEY> [--json]
lip config set <KEY> <VALUE>
lip config list [--json]
lip config delete <KEY>
```

- `get` writes the value of the key. With `--json`, it writes the value as a JSON string.
- `set` sets the key to the value.
- `list` writes every key with its value. With `--json`, it writes a JSON object mapping each key to its value as a string.
- `delete` resets the key to its default value.

When `<KEY>` is not a key of §6, the command fails.

### 5.11 `lip pack`

```
lip pack [<DIR>] [--variant <VARIANT>] [--platform <PLATFORM>] [--rid <RID>] [-o, --output <FILE>]
```

Packs the `manifest.json` of format `bf45b1` (PF §4) in the current directory into the package document of one environment (PF §8):

- For a meta document, the Target is selected as in PF §5.2 step 3, for the variant `--variant` (default `""`) and the environment of `--platform` (`server` or `client`, default `server`) and `--rid` (default: the RID lip runs on). The result is Pack(document, Target, platform, runtime).
- For a package document, `--variant` must be `""`, `--platform` and `--rid` are not used, and the result is the document itself.

Without `--output`, the result is written to standard output as JSON. With `--output`, lip writes a ZIP archive to `<FILE>`, replacing it: the result as `manifest.json` at its root, and every regular file below `<DIR>` (default: the current directory) at its path relative to `<DIR>`, except `manifest.json` at the root of `<DIR>`, everything below `.git/` and `.lip/` at the root of `<DIR>`, and `<FILE>` itself.

The command fails, writing nothing, when `manifest.json` is missing, is not a valid document of format `bf45b1`, has no Target or more than one for the variant and environment, or when packing fails (PF §8).

## 6. Configuration

The configuration is per user and is stored in `config.yaml`:

| Platform | Path |
|---|---|
| Windows | `%APPDATA%\lip\config.yaml` |
| Other | `$XDG_CONFIG_HOME/lip/config.yaml`, by default `~/.config/lip/config.yaml` |

The file is a YAML mapping of keys to values. A key absent from the file has its default value. When the file cannot be read, lip reports an error and uses the default values.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `github_proxy` | URL | none | Prefix through which requests to GitHub (git repositories and downloads) are sent. When a request through the proxy fails, lip sends it to GitHub directly. |

**Legacy**: a configuration written by earlier versions of lip keeps working.

## 7. The Lock

The lock of a workspace (PF §9) is the file `.lip/lock.json` in the workspace. It belongs to the directory, not to its path: a workspace that is moved, renamed or copied keeps its lock, and so its installed packages.

**Legacy**: a `tooth_lock.json` that lip v0.x left in the workspace keeps working (PF §9, "Earlier formats").
