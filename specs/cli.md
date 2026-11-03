# lip Command Line Interface Specification

This specification defines the externally observable behavior of the `lip` command. It uses the terms of the [package specification][PF] and cites its sections as "PF §N". A reference "§N" names section N of this specification.

## Conventions

The key words "MUST", "MUST NOT", "REQUIRED", "SHOULD", "SHOULD NOT", "RECOMMENDED", "MAY" and "OPTIONAL" are to be interpreted as described in BCP 14 ([RFC 2119], [RFC 8174]) when, and only when, they appear in all capitals.

## Terms

Each term below is defined using only the terms before it and the terms of the [package specification][PF].

- <a id="t-command"></a>**command**: an invocation of `lip` with a command name, such as `lip install`. (Not to be confused with a [hook command][PF command].)
- <a id="t-usage-error"></a>**usage error**: an unknown [command] or option, or a missing required argument.
- <a id="t-result"></a>**result**: the data a [command] is asked for. Only `list`, `versions`, `view`, `config get`, `config list` and `pack` without `--output` have a [result].
- <a id="t-message"></a>**message**: any text that is not a [result]: progress, information, warnings, errors and the final success line.
- <a id="t-workspace-operation"></a>**workspace operation**: a [command] that is an [operation][PF operation]: `install`, `uninstall` or `update`.
- <a id="t-rid"></a>**RID lip runs on**: the [runtime][PF runtime] of the process running `lip`.

## 1. Invocation

```
lip [-h | --help] [-v | --version]
lip <command> [arguments] [options]
```

- `-h`, `--help` writes help for `lip`, or for the [command] it follows, and exits with code 0.
- `-v`, `--version` writes the version of `lip`, a [version][PF version], and exits with code 0.
- `lip` without arguments writes help and exits with code 0.
- A [usage error] fails the [command].

### 1.1 Workspace

The [workspace][PF workspace] of every [command] is the current working directory. A relative path in an argument is relative to it.

### 1.2 Output

- A [result] is written to standard output.
- A [message] is written to standard error.
- With `--json`, the [result] is written to standard output as JSON ([RFC 8259]) of the shape the [command] defines, and nothing else is written to standard output. Without `--json`, the layout of a [result] is meant for people and is not specified.

### 1.3 Exit Status

A [command] that succeeds exits with code 0. A [command] that fails, including on a [usage error], writes a [message] describing the cause and exits with a non-zero code.

## 2. Package Arguments

<a id="t-package-argument"></a>A **package argument** is an argument of `install`, `update` or `view` that names a [request][PF request]. It is classified by the first of these rules that applies:

1. <a id="t-url-argument"></a>**URL argument**: it starts with `https://`. It is a [url ref][PF url ref]. An argument with any other URI scheme is invalid.
2. <a id="t-local-file-argument"></a>**Local file argument**: let P be the argument without the part from its last `#` on, or the whole argument when the whole argument names an existing path. P starts with `./`, `../`, `.\` or `..\`; or P is `.` or `..`; or P is an absolute path; or the argument contains no `@` and P either ends with `.mcmod` or `.zip` ([ASCII case-insensitively][PF ASCII case-insensitive]) or names an existing file or directory. The argument is the [local file ref][PF local file ref] of the absolute form of P, with the [variant][PF variant] after the last `#`, if any.
   - When P names a directory, the argument is invalid.
   - When P does not exist, the argument is invalid.
3. <a id="t-package-path"></a>**Package path**: any other argument, of the form `spec [ "#" variant ] [ "@" ( version / git-ref ) ]`, `spec` being the [target][PF target] of a [spec ref][PF spec ref].
   - Without `@`, the [range][PF range] of the [request][PF request] matches every [version][PF version]; the highest is preferred (PF §5.2, §6).
   - With `@<version>`, `<version>` being a [version][PF version], the [range][PF range] matches exactly that [version][PF version].
   - With `@branch:<name>`, `@tag:<name>` or `@rev:<commit>`, the argument is a [pinned spec ref][PF pinned spec ref].
   - With anything else after `@`, the argument is invalid.

An invalid [package argument] fails the [command] before the [workspace][PF workspace] changes.

## 3. Environment

`lip` manages only [workspaces][PF workspace] whose [platform][PF platform] is `"server"`. The [environment][PF environment] (PF §5.1) of every [workspace operation] is:

- [platform][PF platform]: `"server"`, as if specified explicitly by the [user][PF user]. When the [lock][PF lock] records another [platform][PF platform], the [workspace operation] fails.
- [runtime][PF runtime]: the `--rid` option when given; otherwise the [runtime][PF runtime] the [lock][PF lock] records; otherwise the [RID lip runs on][rid].

## 4. Workspace Operations

Each [workspace operation] is one [operation][PF operation] (PF §7): it succeeds or fails as a whole, and when it succeeds the [lock][PF lock] (PF §9) records its result.

<a id="t-common-options"></a>The **common options** of a [workspace operation] are:

| Option | Behavior |
|---|---|
| `-n`, `--dry-run` | Performs every step that changes nothing, and reports the changes the [operation][PF operation] would make: the [packages][PF package] it would install or uninstall, and those it would mark [explicitly installed][PF explicitly installed]. Neither the [workspace][PF workspace] nor the [lock][PF lock] changes, and no [hook command][PF command] runs. |
| `--ignore-scripts` | No [hook command][PF command] runs: neither those of `hooks` (PF §4.1) nor the scripts of [tooth.json][PF tooth.json] [packages][PF package] (PF §10.2 rule 7). Nothing else changes. |
| `--rid <RID>` | Sets the [runtime][PF runtime] of the [environment][PF environment] (§3) to `<RID>`. |

**Cancellation.** When the [user][PF user] interrupts `lip` (Ctrl+C) while a [workspace operation] is changing the [workspace][PF workspace], the [operation][PF operation] is cancelled and its changes are undone (PF §7 "Atomicity" rule 2). A second interruption ends `lip` at once; the next [workspace operation] then completes or undoes the interrupted one (PF §7 "Atomicity" rule 5). An interruption before the [workspace][PF workspace] starts to change ends `lip` at once.

**Concurrency.** When another `lip` process is running a [workspace operation] on the same [workspace][PF workspace], a [workspace operation] fails at once without changing anything (PF §7 "Atomicity" rule 6). It does not wait.

**Interrupted operations.** When a previous [operation][PF operation] on the [workspace][PF workspace] was interrupted, a [workspace operation] first completes or undoes it, and reports which. When it cannot undo it, the [workspace operation] fails and reports what is left to undo. With `--dry-run`, it only reports that an interrupted [operation][PF operation] exists.

## 5. Commands

### 5.1 `lip install <PACKAGES>... [options]`

Options: the [common options], and `--no-dependencies`.

Without `--no-dependencies`, `lip install` makes a [request][PF request] of each [package argument], and installs the set S of PF §6 for those [requests][PF request] together with the installed [packages][PF package]:

- The requested [packages][PF package] become [explicitly installed][PF explicitly installed]; this includes a requested [package][PF package] that is installed as a [dependency package][PF dependency package].
- An installed [package][PF package] keeps its [version][PF version] when that satisfies every condition of PF §6; otherwise a [dependency package][PF dependency package] may change [version][PF version]. Each [dependency package][PF dependency package] that no [explicitly installed][PF explicitly installed] [package][PF package] needs any more is uninstalled.
- When a requested [package][PF package] is already [explicitly installed][PF explicitly installed], the [command] fails.
- When two [package arguments][package argument] request the same [identity][PF identity], the [command] fails.

With `--no-dependencies`, for each [package argument], `lip` takes the first [candidate][PF candidate] of its [request][PF request] (PF §5.2) and installs exactly that [package][PF package] as [explicitly installed][PF explicitly installed], replacing an installed [package][PF package] of the same [identity][PF identity]. No [install edge][PF install edge] is followed, and no other [package][PF package] is installed or uninstalled. When the installed [package][PF package] has the same [identity][PF identity], [version][PF version], [archive key][PF archive key] and [archive identifier][PF archive identifier] as that [candidate][PF candidate], it is only marked [explicitly installed][PF explicitly installed]. `lip` warns that the [workspace][PF workspace] may be left broken.

### 5.2 `lip uninstall <PACKAGES>... [options]`

Options: the [common options], and `--no-dependencies`.

Each argument names the installed [packages][PF package] that match it by the first of these rules that matches any:

1. Its [identity][PF identity] equals the argument.
2. The first [ref][PF ref] of its [chain][PF chain] (PF §9) equals the argument, with the [target][PF target] compared [ASCII case-insensitively][PF ASCII case-insensitive]. An argument without a [variant][PF variant] matches every [variant][PF variant], and an argument without a [git-ref][PF git-ref] matches every [git-ref][PF git-ref]. A [local file argument] is first converted to its [local file ref][PF local file ref] as in §2.

When an argument names no installed [package][PF package], the [command] fails.

Without `--no-dependencies`:
- Every named [package][PF package] MUST be [explicitly installed][PF explicitly installed]; otherwise the [command] fails.
- The named [packages][PF package] stop being [explicitly installed][PF explicitly installed]. Each [package][PF package] that no remaining [explicitly installed][PF explicitly installed] [package][PF package] needs is uninstalled. A named [package][PF package] that another [package][PF package] still needs is kept as a [dependency package][PF dependency package], and `lip` reports it.

With `--no-dependencies`, exactly the named [packages][PF package] are uninstalled, [dependency packages][PF dependency package] included, and no other [package][PF package] is. `lip` warns that the [workspace][PF workspace] may be left broken.

### 5.3 `lip update [PACKAGES]... [options]`

Options: the [common options].

With arguments, `lip update` makes a [request][PF request] of each [package argument] again: a [package path] without `@` gives its highest [version][PF version]; with `@<version>`, that [version][PF version]; with a [git-ref][PF git-ref], the commit it names now. Every requested [package][PF package] MUST already be installed, as [explicitly installed][PF explicitly installed] or as a [dependency package][PF dependency package]; otherwise the [command] fails. The requested [packages][PF package] become [explicitly installed][PF explicitly installed].

Without arguments, `lip update` updates every [explicitly installed][PF explicitly installed] [package][PF package] by the first [ref][PF ref] of its [chain][PF chain] (PF §9 "Updating"). When that [ref][PF ref] is a [local file ref][PF local file ref] whose file no longer exists, the [package][PF package] is kept as it is and `lip` warns.

A [package][PF package] is replaced only when the new [candidate][PF candidate] differs from the installed [package][PF package] (PF §9 "Updating"). [Dependency packages][PF dependency package] are updated as PF §6 requires. A [package][PF package] installed from a [pinned spec ref][PF pinned spec ref] is moved back to [spec versions][PF spec version] by updating it with its [package path] without `@`.

### 5.4 `lip list [--json]`

Writes as its [result] the [packages][PF package] the [lock][PF lock] records, each as `<identity>@<version>`, in [installation order][PF installation order], in two groups: [explicitly installed][PF explicitly installed] [packages][PF package], then [dependency packages][PF dependency package]. It changes nothing.

With `--json`, the [result] is:

```json
{ "ExplicitInstalled": ["<identity>@<version>", ...], "ImplicitInstalled": ["<identity>@<version>", ...] }
```

### 5.5 `lip versions <PACKAGE> [--json]`

`<PACKAGE>` MUST be a [package path] without `@`; otherwise the [command] fails. Its [variant][PF variant] does not affect the [result]. Writes as its [result] the [spec versions][PF spec version] of its [spec ref][PF spec ref], in ascending [SemVer 2.0.0] precedence.

With `--json`, the [result] is a JSON array of strings.

### 5.6 `lip view <PACKAGE>`

`<PACKAGE>` is a [package argument]. Writes as its [result], as JSON, the [root document][PF root document] of the [archive][PF archive] it names. For a [package path], that [archive][PF archive] is the [archive][PF archive] of its highest [spec version][PF spec version], of the given [version][PF version], or of the commit its [git-ref][PF git-ref] names. A [meta document][PF meta document] is written as it is and not followed. It changes nothing.

### 5.7 `lip init`

Writes `manifest.json` in the current directory: a [package document][PF package document] of a [mod][PF mod] with:

- `format`: `"bf45b1"`;
- `id`: the name of the current directory, with every character other than an ASCII letter, an ASCII digit, `.`, `_` and `-` replaced by `-`;
- `version`: `"0.1.0"`;
- `dependencies`: `{}`;
- `mod`: `{ "loader": "native", "entry": "<id>.dll" }`, `<id>` being the value of `id`.

When `manifest.json` exists, or the value of `id` is not an [id][PF id], the [command] fails and writes nothing.

### 5.8 `lip migrate`

Converts the [root document][PF root document] of the current directory to a [document][PF document] of format `bf45b1` (PF §4) in `manifest.json`: from `tooth.json` when it exists, and otherwise from a [LeviLamina manifest][PF LeviLamina manifest] in `manifest.json`. The output is written with an indentation of two spaces.

<a id="t-question"></a>**Questions.** When a value the output needs is missing or invalid, `lip` asks the [user][PF user] for it on the terminal, again until the answer is valid. When standard input or standard error is not a terminal, the [command] fails instead, naming the value it would ask for, and changes nothing.

**Warnings.** `lip` warns about every value it drops or converts to another meaning, naming the member it comes from. Warnings do not fail the [command].

**From tooth.json.** A [tooth.json][PF tooth.json] of format 1 or 2 is first migrated to format 3 (PF §10.2). When `manifest.json` also exists, the [command] fails and changes nothing. `tooth.json` is deleted after `manifest.json` has been written. [Labels][PF label] and the [tooth name][PF tooth name] are as in PF §10.2.

1. The output has: `id`, the [identity][PF identity] PF §10.2 rule 1 gives the [label][PF label] `""` (`lip` asks for it when that is not an [id][PF id]); `version`; and `info` with `name`, `description`, `tags`, and `icon` from `avatar_url`, each only when not empty.
2. When the [tooth.json][PF tooth.json] has exactly one [tooth variant][PF tooth variant], that [tooth variant][PF tooth variant] has the [label][PF label] `""`, all its assets are `self`, and its `platform` is empty or contains none of `*`, `?` and `[`, the output is a [package document][PF package document]: the content of that [tooth variant][PF tooth variant] (rule 4) at its top level, and its `platform`, when not empty, as `runtime`.
3. Otherwise the output is a [meta document][PF meta document] whose [Targets][PF Target] hold the content:
   - For every [tooth variant][PF tooth variant] whose `platform` contains `*`, `?` or `[`, `lip` asks for the [runtimes][PF runtime] it stands for, as a comma-separated list.
   - For every [label][PF label] L, the [runtimes][PF runtime] of L are the non-empty `platform` values of its [tooth variants][PF tooth variant], a pattern standing for the [runtimes][PF runtime] given for it. For every [runtime][PF runtime] R of L, the [tooth variants][PF tooth variant] with [label][PF label] L whose `platform` is empty or stands for R are merged as PF §10.2 rule 2 merges them. [Runtimes][PF runtime] whose merged content is the same make one [Target][PF Target], whose `runtime` is the list of them. When L has [tooth variants][PF tooth variant] with an empty `platform`, they make one more [Target][PF Target] without `runtime`, holding their merged content.
   - A [Target][PF Target] has `variant` L (omitted when L is `""`), the merged content (rule 4), and, when L is not `""`, the `id` PF §10.2 rule 1 gives L.
   - When the [tooth.json][PF tooth.json] has a [tooth variant][PF tooth variant] with the [label][PF label] `client`, every [Target][PF Target] with the `variant` `""` also has `"platform": "server"`, and every [Target][PF Target] with the `variant` `client` is written once more, with its `id`, with the `variant` `""` and `"platform": "client"` (PF §10.2 rule 2).
   - The `source` of a [Target][PF Target] is `https://github.com/<owner>/<repo>/releases/download/v{{version}}/<file>` for a `tooth` path `github.com/<owner>/<repo>[/...]`. For any other `tooth` path, `lip` asks for the URI prefix the [packages][PF package] will be published under, and the `source` is `<prefix>/<file>`. `<file>` is `{{id}}`, followed by `-{{platform}}` when the [Target][PF Target] has `platform`, by `-{{runtime}}` when it has `runtime`, and by `.mcmod`.
4. The content of a [tooth variant][PF tooth variant], or of merged [tooth variants][PF tooth variant]:
   - `dependencies`: every `"<path>[#label]": range` becomes the member whose key is the [identity][PF identity] PF §10.2 rule 1 gives that [package][PF package], and whose value is `{ "version": range, "source": "<path>[#label]" }`. The [tooth range][PF tooth range] is written as a [range][PF range]. A [tooth range][PF tooth range] that cannot be written as one, or whose [range][PF range] then matches other [versions][PF version], is reported; a [tooth range][PF tooth range] that matches every [version][PF version] is omitted.
   - `archives`: every asset other than `self` becomes an [Archive][PF Archive] named `asset<i>`, `<i>` being the index of the asset, with its `urls` as `url` and its type as `format` (`"file"` for `uncompressed`).
   - `files`: every placement becomes a [File][PF File] whose `from` names the [Archive][PF Archive] of its asset (no `from` for a `self` asset). A `dir` placement becomes the [directory form][PF directory form] (`src` `""` for `""` and `"."`, and otherwise `src` followed by `/`). A `file` placement becomes the [file form][PF file form] when its `src` names a file, and the [glob form][PF glob form] when `src` is a pattern; `lip` warns when the [glob form][PF glob form] places files at other paths than [tooth.json][PF tooth.json] did ([tooth.json][PF tooth.json] kept only the file name), checking `self` assets against the files of the current directory. For an `uncompressed` asset, `src` is the name of its [Archive][PF Archive]. `\` in `dest` becomes `/`, and `.` and `..` segments are applied.
   - `preserve` and `remove`: `preserve_files` and `remove_files`, converted as PF §10.2 rule 6.
   - `hooks`: the scripts converted as PF §10.2 rule 7; other scripts are dropped.
   - Empty values are omitted.

**From a LeviLamina manifest.** `manifest.json` is rewritten with `format` and:

| [LeviLamina manifest][PF LeviLamina manifest] member | Becomes |
|---|---|
| `name` | `id` |
| `version` | `version` |
| `platform` | `platform`, when it is a [platform][PF platform] |
| `description` | `info.description` |
| `author` | `info.authors`: `[author]` |
| `type`, `entry`, `passive`, `extraInfo` | `mod.loader`, `mod.entry`, `mod.passive`, `mod.extra` |
| `dependencies` | `dependencies`: each `name` mapped to its `version`, or to `"*"` |
| `conflicts` | `conflicts`: each `name` mapped to its `version`, or to `"*"` |
| `optionalDependencies`, `loadAfter` | `mod.loadAfter`: their `name`s, without duplicates |
| `loadBefore` | `mod.loadBefore`: their `name`s, `__before_all_mods__` becoming `"*"` |

The `version` of entries of `optionalDependencies`, `loadAfter` and `loadBefore` is dropped. In all of these members, an entry whose `name` is not an [id][PF id] is dropped, and a `version` that is not a [range][PF range] becomes `"*"`. Every other member is moved into `mod.extra`. `lip` asks for `id`, `version`, `mod.loader` and `mod.entry` when they would be missing or invalid.

When neither `tooth.json` nor `manifest.json` exists, when `manifest.json` has the member `format`, or when a file cannot be read, the [command] fails and changes nothing.

### 5.9 `lip cache clean`

Deletes all data `lip` has cached. The cache holds only data `lip` can obtain again; installed [packages][PF package], the [lock][PF lock] and the [configuration] are not affected.

### 5.10 `lip config`

```
lip config get <KEY> [--json]
lip config set <KEY> <VALUE>
lip config list [--json]
lip config delete <KEY>
```

- `get` writes as its [result] the value of `<KEY>`. With `--json`, the [result] is the value as a JSON string.
- `set` sets `<KEY>` to `<VALUE>`.
- `list` writes as its [result] every [configuration key] with its value. With `--json`, the [result] is a JSON object that maps each [configuration key] to its value as a string.
- `delete` resets `<KEY>` to its default value.

When `<KEY>` is not a [configuration key], the [command] fails.

### 5.11 `lip pack`

```
lip pack [<DIR>] [--variant <VARIANT>] [--platform <PLATFORM>] [--rid <RID>] [-o, --output <FILE>]
```

Packs the [document][PF document] of format `bf45b1` in `manifest.json` in the current directory into the [package document][PF package document] of one [environment][PF environment] (PF §8):

- For a [meta document][PF meta document], the [Target][PF Target] is selected as in PF §5.2 step 3, for the [variant][PF variant] `--variant` (default `""`) and the [environment][PF environment] of `--platform` (`server` or `client`, default `server`) and `--rid` (default: the [RID lip runs on][rid]). The output is Pack(document, Target, platform, runtime).
- For a [package document][PF package document], `--variant` MUST be `""`, `--platform` and `--rid` are not used, and the output is the [package document][PF package document] itself.

Without `--output`, the output is the [result], written as JSON. With `--output`, `lip` writes a [ZIP archive][PF ZIP archive] to `<FILE>`, replacing it, that holds the output as `manifest.json` at its root, and every regular file below `<DIR>` (default: the current directory) at its path relative to `<DIR>`, except `manifest.json` at the root of `<DIR>`, everything below `.git/` and `.lip/` at the root of `<DIR>`, and `<FILE>` itself.

The [command] fails and writes nothing when `manifest.json` is missing or is not a valid [document][PF document] of format `bf45b1`, when no [Target][PF Target] or more than one is selected, or when packing fails (PF §8).

## 6. Configuration

<a id="t-configuration"></a>The **configuration** belongs to the operating system user, and is stored in the file `config.yaml`:

| Operating system | Path |
|---|---|
| Windows | `%APPDATA%\lip\config.yaml` |
| Other | `$XDG_CONFIG_HOME/lip/config.yaml`; `~/.config/lip/config.yaml` when `XDG_CONFIG_HOME` is not set ([XDG]) |

The file is a [YAML] mapping of [configuration keys][configuration key] to values. A [configuration key] absent from the file has its default value. When the file cannot be read, `lip` reports an error and uses the default values.

<a id="t-configuration-key"></a>The **configuration keys** are:

| Key | Type | Default | Meaning |
|---|---|---|---|
| `github_proxy` | URI prefix | none | The prefix through which `lip` sends requests to GitHub (git repositories and downloads). When a request through it fails, `lip` sends the request to GitHub directly. |

**Earlier formats.** A [configuration] written by an earlier version of `lip` keeps working.

## 7. The Lock

The [lock][PF lock] (PF §9) of a [workspace][PF workspace] is the file `.lip/lock.json` in the [workspace][PF workspace], in the format of [lock.schema.yaml][lock-schema]. It belongs to the directory, not to its path: a [workspace][PF workspace] that is moved, renamed or copied keeps its [lock][PF lock], and so its installed [packages][PF package].

**Earlier formats.** A `tooth_lock.json` that lip v0.x left in the [workspace][PF workspace] keeps working (PF §9 "Earlier formats").

## References

- [PF]: lip Package Specification.
- [RFC 2119], [RFC 8174]: Key words for use in RFCs to Indicate Requirement Levels.
- [RFC 8259]: The JavaScript Object Notation (JSON) Data Interchange Format.
- [SemVer 2.0.0]: Semantic Versioning 2.0.0.
- [XDG]: XDG Base Directory Specification.
- [YAML]: YAML Ain't Markup Language, version 1.2.

[command]: #t-command
[usage error]: #t-usage-error
[result]: #t-result
[message]: #t-message
[workspace operation]: #t-workspace-operation
[rid]: #t-rid
[package argument]: #t-package-argument
[local file argument]: #t-local-file-argument
[package path]: #t-package-path
[common options]: #t-common-options
[configuration]: #t-configuration
[configuration key]: #t-configuration-key

[PF]: package_format.md
[PF ASCII case-insensitive]: package_format.md#t-ascii-ci
[PF archive]: package_format.md#t-archive
[PF ZIP archive]: package_format.md#t-zip-archive
[PF document]: package_format.md#t-document
[PF root document]: package_format.md#t-root-document
[PF LeviLamina manifest]: package_format.md#t-leviLamina-manifest
[PF tooth.json]: package_format.md#t-tooth-json
[PF meta document]: package_format.md#t-meta-document
[PF package document]: package_format.md#t-package-document
[PF package]: package_format.md#t-package
[PF mod]: package_format.md#t-mod
[PF id]: package_format.md#t-id
[PF identity]: package_format.md#t-identity
[PF version]: package_format.md#t-version
[PF range]: package_format.md#t-range
[PF variant]: package_format.md#t-variant
[PF platform]: package_format.md#t-platform
[PF runtime]: package_format.md#t-runtime
[PF environment]: package_format.md#t-environment
[PF workspace]: package_format.md#t-workspace
[PF operation]: package_format.md#t-operation
[PF user]: package_format.md#t-user
[PF ref]: package_format.md#t-ref
[PF target]: package_format.md#t-target
[PF url ref]: package_format.md#t-url-ref
[PF spec ref]: package_format.md#t-spec-ref
[PF spec version]: package_format.md#t-spec-version
[PF pinned spec ref]: package_format.md#t-pinned-spec-ref
[PF git-ref]: package_format.md#t-git-ref
[PF local file ref]: package_format.md#t-local-file-ref
[PF Archive]: package_format.md#t-archive-object
[PF File]: package_format.md#t-file
[PF glob form]: package_format.md#t-glob-form
[PF directory form]: package_format.md#t-directory-form
[PF file form]: package_format.md#t-file-form
[PF Target]: package_format.md#t-target-object
[PF candidate]: package_format.md#t-candidate
[PF chain]: package_format.md#t-chain
[PF archive key]: package_format.md#t-archive-key
[PF request]: package_format.md#t-request
[PF install edge]: package_format.md#t-install-edge
[PF installation order]: package_format.md#t-installation-order
[PF command]: package_format.md#t-command
[PF lock]: package_format.md#t-lock
[PF archive identifier]: package_format.md#t-archive-identifier
[PF explicitly installed]: package_format.md#t-explicitly-installed
[PF dependency package]: package_format.md#t-dependency-package
[PF label]: package_format.md#t-label
[PF tooth name]: package_format.md#t-tooth-name
[PF tooth variant]: package_format.md#t-tooth-variant
[PF tooth range]: package_format.md#t-tooth-range
[lock-schema]: schemas/lock.schema.yaml

[RFC 2119]: https://www.rfc-editor.org/rfc/rfc2119
[RFC 8174]: https://www.rfc-editor.org/rfc/rfc8174
[RFC 8259]: https://www.rfc-editor.org/rfc/rfc8259
[SemVer 2.0.0]: https://semver.org/spec/v2.0.0.html
[XDG]: https://specifications.freedesktop.org/basedir-spec/latest/
[YAML]: https://yaml.org/spec/1.2.2/
