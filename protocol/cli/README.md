# CLI

This document defines the command line interface of the program `lip`: its [commands][command], their arguments and options, what they write to the standard streams, and which of their effects on [workspaces][workspace], the [configuration] and the file system are defined by the other documents of this protocol. Terms written as links into [`../package/README.md`][P], [`../workspace/README.md`][W] and [`../userspace/README.md`][U] are defined there.

## Conventions

The key words "MUST", "MUST NOT", "REQUIRED", "SHOULD", "SHOULD NOT", "RECOMMENDED", "MAY" and "OPTIONAL" are to be interpreted as described in BCP 14 ([RFC 2119], [RFC 8174]) when, and only when, they appear in all capitals.

## 1. Terms

- <a id="t-command"></a>**command**: an invocation of `lip` with a command name, such as `lip install`. (Not to be confused with a [hook command][hook command].)
- <a id="t-usage-error"></a>**usage error**: an unknown [command] or option, or a missing required argument.
- <a id="t-result"></a>**result**: the data a [command] is asked for. Only `list`, `versions`, `view`, `config get`, `config list` and `pack` without `--output` have a [result].
- <a id="t-message"></a>**message**: any text that is not a [result]: progress, information, warnings, errors and the final success line. Their wording is not defined by this document.
- <a id="t-workspace-operation"></a>**workspace operation**: a [command] that is an [operation]: `install`, `uninstall` or `update`.
- <a id="t-rid"></a>**RID lip runs on**: the [runtime] of the process running `lip`.

## 2. Invocation

```
lip [-h | --help] [-v | --version]
lip <command> [arguments] [options]
```

- `-h`, `--help` writes help for `lip`, or for the [command] it follows, and exits with code 0.
- `-v`, `--version` writes the [version] of `lip` and exits with code 0.
- `lip` without arguments writes help and exits with code 0.
- A [usage error] fails the [command]. `lip cache` and `lip config` without a subcommand are [usage errors][usage error].

**Workspace.** The [workspace] of every [command] is the current working directory. A relative path in an argument is relative to it.

**Output.**
- A [result] is written to standard output.
- A [message] is written to standard error.
- `lip rpc` writes to standard output only the messages of its protocol (§5.13).
- With `--json`, the [result] is written to standard output as JSON ([RFC 8259]) of the shape the [command] defines, and nothing else is written to standard output. Without `--json`, the layout of a [result] is meant for people and is not defined. A [result] that is JSON by definition (`view`, `pack`) is written as such with no other text on standard output.

**Exit status.** A [command] that succeeds exits with code 0. A [command] that fails, including on a [usage error], writes a [message] describing the cause and exits with a non-zero code.

## 3. Package Arguments

<a id="t-package-argument"></a>A **package argument** is an argument of `install`, `update` or `view` that names a [source]. It is classified by the first of these rules that applies:

1. **URL.** It starts with `https://`. It is a [URL][url source]. An argument with any other URI scheme is invalid.
2. **Local path.** Let L be the argument without the part from its last `#` on, or the whole argument when the whole argument names an existing path. The argument is a local path when L starts with `./`, `../`, `.\` or `..\`; or L is `.` or `..`; or L is an absolute path; or the argument contains no `@` and L either ends with `.mcmod` or `.zip` ([ASCII case-insensitively][ASCII case-insensitive]) or names an existing file or directory. The argument is the [source] made of the absolute form of L, followed by the part from the last `#`, if any. When L does not exist, the argument is invalid.
3. **Remote repository.** Any other argument is the [remote repository] [source] of that string: `repo [ "#" variant ] [ "@" ref ]`.

A [source] that this rule gives is subject to the rules of [`../package/README.md` §2][source]; a string that is not a valid [source] makes the argument invalid. An invalid [package argument] fails the [command] before the [workspace] changes.

A [package argument] is made into the [request] ([source], [range]) with the [range] that matches every [version]: the [ref] and the [variant] of the [source] select the [candidates][candidate], not the [range].

## 4. Environment and Workspace Operations

`lip` manages only [workspaces][workspace] whose [platform] is `"server"`. The [environment] of every [workspace operation] is:

- [platform]: `"server"`. When the [lock] records another [platform], the [workspace operation] fails.
- [runtime]: the `--rid` option when given; otherwise the [runtime] the [lock] records; otherwise the [RID lip runs on][rid].

A [workspace] without a recorded [environment] thus gets the one above from its first [workspace operation].

Each [workspace operation] is one [operation]: it succeeds or fails as a whole (W §6), and when it succeeds the [lock] records its result.

<a id="t-common-options"></a>The **common options** of a [workspace operation] are:

| Option | Behavior |
|---|---|
| `-n`, `--dry-run` | Performs every step that changes nothing, and reports as [messages][message] the changes the [operation] would make: the [packages][package] it would install or uninstall, and those it would mark [explicitly installed][explicitly installed]. Neither the [workspace] nor the [lock] changes, and no [hook command][hook command] runs. |
| `--ignore-scripts` | No [hook command][hook command] runs. Nothing else changes. |
| `--rid <RID>` | Sets the [runtime] of the [environment] to `<RID>`. |

**Cancellation.** When the user interrupts `lip` (Ctrl+C) while a [workspace operation] is changing the [workspace], the [operation] is cancelled and its changes are undone (W §6 rule 2). A second interruption ends `lip` at once; the next [operation] on the [workspace] then completes or undoes the interrupted one (W §6 rule 5). An interruption before the [workspace] starts to change ends `lip` at once.

**Concurrency.** When another `lip` process is running a [workspace operation] on the same [workspace], a [workspace operation] fails at once without changing anything (W §6 rule 6). It does not wait.

## 5. Commands

### 5.1 `lip install <PACKAGES>... [options]`

Options: the [common options], and `--no-dependencies`.

Without `--no-dependencies`, `lip install` makes a [request] of each [package argument], and installs a set S of [packages][package] that satisfies W §3 for those [requests][request] together with the [requests][request] that made the installed [explicitly installed][explicitly installed] [packages][package]:

- The requested [packages][package] become [explicitly installed][explicitly installed]; this includes a requested [package][package] that is installed as a dependency package.
- An installed [package] keeps its [version] when that satisfies every condition of W §3; otherwise a dependency package may change [version]. Each dependency package that no [explicitly installed][explicitly installed] [package] needs any more is uninstalled.
- When a requested [package] is already [explicitly installed][explicitly installed], the [command] fails.
- When two [package arguments][package argument] request the same [identity], the [command] fails.

With `--no-dependencies`, for each [package argument], `lip` takes the first [candidate] of its [request] and installs exactly that [package] as [explicitly installed][explicitly installed], replacing an installed [package] of the same [identity]. No [install edge] is followed, and no other [package] is installed or uninstalled. When the installed [package] has the same [identity], version and [archive identifier] as that [candidate], it is only marked [explicitly installed][explicitly installed]. W §3 may no longer hold afterwards.

### 5.2 `lip uninstall <PACKAGES>... [options]`

Options: the [common options], and `--no-dependencies`.

Each argument names the installed [packages][package] that match it by the first of these rules that matches any:

1. Its [identity] equals the argument.
2. The first [source] of its [chain] equals the argument made into a [source] as in §3, with the [target] compared [ASCII case-insensitively][ASCII case-insensitive]. An argument without a [variant] matches every [variant], and an argument without a [ref] matches every [ref].

When an argument names no installed [package], the [command] fails.

Without `--no-dependencies`:
- Every named [package] MUST be [explicitly installed][explicitly installed]; otherwise the [command] fails.
- The named [packages][package] stop being [explicitly installed][explicitly installed]. Each [package] that no remaining [explicitly installed][explicitly installed] [package] needs is uninstalled. A named [package] that another [package] still needs is kept as a dependency package.

With `--no-dependencies`, exactly the named [packages][package] are uninstalled, dependency packages included, and no other [package] is. W §3 may no longer hold afterwards.

### 5.3 `lip update [PACKAGES]... [options]`

Options: the [common options].

With arguments, `lip update` makes a [request] of each [package argument] again, as W §5 "Updating" defines it for the [source]. Every requested [package] MUST already be installed, as [explicitly installed][explicitly installed] or as a dependency package; otherwise the [command] fails. The requested [packages][package] become [explicitly installed][explicitly installed].

Without arguments, `lip update` updates every [explicitly installed][explicitly installed] [package] as W §5 "Updating" defines it. When the first [source] of its [chain] is a local path that no longer exists, the [package] is kept as it is.

A [package] is replaced only as W §5 "Updating" defines. Dependency packages are updated as far as W §3 requires. A [package] installed from a [source] with a [ref] is moved back to the [version tags][version tag] by updating it with its [source] without a [ref].

### 5.4 `lip list [--json]`

Writes as its [result] the [packages][package] the [lock] records, each as `<identity>@<version>`, in [installation order][installation order], in two groups: [explicitly installed][explicitly installed] [packages][package], then dependency packages. It changes nothing.

With `--json`, the [result] is:

```json
{ "ExplicitInstalled": ["<identity>@<version>", ...], "ImplicitInstalled": ["<identity>@<version>", ...] }
```

### 5.5 `lip versions <PACKAGE> [--json]`

`<PACKAGE>` MUST be a [remote repository] [package argument] without a [ref]; otherwise the [command] fails. Its [variant] does not affect the [result]. Writes as its [result] the [versions][version] of the [version tags][version tag] of the repository, in ascending [SemVer 2.0.0] precedence.

With `--json`, the [result] is a JSON array of strings.

### 5.6 `lip view <PACKAGE>`

`<PACKAGE>` is a [package argument]. Writes as its [result], as JSON, the [manifest] of the first [archive] that its [source] names (P §4 step 1): for a [remote repository] without a [ref], that of the highest [version tag]; for one with a [ref], that of the commit the [ref] selects. A [meta package] is written as it is and its `targets` are not followed. It changes nothing.

### 5.7 `lip init`

Writes `manifest.json` in the current directory: a [manifest] of a [mod] with:

- `format`: `"bf45b1"`;
- `id`: the name of the current directory, with every character other than an ASCII letter, an ASCII digit, `.`, `_` and `-` replaced by `-`;
- `version`: `"0.1.0"`;
- `dependencies`: `{}`;
- `mod`: `{ "loader": "native", "entry": "<id>.dll" }`, `<id>` being the value of `id`.

When `manifest.json` exists, or the value of `id` is not an [id], the [command] fails and writes nothing.

### 5.8 `lip migrate`

Converts the package repository in the current directory, which holds a `tooth.json` of lip v0.x or a LeviLamina `manifest.json` without the key `format`, to a `manifest.json` with `"format": "bf45b1"`, written with an indentation of two spaces.

- When `tooth.json` exists, it is the source of the conversion. When `manifest.json` also exists, the [command] fails and changes nothing. `tooth.json` is deleted after `manifest.json` has been written.
- Otherwise the `manifest.json` is the source of the conversion, and is rewritten in place.
- When neither exists, when `manifest.json` has the key `format`, or when a file cannot be read, the [command] fails and changes nothing.

The mapping of the members of the source to those of the output is not defined by this document. When a value the output needs is missing or invalid, `lip` asks the user for it on the terminal, again until the answer is valid. When standard input or standard error is not a terminal, the [command] fails instead, naming the value it would ask for, and changes nothing.

### 5.9 `lip cache clean`

Deletes the contents of the [cache directory]. Installed [packages][package], the [lock] and the [configuration] are not affected.

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

When `<KEY>` is not a [configuration key], the [command] fails. `set` and `delete` change the [configuration] as U §2 defines.

### 5.11 `lip pack`

```
lip pack [<DIR>] [--variant <VARIANT>] [--platform <PLATFORM>] [--rid <RID>] [-o, --output <FILE>]
```

Packs the `manifest.json` in the current directory into the [manifest] of a [concrete package] for one [environment]. `--variant` defaults to `""`, `--platform` (`server` or `client`) to `server`, and `--rid` to the [RID lip runs on][rid]; together, `--platform` and `--rid` are the [environment].

- For a [meta package], the target selected is the one P §4 step 2 selects for the [variant] and the [environment]. The output is the [manifest] without the key `targets`, and with, for each key of the target other than `variant`, `platform`, `runtime` and `source`, that key replaced by the value in the target, or deleted when the value is `null`. When the target has `platform`, the output has `platform` equal to the [platform] of the [environment]; when it has `runtime`, the output has `runtime` equal to the [runtime] of the [environment].
- For a [concrete package], `--variant` MUST be `""`, `--platform` and `--rid` are not used, and the output is the [manifest] itself.

Without `--output`, the output is the [result], written as JSON. With `--output`, `lip` writes a [ZIP archive] to `<FILE>`, replacing it, that holds the output as `manifest.json` at its root, and every regular file below `<DIR>` (default: the current directory) at its path relative to `<DIR>`, except `manifest.json` at the root of `<DIR>`, everything below `.git/` and `.lip/` at the root of `<DIR>`, and `<FILE>` itself.

The [command] fails and writes nothing when `manifest.json` is missing or is not a valid [manifest], when no target or more than one is selected, or when the output is not a valid [manifest] of a [concrete package].

### 5.12 `lip rpc`

Serves the [commands][command] over [JSON-RPC 2.0] on standard input and standard output, with messages framed by `Content-Length` headers as in the [Language Server Protocol][LSP framing]. Standard output carries nothing else (§2).

The server has one method for each [command] other than `pack` and `rpc`, named as the [command] in PascalCase without spaces (`Install`, `Uninstall`, `Update`, `List`, `Versions`, `View`, `Init`, `Migrate`, `CacheClean`, `ConfigGet`, `ConfigSet`, `ConfigList`, `ConfigDelete`). Its parameters are the arguments and options of the [command] in the order of its synopsis, by position or by name; its result is the [result] of the [command] (`List` as an array of the two groups of §5.4, [explicitly installed][explicitly installed] first), or `null` when the [command] has none:

- `Install`: `packages`, `dryRun`, `ignoreScripts`, `noDependencies`, `rid`.
- `Uninstall`: `packages`, `dryRun`, `ignoreScripts`, `noDependencies`, `rid`.
- `Update`: `packages`, `dryRun`, `ignoreScripts`, `rid`.
- `Versions`, `View`: `package`.
- `ConfigGet`, `ConfigDelete`: `key`. `ConfigSet`: `key`, `value`.

`rid` is optional: `null`, `""` or its omission means the [runtime] is determined as in §4. The `packages` of `Update` MAY be empty.

A method that fails responds with a JSON-RPC error whose `message` describes the cause, and leaves the [workspace] as the failing [command] would. `Migrate` asks no questions: where `lip migrate` would ask, the method fails, naming the value. The client MAY cancel a pending request with the notification `$/cancelRequest` `{ "id": <request id> }`; a cancelled `Install`, `Uninstall` or `Update` undoes its changes and responds with an error. Requests MAY be sent before earlier ones are answered, but a [workspace operation] that finds another one running on the same [workspace] fails at once.

The server sends the [messages][message] to the client as requests, which the client answers with `null`: `PrintInfo(message)`, `PrintSuccess(message)`, `PrintWarning(message)`, `PrintError(message)` and `ReportProgress(id, message, percentage)`.

[P]: ../package/README.md
[W]: ../workspace/README.md
[U]: ../userspace/README.md
[package]: ../package/README.md#t-package
[manifest]: ../package/README.md#t-manifest
[meta package]: ../package/README.md#t-meta-package
[concrete package]: ../package/README.md#t-concrete-package
[ZIP archive]: ../package/README.md#t-zip-archive
[archive]: ../package/README.md#t-archive
[ASCII case-insensitive]: ../package/README.md#t-ascii-ci
[id]: ../package/README.md#t-id
[version]: ../package/README.md#t-version
[range]: ../package/README.md#t-range
[variant]: ../package/README.md#t-variant
[platform]: ../package/README.md#t-platform
[runtime]: ../package/README.md#t-runtime
[environment]: ../package/README.md#t-environment
[source]: ../package/README.md#2-sources
[target]: ../package/README.md#t-target
[remote repository]: ../package/README.md#t-remote-repository
[url source]: ../package/README.md#t-url-source
[version tag]: ../package/README.md#t-version-tag
[ref]: ../package/README.md#t-ref
[candidate]: ../package/README.md#t-candidate
[chain]: ../package/README.md#t-chain
[workspace]: ../workspace/README.md#t-workspace
[mod]: ../workspace/README.md#t-mod
[identity]: ../workspace/README.md#t-identity
[operation]: ../workspace/README.md#t-operation
[request]: ../workspace/README.md#t-request
[requests]: ../workspace/README.md#t-request
[install edge]: ../workspace/README.md#t-install-edge
[hook command]: ../workspace/README.md#t-command
[lock]: ../workspace/README.md#t-lock
[explicitly installed]: ../workspace/README.md#t-explicitly-installed
[archive identifier]: ../workspace/README.md#t-archive-identifier
[installation order]: ../workspace/README.md#t-installation-order
[configuration]: ../userspace/README.md#t-configuration
[configuration key]: ../userspace/README.md#t-configuration-key
[cache directory]: ../userspace/README.md#t-cache-directory

[command]: #t-command
[usage error]: #t-usage-error
[result]: #t-result
[message]: #t-message
[workspace operation]: #t-workspace-operation
[rid]: #t-rid
[package argument]: #t-package-argument
[common options]: #t-common-options

[RFC 2119]: https://www.rfc-editor.org/rfc/rfc2119
[RFC 8174]: https://www.rfc-editor.org/rfc/rfc8174
[RFC 8259]: https://www.rfc-editor.org/rfc/rfc8259
[JSON-RPC 2.0]: https://www.jsonrpc.org/specification
[LSP framing]: https://microsoft.github.io/language-server-protocol/specifications/lsp/3.17/specification/#headerPart
[SemVer 2.0.0]: https://semver.org/spec/v2.0.0.html
