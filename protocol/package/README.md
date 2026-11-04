# Package

This document defines [packages][package], the four forms of a [source] that names them, and how a [source] resolves to [concrete packages][concrete package]. The format of `manifest.json` is defined by the JSON Schema [`manifest.schema.json`](manifest.schema.json).

## Conventions

The key words "MUST", "MUST NOT", "REQUIRED", "SHOULD", "SHOULD NOT", "RECOMMENDED", "MAY" and "OPTIONAL" are to be interpreted as described in BCP 14 ([RFC 2119], [RFC 8174]) when, and only when, they appear in all capitals.

Grammars use ABNF ([RFC 5234]). A rule written as `<...>` is defined by the prose inside the brackets.

## 1. Terms

Each term below is defined using only the terms before it.

- <a id="t-ascii-ci"></a>**ASCII case-insensitive**: two strings are equal ASCII case-insensitively when they are equal after every character `A`–`Z` in both is replaced by the corresponding character `a`–`z`.
- <a id="t-archive"></a>**archive**: a tree of regular files and directories, each named by its path from the root of the tree. An [archive] has one of these forms:
  - <a id="t-zip-archive"></a>**ZIP archive**: a file in the ZIP format ([APPNOTE]). Its entry paths MUST be separated by `/`, MUST NOT start with `/`, MUST NOT contain a `..` segment or `\`, and are decoded as UTF-8. Each entry MUST be a regular file or a directory. Whether a file is a [ZIP archive] depends only on its content, not on its file name.
  - <a id="t-directory-archive"></a>**directory archive**: a directory, taken as the [archive] of its contents. It is the unpacked form of a [ZIP archive].
  - <a id="t-git-archive"></a>**git archive**: the tree of a [git commit][git-objects]. It contains only the regular files (modes `100644` and `100755`) and the trees of the commit; symbolic links and submodules are not part of it.
- <a id="t-manifest"></a>**manifest**: the file `manifest.json` at the root of an [archive]. A [manifest] MUST be valid against [`manifest.schema.json`](manifest.schema.json).
- <a id="t-package"></a>**package**: an [archive] that has a [manifest].
- <a id="t-meta-package"></a>**meta package**: a [package] whose [manifest] has the key `targets`. It names other [packages][package] (§4).
- <a id="t-concrete-package"></a>**concrete package**: a [package] whose [manifest] does not have the key `targets`. It is what gets installed.
- <a id="t-id"></a>**id**: a string that matches the regular expression `^[A-Za-z0-9][A-Za-z0-9._-]*$`. Two [ids][id] are equal when they are equal [ASCII case-insensitively][ASCII case-insensitive].
- <a id="t-version"></a>**version**: a string that is a version of [SemVer 2.0.0], without a leading `v`.
- <a id="t-range"></a>**range**: a string with the syntax and the matching semantics of a [node-semver] range, as `semver.satisfies` applies it without the options `loose` and `includePrerelease`. A [version] *satisfies* a [range] when the [range] matches it.
- <a id="t-variant"></a>**variant**: the string `""`, or a string that matches the regular expression `^[a-z0-9_]+(/[a-z0-9_]+)?$`.
- <a id="t-platform"></a>**platform**: the string `"server"` or the string `"client"`.
- <a id="t-runtime"></a>**runtime**: a .NET runtime identifier ([RID]).
- <a id="t-environment"></a>**environment**: a pair `(platform, runtime)` of a [platform] and a [runtime], for which [packages][package] are resolved.

## 2. Sources

<a id="t-source"></a>A **source** is a string that names [packages][package]. It has one of four forms:

| Form | Names | May be |
|---|---|---|
| [Remote repository][remote repository] | A git repository; one [git archive] per selected commit | A [meta package] only |
| [URL][url source] | A [ZIP archive] downloaded over HTTPS | A [meta package] or a [concrete package] |
| [Local file][local file] | A [ZIP archive] on the local file system | A [meta package] or a [concrete package] |
| [Local directory][local directory] | A [directory archive] on the local file system | A [meta package] or a [concrete package] |

Its syntax is:

```
source        = repo-source / url-source / local-source
repo-source   = repo [ "#" variant ] [ "@" ref ]
url-source    = url [ "#" variant ]
local-source  = local-path [ "#" variant ]
repo          = <a Go module path, see §2.1>
url           = "https://" <the rest of an absolute URI, RFC 3986>
local-path    = <a path, see §2.3>
variant       = <a variant other than "">
ref           = version / "branch:" name / "tag:" name / "rev:" commit
version       = <a version>
name          = <a git ref name, see §2.1>
commit        = 40HEXDIG
```

<a id="t-target"></a>The part of a [source] before its `#` (and before its `@`, for a [remote repository]) is its **target**. The form of a [source] is decided by its [target], in this order: a [target] that starts with `https://` is a [URL][url source]; a [target] that is a local path (§2.3) is a [local file] or a [local directory], by what it names; any other [target] is a [remote repository].

<a id="t-source-variant"></a>The part after the `#` is the **variant of the source**; a [source] without `#` has the [variant] `""`. A [source] with `#` followed by nothing is invalid. A [remote repository] is first split at its first `@`, and the part before it at its first `#`. A [URL][url source] is split at its first `#`, and a local path at its last `#`.

A [source] with any other URI scheme, such as `http:` or `file:`, is invalid.

### 2.1 Remote Repositories

<a id="t-remote-repository"></a>A **remote repository** names the git repository at `https://<repo>.git`. `<repo>`, such as `github.com/owner/repo`, MUST be a valid Go module path ([module.CheckPath]). Two `<repo>` are equal when they are equal [ASCII case-insensitively][ASCII case-insensitive].

<a id="t-version-tag"></a>A **version tag** of the repository is a tag named `v` followed by a [version]. Tags of any other name are not [version tags][version tag].

<a id="t-ref"></a>The **ref** after `@` selects commits:

| Ref | Selects |
|---|---|
| none | The commit of every [version tag] |
| `<version>` | The commit of the tag `v<version>` |
| `branch:<name>` | The commit `refs/heads/<name>` points to |
| `tag:<name>` | The commit `refs/tags/<name>` points to, for a tag of any name |
| `rev:<commit>` | The commit with that id, compared case-insensitively; an abbreviated id is invalid |

An annotated tag is peeled to its commit. `<name>` MUST be a valid ref name as defined by [git check-ref-format]. A [ref] other than these forms is invalid.

<a id="t-tag-version"></a>The **tag version** of a selected commit is the [version] V when the commit was selected through the tag `vV`: with no [ref], with the [ref] `V`, or with the [ref] `tag:vV`. Otherwise the commit has no [tag version].

The [git archive] of each selected commit MUST be a [meta package]. When it has a [tag version] and its [manifest] has the key `version`, that `version` MUST equal the [tag version].

### 2.2 URLs

<a id="t-url-source"></a>A **URL** names the [ZIP archive] that is the response body of an HTTPS GET of the [target], following redirects. The [target] MUST be an absolute URI ([RFC 3986]).

### 2.3 Local Paths

<a id="t-local-file"></a>A **local file** is a local path that names a file, which MUST be a [ZIP archive]. <a id="t-local-directory"></a>A **local directory** is a local path that names a directory, which is a [directory archive].

Inside a [manifest], a local path MUST start with `./`, and is relative to the root of the [archive] that contains the [manifest]. After `./` comes a non-empty path of segments separated by `/`, optionally ending with `/`. It MUST NOT contain an empty segment other than the trailing one, a `.` segment, a `..` segment or `\`. A local path inside a [manifest] names a file or directory in that [archive], not on the local file system.

Outside a [manifest], a local path names a file or directory on the local file system; how it is written is not defined by this document.

## 3. Versions and Variants

The <a id="t-package-version"></a>**version of a package** is the [tag version] of its [git archive], when it has one, and otherwise the `version` of its [manifest]. A [concrete package] always has a version.

The [variant of a source][source variant] selects among the `targets` of a [meta package] (§4). A [source] with a [variant] other than `""` names no [concrete package] directly.

## 4. Resolution

<a id="t-candidate"></a>Resolve(source, base, range, environment) is the ordered sequence of **candidates**, [concrete packages][concrete package], that `source` names for `environment`. `base` is the [archive] that contains the [manifest] holding `source`, when there is one; local paths inside a [manifest] are resolved against it (§2.3). An omitted `range` matches every [version].

<a id="t-chain"></a>The **chain** of a [candidate] is the sequence of [sources][source] followed to reach it, starting with `source`.

1. **Archives.** The [archives][archive] that `source` names (§2), in this order:
   - a [remote repository] without a [ref]: the commit of each [version tag] whose [version] satisfies `range`, from the highest to the lowest [SemVer 2.0.0] precedence, and at equal precedence by tag name, the lexicographically greater first;
   - any other [source]: its single [archive].

   An [archive] that cannot be obtained, or that is not a [package], contributes no [candidate].

2. **Meta packages.** For a [meta package]:
   - Of its `targets`, keep each whose `variant` (default `""`) equals the [variant of the source][source variant], and whose `platform` and `runtime`, when present, contain the corresponding component of `environment`. A `platform` or `runtime` written as a single value contains only that value.
   - Of those, select the one that writes the most of `platform` and `runtime`. When none is left, or more than one writes the most, the [meta package] contributes no [candidate].
   - The [candidates][candidate] are those of Resolve(s, this [archive], range, environment) for each [source] s of the `source` of the selected target, concatenated in order.
   - In `source`, `{{id}}` and `{{version}}` are replaced by the value of the member of the same name in the selected target, or else in the [manifest], `version` being the [version of the package][package version] for a [git archive] with a [tag version]. `{{platform}}` and `{{runtime}}` are replaced by the components of `environment`. A [source] whose template has no value contributes no [candidate]. Templates are replaced before the [source] is parsed.

3. **Concrete packages.** A [concrete package] is the single [candidate] when all of the following hold, and contributes no [candidate] otherwise:
   - the [variant of the source][source variant] is `""`;
   - `platform` of its [manifest] is omitted, or equals the [platform] of `environment`;
   - `runtime` of its [manifest] is omitted, or equals the [runtime] of `environment`;
   - its [version][package version] satisfies `range`.

**Loops.** A [chain] contributes no [candidate] when it has more than 8 [sources][source], or when the same [archive] with the same [variant of the source][source variant] occurs twice in it.

[package]: #t-package
[meta package]: #t-meta-package
[concrete package]: #t-concrete-package
[archive]: #t-archive
[ZIP archive]: #t-zip-archive
[directory archive]: #t-directory-archive
[git archive]: #t-git-archive
[manifest]: #t-manifest
[ASCII case-insensitive]: #t-ascii-ci
[id]: #t-id
[version]: #t-version
[range]: #t-range
[variant]: #t-variant
[platform]: #t-platform
[runtime]: #t-runtime
[environment]: #t-environment
[source]: #t-source
[target]: #t-target
[source variant]: #t-source-variant
[remote repository]: #t-remote-repository
[url source]: #t-url-source
[local file]: #t-local-file
[local directory]: #t-local-directory
[version tag]: #t-version-tag
[ref]: #t-ref
[tag version]: #t-tag-version
[package version]: #t-package-version
[candidate]: #t-candidate
[chain]: #t-chain

[RFC 2119]: https://www.rfc-editor.org/rfc/rfc2119
[RFC 8174]: https://www.rfc-editor.org/rfc/rfc8174
[RFC 5234]: https://www.rfc-editor.org/rfc/rfc5234
[RFC 3986]: https://www.rfc-editor.org/rfc/rfc3986
[APPNOTE]: https://pkware.cachefly.net/webdocs/casestudies/APPNOTE.TXT
[git-objects]: https://git-scm.com/book/en/v2/Git-Internals-Git-Objects
[git check-ref-format]: https://git-scm.com/docs/git-check-ref-format
[module.CheckPath]: https://pkg.go.dev/golang.org/x/mod/module#CheckPath
[SemVer 2.0.0]: https://semver.org/spec/v2.0.0.html
[node-semver]: https://github.com/npm/node-semver#ranges
[RID]: https://learn.microsoft.com/dotnet/core/rid-catalog
