# lip Command Line Interface

## Abstract

This document defines the command line interface of the program `lip`: its commands, their arguments and options, what they write to the standard streams, and their effects on workspaces, the configuration and the cache.

## Status of This Memo

This document is part of the lip protocol and is normative.

## Table of Contents

1. [Introduction](#1-introduction)
   1. [Requirements Language](#11-requirements-language)
2. [Terminology](#2-terminology)
3. [Invocation](#3-invocation)
4. [Package Arguments](#4-package-arguments)
5. [Environment and Workspace Operations](#5-environment-and-workspace-operations)
6. [Commands](#6-commands)
   1. [`lip install`](#61-lip-install)
   2. [`lip uninstall`](#62-lip-uninstall)
   3. [`lip update`](#63-lip-update)
   4. [`lip list`](#64-lip-list)
   5. [`lip versions`](#65-lip-versions)
   6. [`lip view`](#66-lip-view)
   7. [`lip init`](#67-lip-init)
   8. [`lip migrate`](#68-lip-migrate)
   9. [`lip cache clean`](#69-lip-cache-clean)
   10. [`lip config`](#610-lip-config)
   11. [`lip pack`](#611-lip-pack)
   12. [`lip rpc`](#612-lip-rpc)
7. [Security Considerations](#7-security-considerations)
8. [IANA Considerations](#8-iana-considerations)
9. [References](#9-references)
   1. [Normative References](#91-normative-references)

## 1. Introduction

`lip` is run as `lip <command> [arguments] [options]` in a workspace [WORKSPACE]. Its commands install, uninstall and update packages [PACKAGE] in that workspace, inspect packages and the workspace, author packages, and read and change the configuration and the cache [USERSPACE]. `lip rpc` serves the same commands to another program.

### 1.1. Requirements Language

The key words "MUST", "MUST NOT", "REQUIRED", "SHALL", "SHALL NOT", "SHOULD", "SHOULD NOT", "RECOMMENDED", "NOT RECOMMENDED", "MAY", and "OPTIONAL" in this document are to be interpreted as described in BCP 14 [RFC2119] [RFC8174] when, and only when, they appear in all capitals, as shown here.

## 2. Terminology

Terms not defined in this document are defined in [PACKAGE], [WORKSPACE] and [USERSPACE].

- **command**: An invocation of `lip` with a command name, such as `lip install`. It is not a command of `hooks` (Section 5 of [WORKSPACE]), which this document calls a **hook command**.
- **usage error**: An unknown command or option, or a missing required argument.
- **result**: The data a command is asked for. Only `list`, `versions`, `view`, `config get`, `config list`, and `pack` without `--output` have a result.
- **message**: Any text that is not a result: progress, information, warnings, errors and the final success line. Its wording is not defined by this document.
- **workspace operation**: A command that is an operation (Section 2 of [WORKSPACE]): `install`, `uninstall` or `update`, without `--dry-run`.
- **RID lip runs on**: The runtime (Section 2 of [PACKAGE]) of the process running `lip`.

## 3. Invocation

```
lip [-h | --help] [-v | --version]
lip <command> [arguments] [options]
```

- `-h`, `--help` writes help for `lip`, or for the command it follows, to standard output, and exits with code 0. The help is a message.
- `-v`, `--version` writes the version of `lip` [SEMVER], followed by a line break, to standard output, and exits with code 0.
- `lip` without arguments writes help and exits with code 0.
- `lip cache` and `lip config` without a subcommand are usage errors.

**Workspace.** The workspace of every command is the current working directory. A relative path in an argument is relative to it.

**Output.**

- A result is written to standard output.
- A message is written to standard error.
- `lip rpc` writes to standard output only the messages of its protocol (Section 6.12).
- With `--json`, the result is written to standard output as JSON [RFC8259] of the shape the command defines, followed by a line break, and nothing else is written to standard output. Without `--json`, the layout of a result is meant for people and is not defined.
- The result of `view` and of `pack` without `--output` is JSON by definition, and is written as with `--json`.

**Exit status.** A command that succeeds exits with code 0. A command that fails, including on a usage error, writes a message describing the cause and exits with a non-zero code.

## 4. Package Arguments

A **package argument** is an argument of `install`, `update`, `uninstall`, `versions` or `view` that names a source (Section 3 of [PACKAGE]). It is classified by the first of these rules that applies:

1. **URL.** The argument starts with `https://`. It is the URL source of that string. An argument that starts with another URI scheme followed by `://`, such as `http://` or `file://`, is invalid.
2. **Local path.** Let L be the argument without the part from its last `#` on; or the whole argument, when the whole argument names an existing file or directory. The argument is a local path when:
   - L starts with `./`, `../`, `.\` or `..\`; or
   - L is `.` or `..`; or
   - L is an absolute path; or
   - the argument contains no `@`, and L ends with `.mcmod` or `.zip` (compared ASCII case-insensitively) or names an existing file or directory.

   The source is the `file:` URI [RFC8089] of the absolute path of L, followed by the part from the last `#`, if any. When L does not exist, the argument is invalid.
3. **Remote repository.** Any other argument is the remote repository source of that string: `repo [ "#" variant ] [ "@" ref ]`.

A string that is not a valid source makes the argument invalid. A command with an invalid package argument fails before anything changes.

A package argument is made into the request (source, range) whose range matches every version: the ref and the variant of the source select the candidates, not the range.

## 5. Environment and Workspace Operations

`lip` manages only workspaces whose platform is `"server"`. The environment of `install`, `uninstall` and `update` is:

- platform: `"server"`. When the workspace file records another platform, the command fails.
- runtime: the value of `--rid` when given; otherwise the runtime the workspace file records; otherwise the RID lip runs on.

A workspace without a recorded environment thus gets the one above from its first workspace operation.

Each workspace operation is one operation, carried out as Section 8.4 of [WORKSPACE] defines: it succeeds or fails as a whole (Section 7 of [WORKSPACE]), and when it succeeds the workspace file records its result.

The **common options** of `install`, `uninstall` and `update` are:

| Option | Behavior |
|---|---|
| `-n`, `--dry-run` | The command performs every step that changes nothing, and reports as messages the changes the operation would make: the packages it would install or uninstall, and those it would mark explicitly installed. It does not take the workspace lock, does not recover an interrupted operation (it reports one as a message, and plans with the workspace as it is), and writes nothing: neither the workspace nor `.lip` changes, and no hook command runs. |
| `--ignore-scripts` | No hook command runs. Nothing else changes. |
| `--rid <RID>` | Sets the runtime of the environment to `<RID>`. |

**Cancellation.** When the user interrupts `lip` (Ctrl+C) while a workspace operation is changing the workspace, the operation is cancelled and its changes are undone (Section 7 rule 2 of [WORKSPACE]). A second interruption ends `lip` at once; the next workspace operation on the workspace then recovers the interrupted one (Section 7 rule 5 of [WORKSPACE]). An interruption before the workspace starts to change ends `lip` at once.

**Concurrency.** When another process holds the workspace lock (Section 8.2 of [WORKSPACE]), a workspace operation fails at once without changing anything. It does not wait.

## 6. Commands

### 6.1. `lip install`

```
lip install <PACKAGES>... [common options] [--no-dependencies]
```

Each of `<PACKAGES>` is a package argument.

Without `--no-dependencies`, `lip install` makes a request of each package argument, and installs a set S of packages that satisfies Section 4 of [WORKSPACE] for those requests together with the requests that made the installed explicitly installed packages:

- The requested packages become explicitly installed; this includes a requested package that is installed as a dependency package.
- An installed package keeps its version when that satisfies every condition of Section 4 of [WORKSPACE]; otherwise a dependency package may change version. Each dependency package that no explicitly installed package needs any more is uninstalled.
- When a requested package is already explicitly installed, the command fails.
- When two package arguments request the same identity, the command fails.

With `--no-dependencies`, for each package argument, `lip` takes the first candidate of its request and installs exactly that package as explicitly installed, replacing an installed package of the same identity. No install edge is followed, and no other package is installed or uninstalled. When the installed package has the same identity, version, archive name and archive identifier as that candidate, it is only marked explicitly installed. Section 4 of [WORKSPACE] may no longer hold afterwards.

### 6.2. `lip uninstall`

```
lip uninstall <PACKAGES>... [common options] [--no-dependencies]
```

Each argument names the installed packages that match it by the first of these rules that matches any installed package:

1. Its identity equals the argument.
2. The first source of its chain equals the argument made into a source as in Section 4, with the targets compared ASCII case-insensitively. An argument without a variant matches every variant, and an argument without a ref matches every ref.

When an argument names no installed package, the command fails.

Without `--no-dependencies`:

- Every named package MUST be explicitly installed; otherwise the command fails.
- The named packages stop being explicitly installed. Each package that no remaining explicitly installed package needs is uninstalled. A named package that another package still needs is kept as a dependency package.

With `--no-dependencies`, exactly the named packages are uninstalled, dependency packages included, and no other package is. Section 4 of [WORKSPACE] may no longer hold afterwards.

### 6.3. `lip update`

```
lip update [PACKAGES]... [common options]
```

Each of `[PACKAGES]` is a package argument.

With arguments, `lip update` makes a request of each package argument again, as "Updating" in Section 6 of [WORKSPACE] defines for the source. Every requested package MUST already be installed, as explicitly installed or as a dependency package; otherwise the command fails. The requested packages become explicitly installed.

Without arguments, `lip update` updates every explicitly installed package as "Updating" in Section 6 of [WORKSPACE] defines. When the first source of its chain is a local path that no longer exists, the package is kept as it is.

A package is replaced only as "Updating" in Section 6 of [WORKSPACE] defines. Dependency packages are updated as far as Section 4 of [WORKSPACE] requires. A package installed from a source with a ref is moved back to the version tags by updating it with its source without a ref.

### 6.4. `lip list`

```
lip list [--json]
```

Writes as its result the packages the workspace file records, each as `<identity>@<version>`, in installation order (Section 6 of [WORKSPACE]), in two groups: explicitly installed packages, then dependency packages. It changes nothing.

With `--json`, the result is:

```json
{ "ExplicitInstalled": ["<identity>@<version>", ...], "ImplicitInstalled": ["<identity>@<version>", ...] }
```

### 6.5. `lip versions`

```
lip versions <PACKAGE> [--json]
```

`<PACKAGE>` MUST be a package argument that is a remote repository without a ref; otherwise the command fails. Its variant does not affect the result. Writes as its result the versions of the version tags of the repository, in ascending precedence [SEMVER], each once.

With `--json`, the result is a JSON array of strings.

### 6.6. `lip view`

```
lip view <PACKAGE>
```

`<PACKAGE>` is a package argument. Writes as its result, as JSON, the manifest of the first archive that its source names (Section 5 step 1 of [PACKAGE]): for a remote repository without a ref, that of the highest version tag; for one with a ref, that of the commit the ref selects. A meta package is written as it is and its `targets` are not followed. It changes nothing.

### 6.7. `lip init`

```
lip init
```

Writes `manifest.json` in the current directory: a manifest of a mod with:

- `format`: `"bf45b1"`;
- `id`: the name of the current directory, with every character other than an ASCII letter, an ASCII digit, `.`, `_` and `-` replaced by `-`;
- `version`: `"0.1.0"`;
- `dependencies`: `{}`;
- `mod`: `{ "loader": "native", "entry": "<id>.dll" }`, `<id>` being the value of `id`.

When `manifest.json` exists, or the value of `id` is not an id (Section 2 of [PACKAGE]), the command fails and writes nothing.

### 6.8. `lip migrate`

```
lip migrate
```

Converts the package repository in the current directory, which holds a `tooth.json` of lip v0.x or a LeviLamina `manifest.json` without the key `format`, to a `manifest.json` with `"format": "bf45b1"`, written as JSON with an indentation of two spaces.

- When `tooth.json` exists, it is the source of the conversion. When `manifest.json` also exists, the command fails and changes nothing. `tooth.json` is deleted after `manifest.json` has been written.
- Otherwise `manifest.json` is the source of the conversion, and is rewritten in place.
- When neither exists, when `manifest.json` has the key `format`, or when a file cannot be read, the command fails and changes nothing.

The mapping of the members of the source to those of the output is not defined by this document. When a value the output needs is missing or invalid, `lip` asks the user for it on the terminal, again until the answer is valid. When standard input or standard error is not a terminal, the command fails instead, naming the value it would ask for, and changes nothing.

### 6.9. `lip cache clean`

```
lip cache clean
```

Deletes the cache directory (Section 4 of [USERSPACE]). Workspaces and the configuration are not affected.

### 6.10. `lip config`

```
lip config get <KEY> [--json]
lip config set <KEY> <VALUE>
lip config list [--json]
lip config delete <KEY>
```

- `get` writes as its result the value of `<KEY>`. With `--json`, the result is the value as a JSON string.
- `set` sets `<KEY>` to `<VALUE>`.
- `list` writes as its result every configuration key with its value. With `--json`, the result is a JSON object that maps each configuration key to its value as a string.
- `delete` resets `<KEY>` to its default value.

When `<KEY>` is not a configuration key (Section 3 of [USERSPACE]), the command fails. `set` and `delete` rewrite the configuration as Section 3 of [USERSPACE] defines.

### 6.11. `lip pack`

```
lip pack [<DIR>] [--variant <VARIANT>] [--platform <PLATFORM>] [--rid <RID>] [-o, --output <FILE>]
```

Packs the `manifest.json` in the current directory into the manifest of a concrete package for one environment. `--variant` defaults to `""`, `--platform` (`server` or `client`) to `server`, and `--rid` to the RID lip runs on; `--platform` and `--rid` together are the environment.

- For a meta package, the target selected is the one Section 5 step 2 of [PACKAGE] selects for the variant and the environment. The output is the manifest without the key `targets`, in which, for each key of the target other than `variant`, `platform`, `runtime` and `source`, that key is set to the value in the target, or deleted when the value is `null`. When the target has `platform`, the output has `platform` equal to the platform of the environment; when it has `runtime`, the output has `runtime` equal to the runtime of the environment.
- For a concrete package, `--variant` MUST be `""`, `--platform` and `--rid` are not used, and the output is the manifest itself.

Without `--output`, the output is the result. With `--output`, `lip` writes a ZIP archive (Section 2 of [PACKAGE]) to `<FILE>`, replacing it, that holds the output as `manifest.json` at its root, and every regular file below `<DIR>` (default: the current directory) at its path relative to `<DIR>`, except `manifest.json` at the root of `<DIR>`, everything below `.git/` and `.lip/` at the root of `<DIR>`, and `<FILE>` itself.

The command fails and writes nothing when `manifest.json` is missing or is not a valid manifest, when no target or more than one is selected, or when the output is not a valid manifest of a concrete package.

### 6.12. `lip rpc`

```
lip rpc
```

Serves the commands over JSON-RPC 2.0 [JSON-RPC] on standard input and standard output, with each message preceded by a `Content-Length` header as in the base protocol of the Language Server Protocol [LSP]. Standard output carries nothing else.

The server has one method for each command other than `pack` and `rpc`, named as the command in PascalCase without spaces: `Install`, `Uninstall`, `Update`, `List`, `Versions`, `View`, `Init`, `Migrate`, `CacheClean`, `ConfigGet`, `ConfigSet`, `ConfigList`, `ConfigDelete`. Its parameters are the arguments and options of the command, by position in the order below or by name:

| Method | Parameters |
|---|---|
| `Install` | `packages`, `dryRun`, `ignoreScripts`, `noDependencies`, `rid` |
| `Uninstall` | `packages`, `dryRun`, `ignoreScripts`, `noDependencies`, `rid` |
| `Update` | `packages`, `dryRun`, `ignoreScripts`, `rid` |
| `Versions`, `View` | `package` |
| `ConfigGet`, `ConfigDelete` | `key` |
| `ConfigSet` | `key`, `value` |
| Others | none |

`packages` is an array of strings, `dryRun`, `ignoreScripts` and `noDependencies` are booleans, and the others are strings. `rid` is optional: `null`, `""` or its omission means the runtime is determined as in Section 5. The `packages` of `Update` MAY be empty.

The result of a method is the result of the command, as the command writes it with `--json`, or `null` when the command has none. The result of `List` is an array of two arrays of strings: the explicitly installed packages, then the dependency packages.

A method that fails responds with a JSON-RPC error whose `message` describes the cause, and leaves everything as the failing command would. `Migrate` asks no questions: where `lip migrate` would ask, the method fails, naming the value. The client MAY cancel a pending request with the notification `$/cancelRequest` with the parameters `{ "id": <request id> }`; a cancelled `Install`, `Uninstall` or `Update` undoes its changes and responds with an error. Requests MAY be sent before earlier ones are answered; a workspace operation that finds the workspace lock held, including by another request, fails at once.

The server sends the messages to the client as requests, which the client answers with `null`: `PrintInfo(message)`, `PrintSuccess(message)`, `PrintWarning(message)`, `PrintError(message)` and `ReportProgress(id, message, percentage)`.

## 7. Security Considerations

`install`, `uninstall` and `update` run hook commands unless `--ignore-scripts` or `--dry-run` is given (Section 9 of [WORKSPACE]).

## 8. IANA Considerations

This document has no IANA actions.

## 9. References

### 9.1. Normative References

- [JSON-RPC] JSON-RPC Working Group, "JSON-RPC 2.0 Specification", <https://www.jsonrpc.org/specification>.
- [LSP] Microsoft, "Language Server Protocol Specification 3.17, Base Protocol", <https://microsoft.github.io/language-server-protocol/specifications/lsp/3.17/specification/#baseProtocol>.
- [PACKAGE] "lip Package Protocol", [`package.md`](package.md).
- [RFC2119] Bradner, S., "Key words for use in RFCs to Indicate Requirement Levels", BCP 14, RFC 2119, DOI 10.17487/RFC2119, March 1997, <https://www.rfc-editor.org/info/rfc2119>.
- [RFC8089] Kerwin, M., "The "file" URI Scheme", RFC 8089, DOI 10.17487/RFC8089, February 2017, <https://www.rfc-editor.org/info/rfc8089>.
- [RFC8174] Leiba, B., "Ambiguity of Uppercase vs Lowercase in RFC 2119 Key Words", BCP 14, RFC 8174, DOI 10.17487/RFC8174, May 2017, <https://www.rfc-editor.org/info/rfc8174>.
- [RFC8259] Bray, T., Ed., "The JavaScript Object Notation (JSON) Data Interchange Format", STD 90, RFC 8259, DOI 10.17487/RFC8259, December 2017, <https://www.rfc-editor.org/info/rfc8259>.
- [SEMVER] Preston-Werner, T., "Semantic Versioning 2.0.0", <https://semver.org/spec/v2.0.0.html>.
- [USERSPACE] "lip Userspace Protocol", [`userspace.md`](userspace.md).
- [WORKSPACE] "lip Workspace Protocol", [`workspace.md`](workspace.md).
