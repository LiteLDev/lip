# lip Package Protocol

## Abstract

This document defines lip packages, the four forms of a source that names them, and how a source resolves to concrete packages.

## Status of This Memo

This document is part of the lip protocol and is normative. The format of `manifest.json` is defined by the JSON Schema [`manifest.schema.json`](manifest.schema.json).

## Table of Contents

1. [Introduction](#1-introduction)
   1. [Requirements Language](#11-requirements-language)
   2. [Notation](#12-notation)
2. [Terminology](#2-terminology)
3. [Sources](#3-sources)
   1. [Remote Repositories](#31-remote-repositories)
   2. [URLs](#32-urls)
   3. [Local Paths](#33-local-paths)
4. [Versions and Variants](#4-versions-and-variants)
5. [Resolution](#5-resolution)
6. [Security Considerations](#6-security-considerations)
7. [IANA Considerations](#7-iana-considerations)
8. [References](#8-references)
   1. [Normative References](#81-normative-references)

## 1. Introduction

A package is an archive with a manifest. A source names packages; resolving a source for an environment yields an ordered sequence of concrete packages.

### 1.1. Requirements Language

The key words "MUST", "MUST NOT", "REQUIRED", "SHALL", "SHALL NOT", "SHOULD", "SHOULD NOT", "RECOMMENDED", "NOT RECOMMENDED", "MAY", and "OPTIONAL" in this document are to be interpreted as described in BCP 14 [RFC2119] [RFC8174] when, and only when, they appear in all capitals, as shown here.

### 1.2. Notation

Grammars use ABNF [RFC5234]. A rule written as `<...>` is defined by the prose inside the brackets.

## 2. Terminology

- **ASCII case-insensitive**: Two strings are equal ASCII case-insensitively when they are equal after `A`–`Z` is replaced by `a`–`z` in both.
- **archive**: A tree of regular files and directories, each named by its path from the root. An archive is one of:
  - **ZIP archive**: a file in the ZIP format [APPNOTE]. Its entry paths MUST be separated by `/`, MUST NOT start with `/`, MUST NOT contain a `..` segment or `\`, and are decoded as UTF-8. Each entry MUST be a regular file or a directory. Whether a file is a ZIP archive depends only on its content, not on its name.
  - **directory archive**: a directory, taken as the archive of its contents; the unpacked form of a ZIP archive.
  - **git archive**: the tree of a git commit [GIT-OBJECTS], containing only its regular files (modes `100644` and `100755`) and trees. Symbolic links and submodules are not part of it.
- **manifest**: The file `manifest.json` at the root of an archive. It MUST be valid against `manifest.schema.json`.
- **package**: An archive that has a manifest.
- **meta package**: A package whose manifest has the key `targets`. It names other packages (Section 5).
- **concrete package**: A package whose manifest does not have the key `targets`. Only concrete packages are installed.
- **id**: A string matching `^[A-Za-z0-9][A-Za-z0-9._-]*$`. Ids are compared ASCII case-insensitively.
- **version**: A version as defined by [SEMVER], without a leading `v`.
- **range**: A string with the syntax and matching semantics of a [NODE-SEMVER] range, as `semver.satisfies` applies it without the options `loose` and `includePrerelease`. A version satisfies a range when the range matches it.
- **variant**: The string `""`, or a string matching `^[a-z0-9_]+(/[a-z0-9_]+)?$`.
- **platform**: `"server"` or `"client"`.
- **runtime**: A .NET runtime identifier [RID].
- **environment**: A pair (platform, runtime) for which packages are resolved.

## 3. Sources

A **source** is a string that names packages. It has one of four forms:

| Form | Names | May be |
|---|---|---|
| Remote repository | A git repository; one git archive per selected commit | A meta package only |
| URL | A ZIP archive downloaded over HTTPS | A meta or concrete package |
| Local file | A ZIP archive on the local file system | A meta or concrete package |
| Local directory | A directory archive on the local file system | A meta or concrete package |

```abnf
source        = repo-source / url-source / local-source
repo-source   = repo [ "#" variant ] [ "@" ref ]
url-source    = url [ "#" variant ]
local-source  = local-path [ "#" variant ]
repo          = <a Go module path, see Section 3.1>
url           = "https://" <the rest of an absolute URI, RFC 3986>
local-path    = <a path, see Section 3.3>
variant       = <a variant other than "">
ref           = version / "branch:" name / "tag:" name / "rev:" commit
version       = <a version>
name          = <a git ref name, see Section 3.1>
commit        = 40HEXDIG
```

The **target** of a source is the part before its `#` (and before its `@`, for a remote repository). The target decides the form, in this order: a target starting with `https://` is a URL; a target that is a local path (Section 3.3) is a local file or local directory, by what it names; any other target is a remote repository.

The **variant of the source** is the part after the `#`; without `#` it is `""`. A `#` followed by nothing is invalid. A remote repository is split first at its first `@`, then the part before it at its first `#`. A URL is split at its first `#`; a local path at its last `#`.

A source with any other URI scheme, such as `http:` or `file:`, is invalid.

### 3.1. Remote Repositories

A **remote repository** names the git repository at `https://<repo>.git`. `<repo>`, such as `github.com/owner/repo`, MUST be a valid Go module path [GO-MODULE]. Two `<repo>` are equal when they are equal ASCII case-insensitively.

A **version tag** is a tag named `v` followed by a version.

The **ref** after `@` selects commits:

| Ref | Selects |
|---|---|
| none | The commit of every version tag |
| `<version>` | The commit of the tag `v<version>` |
| `branch:<name>` | The commit `refs/heads/<name>` points to |
| `tag:<name>` | The commit `refs/tags/<name>` points to, for a tag of any name |
| `rev:<commit>` | The commit with that id, compared case-insensitively; an abbreviated id is invalid |

An annotated tag is peeled to its commit. `<name>` MUST be a valid ref name [GIT-REF]. Any other ref is invalid.

The **tag version** of a selected commit is the version V when the commit was selected through the tag `vV`: with no ref, the ref `V`, or the ref `tag:vV`. Otherwise the commit has no tag version.

The git archive of each selected commit MUST be a meta package. When it has a tag version and its manifest has `version`, that `version` MUST equal the tag version.

### 3.2. URLs

A **URL** names the ZIP archive that is the response body of an HTTPS GET of the target, following redirects. The target MUST be an absolute URI [RFC3986].

### 3.3. Local Paths

A **local file** is a local path naming a file, which MUST be a ZIP archive. A **local directory** is a local path naming a directory, which is a directory archive.

Inside a manifest, a local path MUST start with `./` and is relative to the root of the archive containing the manifest. After `./` comes a non-empty path of `/`-separated segments, optionally ending with `/`. It MUST NOT contain an empty segment other than the trailing one, a `.` or `..` segment, or `\`. It names a file or directory in that archive, not on the local file system.

Outside a manifest, a local path names a file or directory on the local file system; how it is written is not defined by this document.

## 4. Versions and Variants

The **version of a package** is the tag version of its git archive, if it has one, and otherwise the `version` of its manifest. A concrete package always has a version.

The variant of a source selects among the `targets` of a meta package (Section 5). A source with a variant other than `""` names no concrete package directly.

## 5. Resolution

Resolve(source, base, range, environment) is the ordered sequence of **candidates**, concrete packages, that `source` names for `environment`. `base` is the archive containing the manifest that holds `source`, if any; local paths inside a manifest are resolved against it (Section 3.3). An omitted `range` matches every version.

The **chain** of a candidate is the sequence of sources followed to reach it, starting with `source`.

1. Archives. The archives that `source` names (Section 3), in this order:
   - a remote repository without a ref: the commit of each version tag whose version satisfies `range`, from highest to lowest [SEMVER] precedence, and at equal precedence the lexicographically greater tag name first;
   - any other source: its single archive.

   An archive that cannot be obtained, or is not a package, contributes no candidate.

2. Meta packages. For a meta package:
   - Of its `targets`, keep each whose `variant` (default `""`) equals the variant of the source, and whose `platform` and `runtime`, when present, contain the corresponding component of `environment`. A single value contains only itself.
   - Of those, select the one that writes the most of `platform` and `runtime`. When none is left, or more than one writes the most, the meta package contributes no candidate.
   - The candidates are those of Resolve(s, this archive, range, environment) for each source s of the selected target's `source`, concatenated in order.
   - In `source`, `{{id}}` and `{{version}}` are replaced by the member of the same name in the selected target, or else in the manifest; for a git archive with a tag version, `version` is the version of the package. `{{platform}}` and `{{runtime}}` are replaced by the components of `environment`. A source with a template that has no value contributes no candidate. Templates are replaced before the source is parsed.

3. Concrete packages. A concrete package is the single candidate when all of the following hold, and contributes no candidate otherwise:
   - the variant of the source is `""`;
   - its manifest's `platform` is omitted or equals the platform of `environment`;
   - its manifest's `runtime` is omitted or equals the runtime of `environment`;
   - its version satisfies `range`.

Loops. A chain contributes no candidate when it has more than 8 sources, or when the same archive with the same variant of the source occurs twice in it.

## 6. Security Considerations

A source may name an archive obtained over the network. The restrictions on ZIP archive entry paths (Section 2) and on local paths inside a manifest (Section 3.3) keep the files an archive names inside that archive.

## 7. IANA Considerations

This document has no IANA actions.

## 8. References

### 8.1. Normative References

- [APPNOTE] PKWARE Inc., "APPNOTE.TXT - .ZIP File Format Specification", <https://pkware.cachefly.net/webdocs/casestudies/APPNOTE.TXT>.
- [GIT-OBJECTS] Chacon, S. and B. Straub, "Pro Git, Section 10.2: Git Objects", <https://git-scm.com/book/en/v2/Git-Internals-Git-Objects>.
- [GIT-REF] "git-check-ref-format", <https://git-scm.com/docs/git-check-ref-format>.
- [GO-MODULE] "golang.org/x/mod/module: CheckPath", <https://pkg.go.dev/golang.org/x/mod/module#CheckPath>.
- [NODE-SEMVER] "node-semver: Ranges", <https://github.com/npm/node-semver#ranges>.
- [RFC2119] Bradner, S., "Key words for use in RFCs to Indicate Requirement Levels", BCP 14, RFC 2119, DOI 10.17487/RFC2119, March 1997, <https://www.rfc-editor.org/info/rfc2119>.
- [RFC3986] Berners-Lee, T., Fielding, R., and L. Masinter, "Uniform Resource Identifier (URI): Generic Syntax", STD 66, RFC 3986, DOI 10.17487/RFC3986, January 2005, <https://www.rfc-editor.org/info/rfc3986>.
- [RFC5234] Crocker, D., Ed. and P. Overell, "Augmented BNF for Syntax Specifications: ABNF", STD 68, RFC 5234, DOI 10.17487/RFC5234, January 2008, <https://www.rfc-editor.org/info/rfc5234>.
- [RFC8174] Leiba, B., "Ambiguity of Uppercase vs Lowercase in RFC 2119 Key Words", BCP 14, RFC 8174, DOI 10.17487/RFC8174, May 2017, <https://www.rfc-editor.org/info/rfc8174>.
- [RID] Microsoft, ".NET RID catalog", <https://learn.microsoft.com/dotnet/core/rid-catalog>.
- [SEMVER] Preston-Werner, T., "Semantic Versioning 2.0.0", <https://semver.org/spec/v2.0.0.html>.
