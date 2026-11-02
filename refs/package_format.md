# lip Package Specification

This specification defines [documents][document] of format `bf45b1`, how [lip] resolves them to [packages][package], and how [lip] installs, uninstalls, packs and locks [packages][package]. It also defines how [lip] reads the legacy formats [LeviLamina manifest] and [tooth.json] (§10).

## Conventions

The key words "MUST", "MUST NOT", "REQUIRED", "SHOULD", "SHOULD NOT", "RECOMMENDED", "MAY" and "OPTIONAL" are to be interpreted as described in BCP 14 ([RFC 2119], [RFC 8174]) when, and only when, they appear in all capitals.

Grammars use ABNF ([RFC 5234]). A rule written as `<...>` is defined by the prose inside the brackets.

A reference "§N" names section N of this specification.

## 1. Terms

Each term below is defined using only the terms before it.

- <a id="t-lip"></a>**lip**: a program that implements this specification.
- <a id="t-user"></a>**user**: the person or program that invokes [lip].
- <a id="t-ascii-ci"></a>**ASCII case-insensitive**: two strings are equal ASCII case-insensitively when they are equal after every character `A`–`Z` in both is replaced by the corresponding character `a`–`z`.
- <a id="t-archive"></a>**archive**: a tree of regular files and directories, each named by its path from the root of the tree. An [archive] has one of these forms:
  - <a id="t-zip-archive"></a>**ZIP archive**: a file in the ZIP format ([APPNOTE]). A [ZIP archive] whose file name ends with `.mcmod` is an <a id="t-mcmod"></a>**mcmod**.
  - <a id="t-git-archive"></a>**git archive**: the tree of a [git commit][git-objects].
  - <a id="t-directory-archive"></a>**directory archive**: a directory of another [archive], taken as an [archive] of its own.
- <a id="t-document"></a>**document**: the file `manifest.json` at the root of an [archive]. A [document] MUST be JSON ([RFC 8259]) without comments, encoded in UTF-8 ([RFC 3629]); its top-level value MUST be an object; no object in it may have two members with the same name.
- <a id="t-leviLamina-manifest"></a>**LeviLamina manifest**: a [document] without the key `format`. It is the mod manifest of [LeviLamina].
- <a id="t-tooth-json"></a>**tooth.json**: the file `tooth.json` at the root of an [archive], in format 1, 2 or 3 as defined by the schemas [v1][tooth-v1], [v2][tooth-v2] and [v3][tooth-v3].
- <a id="t-root-document"></a>**root document**: the [tooth.json] of an [archive] when the [archive] has one; otherwise the [document] of the [archive].
- <a id="t-meta-document"></a>**meta document**: a [document] with the key `format` and the key `targets`.
- <a id="t-package-document"></a>**package document**: a [document] with the key `format` and without the key `targets`, or a [LeviLamina manifest] or [tooth.json] read as defined by §10.
- <a id="t-package"></a>**package**: an [archive] whose [root document] is a [package document].
- <a id="t-mod"></a>**mod**: a [package] whose [package document] has the key `mod`, or that is read from a [LeviLamina manifest].
- <a id="t-file-package"></a>**file package**: a [package] that is not a [mod].
- <a id="t-id"></a>**id**: a string that matches the regular expression `^[A-Za-z0-9][A-Za-z0-9._-]*$`.
- <a id="t-identity"></a>**identity**: the [id] that names a [package] (§4.1, §10). Two [identities][identity] are equal when they are equal [ASCII case-insensitively][ASCII case-insensitive].
- <a id="t-version"></a>**version**: a string that is a version of [SemVer 2.0.0], without a leading `v`.
- <a id="t-range"></a>**range**: a string with the syntax and the matching semantics of a [node-semver] range, as `semver.satisfies` applies it without the options `loose` and `includePrerelease`: `*`, X-ranges, `~`, `^`, the comparators `<`, `<=`, `>`, `>=` and `=`, hyphen ranges `A - B`, whitespace for AND, and `||` for OR. A [version] without an operator matches exactly that [version]. A [version] with a prerelease matches only a comparator set that has a prerelease on the same `major.minor.patch`. A [version] *satisfies* a [range] when the [range] matches it.
- <a id="t-variant"></a>**variant**: the string `""`, or a string that matches the regular expression `^[a-z0-9_]+(/[a-z0-9_]+)?$`.
- <a id="t-glob"></a>**glob**: a path pattern. A [glob] MUST NOT be empty, MUST NOT start with `/`, MUST NOT contain `\` or an empty segment other than one after a trailing `/`, and every bracket expression in it MUST be closed. A [glob] matches a path as follows:
  - Each segment of the [glob] matches the corresponding segment of the path by the pattern matching notation of POSIX ([POSIX.2.13], §2.13.1–2.13.2), as `fnmatch` applies it with `FNM_PATHNAME` and without `FNM_PERIOD`: `*` matches any sequence of characters other than `/`, including a leading `.`; `?` matches one character other than `/`; a bracket expression `[...]` matches one character of its set, and `[!...]` one character outside it, with ranges, character classes such as `[:alpha:]`, collating symbols such as `[.c.]` and equivalence classes such as `[=c=]` of the POSIX locale. A bracket expression that starts with `^` is invalid. There is no escape character.
  - A segment that is exactly `**` matches zero or more segments.
  - A [glob] that ends with `/` matches directories, and also matches every file below a directory it matches.
- <a id="t-platform"></a>**platform**: the string `"server"` or the string `"client"`.
- <a id="t-runtime"></a>**runtime**: a .NET runtime identifier ([RID]).
- <a id="t-environment"></a>**environment**: a pair `(platform, runtime)` of a [platform] and a [runtime] (§5.1).
- <a id="t-workspace"></a>**workspace**: the root directory that [lip] operates on.
- <a id="t-operation"></a>**operation**: one installation, uninstallation or update of [packages][package] in a [workspace], together with every [package] it installs, replaces or uninstalls as a dependency (§6, §7).
- <a id="t-failure"></a>**failure**: when this specification calls a condition a [failure], [lip] MUST abort the action in progress and report the cause. When the action is an [operation], §7 "Atomicity" defines what is left of its changes.

## 2. Archives

1. The entry paths of a [ZIP archive] MUST be separated by `/`, MUST NOT start with `/`, MUST NOT contain a `..` segment or `\`, and are decoded as UTF-8. Each entry MUST be a regular file or a directory.
2. A [git archive] contains only the regular files (modes `100644` and `100755`) and the trees of its commit. Symbolic links and submodules are not part of the [git archive].
3. Whether a file is a [ZIP archive] depends only on its content, not on its file name. A [ZIP archive] meant to be a [package] SHOULD be an [mcmod].
4. A [root document] that is a [tooth.json] is read as defined by §10.2. A [root document] that is a [LeviLamina manifest] is read as defined by §10.1. An [archive] that has neither a [tooth.json] nor a [document] is invalid.

## 3. Refs

<a id="t-ref"></a>A **ref** is a string that names [archives][archive]. Its syntax is:

```
ref       = relative-ref / url-ref / spec-ref
relative-ref = relative [ "#" variant ]
url-ref   = url [ "#" variant ]
spec-ref  = spec [ "#" variant ] [ "@" git-ref ]
relative  = "./" <a path, see below>
url       = "https://" <the rest of an absolute URI, RFC 3986>
spec      = <any other string; see below>
variant   = <a variant other than "">
git-ref   = "branch:" name / "tag:" name / "rev:" commit
name      = <a git ref name, see below>
commit    = 40HEXDIG
```

<a id="t-target"></a>The part of a [ref] before its `#` (and before its `@`, for a [spec ref][spec-ref]) is its **target**. The form of a [ref] is decided by its [target], in this order:

| Form | Recognized by | Names |
|---|---|---|
| <a id="t-relative-ref"></a>**relative ref** | The [target] starts with `./` | A path inside the [archive] that contains the [document] holding the [ref], see below |
| <a id="t-url-ref"></a>**url ref** | The [target] starts with `https://` | The [ZIP archive] that is the response body of an HTTPS GET of the [target], an absolute URI ([RFC 3986]); [lip] follows redirects |
| <a id="t-spec-ref"></a>**spec ref** | Any other [target] | The git repository `https://<target>.git`; the [target] MUST be a valid Go module path ([module.CheckPath]) |

**Splitting.** A [relative ref] or [url ref] is split at its first `#`. A [spec ref][spec-ref] is first split at its first `@`, <a id="t-git-ref"></a>everything after which is its **git-ref**, and the part before that `@` is split at its first `#`. <a id="t-ref-variant"></a>The part after the `#` is the **variant of the ref**; a [ref] without `#` has the [variant] `""`. A [ref] with `#` followed by nothing is invalid.

**Relative refs.**
- After `./` comes a non-empty path of segments separated by `/`. It MUST NOT contain an empty segment, a `.` segment, a `..` segment or `\`.
- When the path ends with `/`, it names a directory, which is used as a [directory archive].
- Otherwise it names a file, which MUST be a [ZIP archive].

**Spec refs.**
- <a id="t-spec-version"></a>A **spec version** of a [spec ref][spec-ref] is a [version] V such that the repository has a tag named `v` followed by V. Tags of any other name are ignored.
- The [archive] of a [spec version] is the [git archive] of the commit its tag points to.

<a id="t-pinned-spec-ref"></a>**Pinned spec refs.** A [spec ref][spec-ref] with a [git-ref] is a **pinned spec ref**. Its [git-ref] names one commit:
- `branch:<name>` names the commit `refs/heads/<name>` points to.
- `tag:<name>` names the commit `refs/tags/<name>` points to, for a tag of any name; an annotated tag is peeled to its commit.
- `rev:<commit>` names the commit with that id, compared case-insensitively. An abbreviated commit id is invalid.
- `<name>` MUST be a valid ref name as defined by [git check-ref-format]: non-empty; without control characters, space, `~`, `^`, `:`, `?`, `*`, `[`, `\`, `..` or `@{`; not `@`; not starting or ending with `/`; not ending with `.`; without `//`; and without a `/`-separated component that starts with `.` or ends with `.lock`.
- When `@` in a [spec ref][spec-ref] is not followed by a valid [git-ref], the [ref] is invalid. In particular, a [version] after `@` is invalid; [versions][version] are constrained by a [range] (§5.2).
- A [relative ref] or [url ref] that contains `@branch:`, `@tag:` or `@rev:` is invalid.

<a id="t-local-file-ref"></a>**Local file refs.** A **local file ref** is the absolute path of a [ZIP archive] on the local file system, optionally followed by `#` and a [variant]. It is split at its last `#`. It names the [ZIP archive] read from that file. A directory on the local file system is not an [archive] and cannot be named by a [local file ref]. A [document] MUST NOT contain a [local file ref]; only [requests][request] (§6) can.

A [document] MUST NOT contain a [ref] with any other URI scheme, such as `http:` or `file:`.

## 4. Documents

A [document] of this format has the member `"format": "bf45b1"`. A [document] whose `format` has any other value is invalid. A [document] without `format` is a [LeviLamina manifest] (§10.1).

Members not defined by this specification are ignored. A [document] in which a member defined by this specification has a value of another type than defined is invalid.

### 4.1 Keys

| Key | Type | Required | Definition |
|---|---|---|---|
| `format` | string | Yes | `"bf45b1"` |
| `id` | [id] | In a [package document] | The [identity] of the [package] |
| `version` | [version] | In a [package document] | The [version] of the [package] |
| `platform` | [platform] | No | The [platform] the [package] is for; omitted means every [platform] |
| `runtime` | [runtime] | No | The [runtime] the [package] is for; omitted means every [runtime] |
| `info` | [Info] | No | Information for people; [lip] does not use it |
| `dependencies` | object mapping [id] to [Dependency] | No | §6 |
| `conflicts` | object mapping [id] to [range] | No | §6 |
| `mod` | [Mod] | No | Makes the [package] a [mod] (§7) |
| `files` | array of [File] | No | The files of a [file package] (§7) |
| `archives` | object mapping an [archive name] to [Archive][archive-object] | No | Downloaded [archives][archive] that `files` take files from (§7) |
| `preserve` | array of [glob] | No | §7 |
| `remove` | array of [glob] | No | §7 |
| `hooks` | [Hooks] | No | §7 |
| `targets` | non-empty array of [Target] | No | Makes the [document] a [meta document] (§4.2) |

In a [package document], `mod` MUST NOT appear together with `files` or `archives`, and the `from` of every [File] MUST be a key of `archives`.

The object types are:

- <a id="t-info"></a>**Info** = `{ "name"?: string, "description"?: string, "authors"?: string[], "tags"?: string[], "icon"?: string }`. `icon` is an `https` URI ([RFC 3986]).
- <a id="t-dependency"></a>**Dependency** = a [range], or `{ "version"?: range, "source"?: ref | ref[], "mod"?: boolean }`. A [range] R alone is equivalent to `{ "version": R }`. An omitted `version` means every [version]. An array `source` MUST NOT be empty; its [refs][ref] are alternatives, in order. `"mod": false` is allowed only together with `source`.
- <a id="t-mod-object"></a>**Mod** = `{ "loader": string, "entry": string, "passive"?: boolean, "loadAfter"?: (id | "*")[], "loadBefore"?: (id | "*")[], "extra"?: object }`. `loader` and `entry` MUST NOT be empty.
- <a id="t-file"></a>**File** = `{ "from"?: string, "src": string, "dest": string }`. `src` is a path in the [archive] of the [package], or in the [Archive][archive-object] named by `from`; `dest` is a path in the [workspace]. `src` and `dest` MUST NOT start with `/`, and MUST NOT contain `\`, a drive letter, a `..` segment, or an empty segment other than the whole value `""` or one after a trailing `/`. A [File] has exactly one of these forms:
  - <a id="t-glob-form"></a>**glob form**, when `src` contains `*`, `?` or `[`: `src` MUST be a [glob], and `dest` MUST be `""` or end with `/`.
  - <a id="t-directory-form"></a>**directory form**, otherwise, when `src` is `""` or ends with `/`: `dest` MUST be `""` or end with `/`.
  - <a id="t-file-form"></a>**file form**, otherwise: `dest` MUST NOT be `""` and MUST NOT end with `/`.
- <a id="t-archive-object"></a>**Archive** = `{ "url": string | string[], "sha256"?: string, "format"?: "zip" | "tar" | "tgz" | "file" }`. Every `url` is an `https` URI ([RFC 3986]); an array MUST NOT be empty, and its URIs are alternatives, in order. `sha256` is 64 hexadecimal digits, compared case-insensitively. <a id="t-archive-name"></a>An **archive name**, a key of `archives`, MUST match `^[A-Za-z0-9][A-Za-z0-9._-]*$`.
- <a id="t-hooks"></a>**Hooks** = `{ "preInstall"?: string[], "postInstall"?: string[], "preUninstall"?: string[], "postUninstall"?: string[] }`. Each string is a [command] (§7).

[LeviLamina] reads `id`, `version`, `platform`, `dependencies`, `conflicts` and `mod` of a [mod]: a [Dependency] without `"mod": false` names a [mod] that MUST be loaded, `loadAfter` and `loadBefore` order the loading, and `"*"` stands for every [mod]. [lip] uses `mod` only through its presence.

### 4.2 Meta Documents

Resolution (§5) uses only the members `format`, `targets`, `id` and `version` of a [meta document]; `id` and `version` take part only in [templates][template]. When present, `id` MUST be an [id] and `version` a [version]. <a id="t-meta-template"></a>All other members of a [meta document] are its **packing template**, used and checked only by packing (§8).

<a id="t-target-object"></a>**Target** = an object with these members:

| Key | Type | Required | Definition |
|---|---|---|---|
| `variant` | [variant] | No | Default `""` |
| `platform` | [platform] or non-empty array of [platform] | No | Omitted means every [platform] |
| `runtime` | [runtime] or non-empty array of [runtime] | No | Omitted means every [runtime] |
| `source` | [ref] or non-empty array of [ref] | Yes | Alternatives, in order |
| any key of §4.1 other than `format` and `targets` | the type of that key, or `null` | No | Replaces the member of the [packing template][meta-template] in packing (§8); `null` removes it |

<a id="t-selector-key"></a>The **selector keys** of a [Target] are `platform` and `runtime`. <a id="t-value-set"></a>The **value set** of a selector key a [Target] writes is its value when that is an array, and otherwise the set holding its value.

<a id="t-template"></a>**Templates.** A [ref] in `source` MAY contain the **templates** `{{id}}`, `{{version}}`, `{{platform}}` and `{{runtime}}`:
- `{{id}}` and `{{version}}` are replaced by the value of the member of the same name in the [Target], or else in the [meta document]. A `null` member in the [Target] means no value. When there is no value, the [ref] produces no [candidate] (§5.2); this is not a [failure].
- `{{platform}}` and `{{runtime}}` are replaced by the [platform] and the [runtime] of the [environment].
- [Templates][template] are replaced before the [ref] is parsed (§3).
- A [meta document] in which any other `{{`…`}}` appears is invalid.

**Validity.** A [meta document] is invalid when two of its [Targets][Target] A and B have the same `variant` and both of the following hold:
1. A and B write the same [selector keys][selector key];
2. for every [selector key] they write, the [value sets][value set] of A and B intersect.

### 4.3 Documents from Tags

<a id="t-tag-version"></a>The **tag version** of a [git archive] is:
- for the [archive] of a [spec version], that [spec version];
- for the [archive] of a [pinned spec ref] whose [git-ref] is `tag:v` followed by a [version] V, the [version] V;
- otherwise, none.

When a [git archive] has a [tag version] and its [document] has the member `version`, that member MUST equal the [tag version]. When there is no [tag version], the `version` of the [document] is used.

### 4.4 Glob Bases

The [globs][glob] of `preserve` and `remove` are matched against paths relative to `mods/<id>/` for a [mod], `<id>` being its [identity], and relative to the [workspace] for a [file package].

## 5. Resolution

### 5.1 Environment

Each [workspace] has one [environment]. Each component is the first value that exists in this order:

| Component | Order |
|---|---|
| [platform] | 1. The value the [user] specifies explicitly.<br>2. The value recorded in the [lock].<br>3. `"server"`, when the file `bedrock_server.exe` exists at the root of the [workspace].<br>4. `"client"`, when the file `Minecraft.Windows.exe` exists at the root of the [workspace].<br>5. Otherwise, a [failure]. |
| [runtime] | 1. The value the [user] specifies explicitly.<br>2. The value recorded in the [lock].<br>3. The [RID] that [lip] itself runs on. |

### 5.2 Resolve

<a id="t-candidate"></a>A **candidate** is a [package] that a [ref] can resolve to. <a id="t-chain"></a>The **chain** of a [candidate] is the sequence of [refs][ref] followed to reach it, starting with the [ref] given to Resolve; each [ref] of a [chain] is a <a id="t-hop"></a>**hop**.

Resolve(ref, base, range) produces an ordered sequence of [candidates][candidate]. `base` is the [archive] that contains the [document] holding the [ref]. `range` is a [range] given by the caller; an omitted `range` matches every [version]. "No [candidate]" below means that the path being followed produces no [candidate]; it is not a [failure]. [lip] SHOULD report each path that produces no [candidate], with the reason.

1. **Obtain the [archive].**
   - [Relative ref]: the path inside `base` (§3).
   - [URL ref][url ref]: downloaded.
   - [Local file ref]: read from the local file system.
   - [Spec ref][spec-ref] that is not a [pinned spec ref]: take every [spec version] that satisfies `range`, ordered from the highest to the lowest [SemVer 2.0.0] precedence, and at equal precedence by tag name, the lexicographically greater first. Run steps 2–4 on the [archive] of each in turn, and concatenate the [candidates][candidate] in that order.
   - [Pinned spec ref]: run steps 2–4 on the [git archive] of the commit its [git-ref] names. Within one [operation], a [git-ref] names the same commit throughout, even when its branch or tag moves. When the repository has no such branch, tag or commit, there is no [candidate]. `range` does not select the [archive]; it applies in step 4.
   - When the [archive] cannot be obtained, or is invalid (§2), there is no [candidate].
2. **Read the [root document].** When it is invalid, there is no [candidate].
3. **[Meta document].**
   - Select the [Targets][Target] whose `variant` equals the [variant of the ref][ref-variant]. Of those, keep each [Target] whose [value set] of every [selector key] it writes contains the corresponding component of the [environment].
   - Of those, select the [Target] that writes the most [selector keys][selector key]. When none is left, or more than one writes the most, there is no [candidate].
   - For each [ref] r of the `source` of the selected [Target], in order, run Resolve(r, this [archive], range), and concatenate the [candidates][candidate] in that order.
4. **[Package document].** When all of the following hold, the single [candidate] is this [package]; otherwise there is no [candidate]:
   - the [variant of the ref][ref-variant] is `""`;
   - `platform` is omitted, or equals the [platform] of the [environment];
   - `runtime` is omitted, or equals the [runtime] of the [environment];
   - `version` satisfies `range`.

**Loops.** There is also no [candidate] when:
- a [chain] has more than 8 [hops][hop];
- the same pair ([archive key], [variant of the ref][ref-variant]) occurs twice in one [chain].

<a id="t-archive-key"></a>The **archive key** of an [archive] is:

| [Archive] obtained from | [Archive key] |
|---|---|
| [URL ref][url ref] | The [target] |
| [Spec ref][spec-ref] | `<spec>@<spec version>` |
| [Pinned spec ref] | `<spec>@rev:<commit>`, `<commit>` being the commit its [git-ref] names, in lowercase |
| [Relative ref] | The [archive key] of `base`, then `!`, then the path |
| [Local file ref] | The `file` URI ([RFC 8089]) of its absolute path |

The [identity] of a [candidate] is the `id` of its [package document], whatever [meta documents][meta document] its [chain] passes through.

## 6. Dependency Graph

<a id="t-request"></a>A **request** is a pair ([ref], [range]) the [user] gives to an [operation]. A [request] is inside no [archive], so it has no `base`: a [relative ref] in a [request] has no [candidate]. The [ref] of a [request] MAY be a [local file ref].

<a id="t-install-edge"></a>The **install edges** of a [package] are the members of its `dependencies` whose [Dependency] has `source`. Members without `source` take no part in this section.

<a id="t-stands-in"></a>**Standing in.** A [package] whose [chain] starts with a [pinned spec ref] `s#v@g` **stands in for** every [candidate] of the [spec ref][spec-ref] `s#v` without [git-ref] (same `s`, compared [ASCII case-insensitively][ASCII case-insensitive], and same `v`) for a `range` its [version] satisfies. A [pinned spec ref] is satisfied only by its own [candidates][candidate]: a [package] from `s@branch:a` does not satisfy `s@branch:b`, and a [package] from a [spec version] does not satisfy `s@branch:a`.

[lip] MUST find, for the [requests][request] of an [operation], a set S of [packages][package] that satisfies all of the following conditions. When no such S exists, it is a [failure].

1. For every [request] (r, range), S contains a [package] that is a [candidate] of Resolve(r, none, range), or that [stands in][stands-in] for one.
2. For every [install edge] (key k, [Dependency] d) of every [package] P in S, S contains a [package] Q such that both hold:
   - for some [ref] r of the `source` of d, Q is a [candidate] of Resolve(r, the [archive] of P, the `version` of d), or [stands in][stands-in] for one;
   - the [identity] of Q equals k.
3. No two [packages][package] in S have the same [identity].
4. For every member (key k, [range] R) of the `conflicts` of every [package] in S, S contains no [package] whose [identity] equals k and whose [version] satisfies R.

When more than one S satisfies the conditions, [lip] SHOULD prefer [packages][package] that come earlier in the sequences of [candidates][candidate].

## 7. Installation and Uninstallation

<a id="t-installation-order"></a>**Installation order.** A [package] is installed after the [packages][package] its [install edges][install edge] are satisfied by. The [packages][package] of a cycle of [install edges][install edge] are installed in ascending order of their [identities][identity] with `A`–`Z` replaced by `a`–`z`.

<a id="t-command"></a>**Commands.** A **command** is a string of [Hooks]. [lip] runs it with the shell of the operating system (`cmd /c` on Windows, `sh -c` on other operating systems), with the [workspace] as the working directory. A [command] that exits with a non-zero code is a [failure].

<a id="t-own"></a>**Ownership.** A [package] **owns** every file it writes to the [workspace] by the steps below.

To install a [package] P:

1. Run each [command] of `hooks.preInstall`, in order.
2. Write the files:
   - [Mod]: write every file of the [archive] of P to `mods/<id>/`, `<id>` being the [identity] of P.
   - [File package]: carry out every [File] of `files`, in order. The source of a [File] is the [Archive][archive-object] named by its `from`, or else the [archive] of P.
     - [File form][file-form]: write the file `src` of the source to `dest`. When the source has no such file, it is a [failure].
     - [Directory form][directory-form]: write every file below the directory `src` of the source into `dest`, at its path relative to `src`. When the source has no file below `src`, it is a [failure].
     - [Glob form][glob-form]: write every file of the source whose path matches the [glob] `src` into `dest`, at its path relative to the **fixed prefix** of `src`: the leading segments of `src` that contain none of `*`, `?` and `[`. A [glob] that matches no file writes nothing.
     - The [document] of P is never written.
     - When two [Files][File] write the same path, the later one wins.
3. Run each [command] of `hooks.postInstall`, in order.

**Downloaded archives.** An [Archive][archive-object] is downloaded from the first of its `url` that can be downloaded and, when `sha256` is given, whose content has that SHA-256 ([FIPS 180-4]). When there is none, it is a [failure]. The files of the source are:
- `format` `"zip"`, `"tar"` or `"tgz"`: the content MUST be a [ZIP archive], a POSIX tar archive ([POSIX.pax]), or a tar archive compressed with gzip ([RFC 1952]), respectively; the source is its files.
- `format` omitted: the content MUST be one of those three formats, detected from the content.
- `format` `"file"`: the source holds one file, the content, at the path that is the [archive name].

§2 rule 1 applies to the entry paths of ZIP and tar archives; entries other than regular files and directories are not part of the source.

**Write rules.** When [lip] writes a file to the [workspace]:
- When the file is `.lip` at the root of the [workspace], or below it, it is a [failure].
- When the file is [owned][own] by another installed [package], it is a [failure].
- When the file exists and matches a [glob] of the `preserve` of P (§4.4), the existing file is kept.
- Otherwise the file is written, replacing an existing one.

To uninstall a [package] P:

1. Run each [command] of `hooks.preUninstall`, in order.
2. Delete each file [owned][own] by P that matches no [glob] of its `preserve`.
3. Delete each path that matches a [glob] of its `remove` and is not [owned][own] by another [package]. A directory is deleted with everything below it. The directories `mods` and `plugins` at the root of the [workspace] never match, so nothing below them is deleted through them. `.lip` at the root of the [workspace], and everything below it, never match.
4. Delete each directory that steps 2 and 3 left empty, except `mods` and `plugins`.
5. Run each [command] of `hooks.postUninstall`, in order.

**Update.** Updating a [package] uninstalls the old [package], then installs the new [package].

<a id="t-server-layout"></a>**Server layout.** In a [workspace] whose [platform] is `"server"`, `plugins` at the root of the [workspace] MUST be a link to `mods`, so that software that looks in `plugins` keeps working: a directory junction on Windows, a relative symbolic link on other operating systems. A path through `plugins/` and the corresponding path through `mods/` are the same file; ownership is compared, and files are recorded in the [lock], in the `mods/` form. Before an [operation] changes the [workspace], [lip] MUST establish this layout:

- When `mods` does not exist, create it as a directory.
- When `plugins` does not exist, create it as a link to `mods`.
- When `plugins` is a link to `mods`, do nothing.
- When `plugins` is a link to anything else, it is a [failure].
- When `plugins` is a directory, move its entries into `mods`, delete `plugins` and create it as a link to `mods`. When an entry has the same name as an entry of `mods`, it is a [failure], before any entry is moved.

Establishing the layout is part of the [operation].

**Atomicity.** An [operation] succeeds or fails as a whole.

1. **Early failures change nothing.** When an [operation] fails for a reason that does not depend on changing the [workspace] — a [failure] of §5 or §6, an [archive] that cannot be obtained, a missing `src`, a file [owned][own] by another [package], a [server layout] that cannot be established — no [command] of the [operation] runs, and neither the [workspace] nor the [lock] changes. Each step is checked against the [workspace] as the steps before it leave it; for example, an update installs the new [package] into the [workspace] that the uninstallation of the old [package] leaves.
2. **Later failures are undone.** When a step fails while the [workspace] is being changed (a file cannot be written or deleted, a [command] exits with a non-zero code, the [user] cancels the [operation]), every change the [operation] made to the [workspace] is undone: files it created are deleted; files it replaced or deleted are restored; directories and the [server layout] are restored. The [lock] does not change. The [lock] records an [operation] only once every step of it has succeeded.
3. **Commands are not undone.** [lip] cannot undo what a [command] did. A failing [command] fails the [operation] like any other step, and [lip] undoes its own changes, including those made before earlier [commands][command] ran; the [commands][command] that already ran are neither run again nor reversed. [Commands][command] SHOULD be safe to run again, and SHOULD NOT assume that the files of the [operation] stay in place.
4. The paths that uninstallation steps 2–4 delete are those that match before any [command] of the [operation] runs: files a [command] creates are not deleted through `remove`.
5. **Interruption.** When [lip] is interrupted while changing the [workspace] (for example, its process is killed), the next [operation] on the [workspace] first completes the interrupted [operation] when the [lock] already records it, and undoes it otherwise.
6. **Concurrency.** Two [operations][operation] MUST NOT apply to the same [workspace] at the same time.

## 8. Packing

Packing turns a [meta document] M into the [package document] of one of its [Targets][Target] T, for a [platform] p and a [runtime] r. Pack(M, T, p, r) is the [document] obtained by these steps:

1. Start from M without `targets`.
2. For each member of T other than `variant`, `platform`, `runtime` and `source`: when its value is `null`, remove the member; otherwise set the member to the value in T, replacing the whole value.
3. When T writes `platform`, set `platform` to p. When T writes `runtime`, set `runtime` to r.
4. When the result is not a valid [package document] (§4), packing fails.

A [Target] stands for one [package] for each combination of p and r. When T writes `platform`, p MUST be in its [value set]; when T writes `runtime`, r MUST be in its [value set]. The [package] of T for p and r SHOULD be published where the first [ref] of the `source` of T names after its [templates][template] are replaced (§4.2) with the [environment] (p, r). Packing a [package document] gives the [package document] itself.

## 9. Lock

<a id="t-lock"></a>The **lock** of a [workspace] records, for each installed [package]:

- its [identity] and [version];
- the [environment];
- <a id="t-explicitly-installed"></a>whether the [user] requested it: such a [package] is **explicitly installed**; any other is a <a id="t-dependency-package"></a>**dependency package**;
- its [chain]: the [ref] of each [hop], including its [git-ref], the index of the [Target] selected at that [hop], and for a [hop] that is a [spec ref][spec-ref], the commit its [archive] was at;
- <a id="t-archive-identifier"></a>the **archive identifier** of the [package]: for a [ZIP archive], its SHA-256 and the URI or local file it was read from; for a [ZIP archive] inside another [archive], its SHA-256 and the [archive identifier] of that [archive]; for a [git archive], the commit id and the path;
- the paths, relative to the [workspace], of the files the [package] [owns][own], in the `mods/` form of §7 "Server layout".

**Reinstalling.** To reinstall a [package] from the [lock], [lip] installs the [archive] the [lock] records, and fails when its [archive identifier] differs. The result does not depend on where the [refs][ref] of the [chain] point now: a [package] from a [pinned spec ref] is reinstalled at the recorded commit, wherever its branch or tag points now.

**Updating.** Updating an [explicitly installed] [package] makes a [request] of the first [ref] of its [chain] again: a [spec ref][spec-ref] without [git-ref] gives its highest [spec version]; `branch:` the commit the branch points to now; `tag:` the commit the tag points to now; `rev:` the same commit; a [url ref] is downloaded again ([lip] MAY serve it from a download cache, so the content behind a URI SHOULD NOT change); a [local file ref] is read again. The [package] is replaced when the result differs in [identity], [version], [archive key] or [archive identifier].

**Earlier formats.** [lip] accepts the `tooth_lock.json` ([schema][tooth-lock-v3]) that lip v0.x left in a [workspace], and keeps managing the [packages][package] it records: they can be listed, uninstalled and updated. How is not specified; the rest of this section applies to the entries [lip] records itself.

## 10. Legacy Formats

[lip] reads the [documents][document] of this section as [package documents][package document]. It does not write them.

### 10.1 LeviLamina Manifests

A [LeviLamina manifest] is read as a [mod] with:

- [identity]: its `name`, which MUST be an [id];
- [version]: its `version`, which MUST be a [version];
- `platform`: its `platform` when that is a [platform]; omitted otherwise;
- `conflicts`: its `conflicts` entries, each `{ "name": id, "version"?: range }`, an omitted `version` meaning every [version];
- no [install edges][install edge], no `runtime`, no `preserve`, no `remove` and no `hooks`.

When `name`, `version` or a `conflicts` entry is not as above, the [LeviLamina manifest] is invalid. Its other members are ignored.

### 10.2 tooth.json

When an [archive] has a [tooth.json], [lip] ignores its [document] and reads the [tooth.json] as a [file package] by the rules below; from then on it is resolved, installed, uninstalled and locked like any other [package] (§5–§9). A [tooth.json] of format 1 or 2 is first migrated to format 3 as the [tooth.json] specifications define, including their template substitution; the rules below apply to format 3.

<a id="t-tooth-variant"></a>A **tooth variant** is an element of `variants` of a format 3 [tooth.json]. <a id="t-label"></a>The **label** of a [tooth variant] is its `label`, or `""` when absent. <a id="t-tooth-range"></a>A **tooth range** is the value of a dependency of a [tooth variant], with the format 3 semantics. <a id="t-tooth-name"></a>The **tooth name** is the last `/`-separated element of the `tooth` path, or the element before it when the last element is a major version suffix (`v` followed by digits, as in `github.com/o/r/v2`).

1. **[Identity] and [version].** The [identity] is the [tooth name], followed by `-` and the [label] selected by rule 2 with every `/` replaced by `-` when that [label] is not `""`. When the [identity] is not an [id], there is no [candidate]. (tooth.json installed each labelled [tooth variant] as a [package] of its own, and a [tooth variant] could depend on its sibling [tooth variants][tooth variant], so the [tooth variants][tooth variant] of one [tooth.json] have different [identities][identity].) The [version] is the [tag version] (§4.3) when there is one, and the value of `version` otherwise; §4.3 does not apply. [lip] SHOULD warn when a [tag version] differs from `version`.
2. **Variant selection**, in place of step 4 of §5.2. The [variant of the ref][ref-variant] is a [label], except that `""` means `client` when the [platform] of the [environment] is `"client"` and a [tooth variant] with the [label] `client` exists. The [tooth variants][tooth variant] with that [label] whose `platform` is empty, equals the [runtime] of the [environment] or matches it as a [tooth.json] glob are merged, in order: their `dependencies`, `assets`, `preserve_files` and `remove_files` are concatenated, and each script hook is taken from the one [tooth variant] that has it. When no [tooth variant] is selected, there is no [candidate]. When two merged [tooth variants][tooth variant] declare the same dependency key or the same script hook, it is a [failure]. The result is a [file package] without `platform` and `runtime`; when its [version] satisfies `range`, it is the single [candidate], and otherwise there is no [candidate].
3. **Dependencies.** Each dependency `"<path>[#label]": range` becomes an [install edge] with the single [ref] `<path>[#label]`, whose `range` is the [tooth range], and which a [package] of any [identity] satisfies. When the [archive] that [ref] names has no [tooth.json] and the [label] is `client`, the [variant of the ref][ref-variant] is taken as `""`.
4. **Assets.** Asset i of the [tooth variant], counting from 0, is mounted at the directory `i` of the [archive] of the [package]. A `self` asset is the [archive] of the [tooth.json] itself. A `zip`, `tar` or `tgz` asset is an [archive] downloaded from the first of its `urls` that can be downloaded; its format is detected from its content, and §2 rule 1 does not apply. An `uncompressed` asset is a single file downloaded the same way, mounted as the file `i`.
5. **Placements.** At installation, every placement of asset i becomes one [File] in [file form][file-form] for each file of the asset it matches, ordered by asset, then by the position of the file in the asset, then by placement. A `file` placement takes the file whose path equals `src`, placed at `dest`; when there is none, every file that matches `src` as a [tooth.json] glob, placed at `dest/<file name>`. A `dir` placement takes every file below the directory `src` (`""` and `"."` meaning every file), placed at `dest/<path relative to src>`. `\` in `dest` is read as `/`, and `.` and `..` segments are applied. A placement that matches no file places nothing. When two placements write the same path, the later one wins. The rule of §7 that the [document] of P is never written does not apply.
6. **preserve_files and remove_files** become `preserve` and `remove`, with `\` replaced by `/`. A `preserve_files` pattern without `/` is prefixed with `**/`, since format 3 also matched it against the file name alone. A `remove_files` pattern that does not end with `/` also yields the same pattern followed by `/`, since format 3 also removed matching directories. A pattern that is not a valid [glob], such as one starting with `/`, is dropped.
7. **Scripts.** `pre_install` becomes `preInstall`; `install` followed by `post_install` becomes `postInstall`; `pre_uninstall` followed by `uninstall` becomes `preUninstall`; `post_uninstall` becomes `postUninstall`. Other scripts are not used.
8. **Lock.** The [lock] records the [tooth.json], in format 3 with the [version] installed, in place of a [document]. Reinstalling or uninstalling the [package] from the [lock] treats it as the [file package] that the recorded [tooth.json] maps to by these rules, with the [variant of the ref][ref-variant] of the last [hop] of the [chain] and the [environment] of the [lock].

## Appendix: Examples (Non-normative)

The [meta document] of the repository `github.com/LiteLDev/LegacyMoney` at tag `v0.22.0`:

```json
{
  "format": "bf45b1",
  "id": "LegacyMoney",
  "version": "0.22.0",
  "info": { "name": "Legacy Money", "authors": ["LiteLDev"] },
  "dependencies": {
    "LeviLamina": { "version": ">=26.51.0 <26.52.0", "source": "github.com/LiteLDev/LeviLamina" },
    "iListenAttentively": { "version": "^0.17.0", "source": "github.com/MiracleForest/iListenAttentively" }
  },
  "mod": { "loader": "native", "entry": "LegacyMoney.dll" },
  "preserve": ["config/", "data/"],
  "targets": [
    {
      "platform": ["server", "client"], "runtime": "win-x64",
      "source": "https://github.com/LiteLDev/LegacyMoney/releases/download/v{{version}}/{{id}}-{{platform}}-{{runtime}}.mcmod"
    }
  ]
}
```

The [package document] that packing it for `("server", "win-x64")` puts at the root of `LegacyMoney-server-win-x64.mcmod`:

```json
{
  "format": "bf45b1",
  "id": "LegacyMoney",
  "version": "0.22.0",
  "platform": "server",
  "runtime": "win-x64",
  "info": { "name": "Legacy Money", "authors": ["LiteLDev"] },
  "dependencies": {
    "LeviLamina": { "version": ">=26.51.0 <26.52.0", "source": "github.com/LiteLDev/LeviLamina" },
    "iListenAttentively": { "version": "^0.17.0", "source": "github.com/MiracleForest/iListenAttentively" }
  },
  "mod": { "loader": "native", "entry": "LegacyMoney.dll" },
  "preserve": ["config/", "data/"]
}
```

A [file package] that takes files from a downloaded [archive] and from itself:

```json
{
  "format": "bf45b1",
  "id": "bedrock-server",
  "version": "1.21.50",
  "platform": "server",
  "runtime": "win-x64",
  "archives": {
    "bds": { "url": "https://www.minecraft.net/bedrockdedicatedserver/bin-win/bedrock-server-1.21.50.01.zip" }
  },
  "files": [
    { "from": "bds", "src": "", "dest": "" },
    { "src": "defaults/*.json", "dest": "" }
  ],
  "preserve": ["server.properties", "allowlist.json", "permissions.json", "worlds/"]
}
```

A [meta document] that names sub-[packages][package] by [variant]:

```json
{
  "format": "bf45b1",
  "targets": [
    { "variant": "quickjs", "source": "./quickjs/" },
    { "variant": "lua", "source": "./lua/" }
  ]
}
```

## References

- [RFC 2119]: Key words for use in RFCs to Indicate Requirement Levels.
- [RFC 8174]: Ambiguity of Uppercase vs Lowercase in RFC 2119 Key Words.
- [RFC 5234]: Augmented BNF for Syntax Specifications: ABNF.
- [RFC 8259]: The JavaScript Object Notation (JSON) Data Interchange Format.
- [RFC 3629]: UTF-8, a transformation format of ISO 10646.
- [RFC 3986]: Uniform Resource Identifier (URI): Generic Syntax.
- [RFC 8089]: The "file" URI Scheme.
- [RFC 1952]: GZIP file format specification version 4.3.
- [FIPS 180-4]: Secure Hash Standard (SHA-256).
- [APPNOTE]: ZIP File Format Specification.
- [POSIX.2.13]: IEEE Std 1003.1-2017, Shell Command Language, §2.13 Pattern Matching Notation.
- [POSIX.pax]: IEEE Std 1003.1-2017, pax, tar interchange format.
- [SemVer 2.0.0]: Semantic Versioning 2.0.0.
- [node-semver]: npm semver, Ranges.
- [module.CheckPath]: `golang.org/x/mod/module.CheckPath`.
- [git check-ref-format]: git-check-ref-format(1).
- [git-objects]: Git Objects (commits and trees).
- [RID]: .NET Runtime Identifier catalog.
- [LeviLamina]: LeviLamina.
- tooth.json schemas: [format 1][tooth-v1], [format 2][tooth-v2], [format 3][tooth-v3]; tooth_lock.json schema: [format 3][tooth-lock-v3].

[lip]: #t-lip
[user]: #t-user
[ASCII case-insensitive]: #t-ascii-ci
[archive]: #t-archive
[ZIP archive]: #t-zip-archive
[mcmod]: #t-mcmod
[git archive]: #t-git-archive
[directory archive]: #t-directory-archive
[document]: #t-document
[LeviLamina manifest]: #t-leviLamina-manifest
[tooth.json]: #t-tooth-json
[meta document]: #t-meta-document
[package document]: #t-package-document
[package]: #t-package
[mod]: #t-mod
[file package]: #t-file-package
[id]: #t-id
[identity]: #t-identity
[version]: #t-version
[range]: #t-range
[variant]: #t-variant
[glob]: #t-glob
[platform]: #t-platform
[runtime]: #t-runtime
[environment]: #t-environment
[workspace]: #t-workspace
[operation]: #t-operation
[failure]: #t-failure
[root document]: #t-root-document
[ref]: #t-ref
[target]: #t-target
[relative ref]: #t-relative-ref
[url ref]: #t-url-ref
[spec-ref]: #t-spec-ref
[ref-variant]: #t-ref-variant
[spec version]: #t-spec-version
[pinned spec ref]: #t-pinned-spec-ref
[git-ref]: #t-git-ref
[local file ref]: #t-local-file-ref
[Info]: #t-info
[Dependency]: #t-dependency
[Mod]: #t-mod-object
[File]: #t-file
[glob-form]: #t-glob-form
[directory-form]: #t-directory-form
[file-form]: #t-file-form
[archive-object]: #t-archive-object
[archive name]: #t-archive-name
[Hooks]: #t-hooks
[meta-template]: #t-meta-template
[Target]: #t-target-object
[selector key]: #t-selector-key
[value set]: #t-value-set
[template]: #t-template
[tag version]: #t-tag-version
[candidate]: #t-candidate
[chain]: #t-chain
[hop]: #t-hop
[archive key]: #t-archive-key
[request]: #t-request
[install edge]: #t-install-edge
[stands-in]: #t-stands-in
[command]: #t-command
[own]: #t-own
[server layout]: #t-server-layout
[lock]: #t-lock
[archive identifier]: #t-archive-identifier
[label]: #t-label
[explicitly installed]: #t-explicitly-installed
[tooth name]: #t-tooth-name
[tooth variant]: #t-tooth-variant
[tooth range]: #t-tooth-range

[RFC 2119]: https://www.rfc-editor.org/rfc/rfc2119
[RFC 8174]: https://www.rfc-editor.org/rfc/rfc8174
[RFC 5234]: https://www.rfc-editor.org/rfc/rfc5234
[RFC 8259]: https://www.rfc-editor.org/rfc/rfc8259
[RFC 3629]: https://www.rfc-editor.org/rfc/rfc3629
[RFC 3986]: https://www.rfc-editor.org/rfc/rfc3986
[RFC 8089]: https://www.rfc-editor.org/rfc/rfc8089
[RFC 1952]: https://www.rfc-editor.org/rfc/rfc1952
[FIPS 180-4]: https://csrc.nist.gov/pubs/fips/180-4/upd1/final
[APPNOTE]: https://pkware.cachefly.net/webdocs/casestudies/APPNOTE.TXT
[POSIX.2.13]: https://pubs.opengroup.org/onlinepubs/9699919799/utilities/V3_chap02.html#tag_18_13
[POSIX.pax]: https://pubs.opengroup.org/onlinepubs/9699919799/utilities/pax.html
[SemVer 2.0.0]: https://semver.org/spec/v2.0.0.html
[node-semver]: https://github.com/npm/node-semver#ranges
[module.CheckPath]: https://pkg.go.dev/golang.org/x/mod/module#CheckPath
[git check-ref-format]: https://git-scm.com/docs/git-check-ref-format
[git-objects]: https://git-scm.com/book/en/v2/Git-Internals-Git-Objects
[RID]: https://learn.microsoft.com/dotnet/core/rid-catalog
[LeviLamina]: https://github.com/LiteLDev/LeviLamina
[tooth-v1]: files/legacy/v1/tooth.schema.yaml
[tooth-v2]: files/legacy/v2/tooth.schema.yaml
[tooth-v3]: files/legacy/v3/tooth.schema.yaml
[tooth-lock-v3]: files/legacy/v3/tooth_lock.schema.yaml
