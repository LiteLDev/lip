# Package

## 1. Terminology

- **ASCII case-insensitive**: Two strings are equal ASCII case-insensitively when equal after `A`–`Z` is replaced by `a`–`z` in both.
- **archive**: A tree of regular files and directories, each named by its path from the root; one of:
  - **ZIP archive**: a file in the ZIP format [APPNOTE], by content, whatever its name. Each entry MUST be a regular file or directory; entry paths are decoded as UTF-8, MUST be `/`-separated, and MUST NOT start with `/` or contain a `..` segment or `\`.
  - **directory archive**: a directory, taken as the archive of its contents.
  - **git archive**: the tree of a git commit [GIT-OBJECTS], with only its regular files (modes `100644` and `100755`) and trees.
- **tooth file**: The file `tooth.json` at the root of an archive.
- **package**: An archive whose tooth file is valid against `tooth.schema.json` and satisfies Section 3, or is of an earlier format (Section 5).
- **tooth path**: The `tooth` of a package, a Go module path [GO-MODULE], compared ASCII case-insensitively.
- **tooth name**: The last `/`-separated element of a tooth path, or the element before it when the last is `v` followed by one or more ASCII digits.
- **id**: A string matching `^[A-Za-z0-9][A-Za-z0-9._-]*$`, compared ASCII case-insensitively.
- **version**: A [SEMVER] version, without a leading `v`.
- **version of a package**: The `version` of its tooth file.
- **range**: A string that `SemVersionRange.Parse` of [SEMVER-NET] accepts with its default options, with the matching semantics of that range. A version satisfies a range that matches it.
- **variant**: `""`, or a string matching `^[a-z0-9_]+(/[a-z0-9_]+)?$`.
- **platform**: `"server"` or `"client"`.
- **runtime**: A .NET runtime identifier [RID].
- **environment**: A pair (platform, runtime) for which packages are resolved.

## 2. Sources

A **source** is a string naming packages, in one of four forms:

| Form | Names |
|---|---|
| Remote repository | A git repository; a git archive per selected commit |
| URL | A ZIP archive downloaded over HTTPS |
| Local file | A ZIP archive on the local file system |
| Local directory | A directory archive on the local file system |

```abnf
source        = repo-source / url-source / local-source
repo-source   = repo [ "#" variant ] [ "@" ref ]
url-source    = url [ "#" variant ]
local-source  = local-path [ "#" variant ]
repo          = <a Go module path, see Section 2.1>
url           = "https://" <the rest of an absolute URI, RFC 3986>
local-path    = <a path on the local file system; its syntax is not defined here>
variant       = <a variant other than "">
ref           = version / "branch:" name / "tag:" name / "rev:" commit
version       = <a version>
name          = <a git ref name, see Section 2.1>
commit        = 40HEXDIG
```

The **location** of a source is the part before its `#` (and before its `@`, for a remote repository). By the first match: a location starting with `https://` is a URL; one that is a local path is a local file or local directory, by what it names; any other is a remote repository.

The **variant of the source** is the part after the `#`, or `""` without `#`; a `#` followed by nothing is invalid. A remote repository is split first at its first `@`, then the part before it at its first `#`. A URL is split at its first `#`; a local path at its last `#`.

A source with any other URI scheme is invalid.

### 2.1. Remote Repositories

A **remote repository** names the git repository at `https://<repo>.git`. `<repo>` MUST be a valid Go module path [GO-MODULE], and is compared ASCII case-insensitively.

A **version tag** is a tag named `v` followed by a version.

The **ref** after `@` selects commits:

| Ref | Selects |
|---|---|
| none | The commit of every version tag |
| `<version>` | The commit of the tag `v<version>` |
| `branch:<name>` | The commit `refs/heads/<name>` points to |
| `tag:<name>` | The commit `refs/tags/<name>` points to |
| `rev:<commit>` | The commit with that id, compared case-insensitively |

An annotated tag is peeled to its commit. `<name>` MUST be a valid ref name [GIT-REF]. Any other ref is invalid.

A commit selected through the tag `vV` (with no ref, the ref `V`, or the ref `tag:vV`) has the **tag version** V; any other selected commit has none.

The git archive of each selected commit MUST be a package whose tooth path equals `<repo>`. When it has a tag version, the version of the package MUST equal it.

### 2.2. URLs

A **URL** names the ZIP archive in the response body of an HTTPS GET of the location, following redirects; the location MUST be an absolute URI [RFC3986] with a non-empty host.

### 2.3. Local Paths

A **local file** is a local path naming a file, which MUST be a ZIP archive. A **local directory** is a local path naming a directory, which is a directory archive.

## 3. Tooth Files

A tooth file of `format_version` 4 MUST also satisfy:

- `tooth` is a valid Go module path [GO-MODULE], and `version` is a version.
- Each key of `dependencies` is `<tooth path>` or `<tooth path>#<variant>`, the variant other than `""`, and each value is a range.
- In each entry, `platform` is a platform and `runtime` is a runtime.
- No two entries of the same variant have the same `platform` and the same `runtime`.
- Each entry has at most one asset whose `type` is `"mcmod"`.
- Each glob of `preserve_files` and `remove_files`, and each `src` in the glob form, is a glob as the schema describes it.

## 4. Resolution

Resolve(source, range, environment) is the ordered sequence of **candidates** that `source` names for `environment`. A candidate is a package together with one of its entries, the **selected entry**. An omitted `range` matches every version.

1. Archives. The archives that `source` names, in this order:
   - a remote repository without a ref: the commit of each version tag whose version satisfies `range`, from highest to lowest [SEMVER] precedence, and at equal precedence the lexicographically greater tag name first;
   - any other source: its single archive.

   An archive that cannot be obtained, or is not a package, contributes no candidate.

2. Entries. A package is a candidate, with the entry of `variants[v]` (v the variant of the source) whose `platform` and `runtime` equal the corresponding components of `environment`, when such an entry exists and the version of the package satisfies `range`. Otherwise it contributes no candidate.

The **identity** of a candidate is its tooth path, followed by `#` and the variant of the source when that is not `""`.

A member (key k, range R) of the `dependencies` of the selected entry is a **dependency**; it names the candidates of Resolve(k, R, environment).

A candidate is a **mod** when its selected entry has an asset of type `"mcmod"`. The archive of that asset MUST have at its root a `manifest.json` that is a JSON object [RFC8259] whose `name` is an id: the **mod manifest**. Its `name` is the **mod name**.

## 5. Earlier Formats

A tooth file with `format_version` 1, 2 or 3 is read as the tooth file of `format_version` 4 that this section derives from it. Formats 1 and 2 are first read as format 3, as lip v0.37.0 [TOOTH-V3] reads them.

A format 3 tooth file T is read, for an environment (p, r), as a tooth file with the `tooth`, `version` and `info` of T, and, for each label L of the variants of T:

- Let V be the variants of T labeled L whose `platform` is `""` or matches r as [TOOTH-V3] matches it. When V is empty, the result has no variant L.
- Otherwise the result maps L to one entry with `"platform": p`, `"runtime": r`, and, from the variants of V merged as [TOOTH-V3] merges them: `dependencies`; `assets`; `preserve_files` and `remove_files`; and `hooks`, whose `pre_install` is `pre_install`, `post_install` is `install` followed by `post_install`, `pre_uninstall` is `pre_uninstall` followed by `uninstall`, and `post_uninstall` is `post_uninstall`.
- When p is `"client"` and T has a variant labeled `"client"`, the variant `""` of the result is the one derived from the label `"client"`.

The assets, `preserve_files` and `remove_files` keep their meaning in [TOOTH-V3]: the asset types `tar`, `tgz` and `uncompressed`, and `placements` with `type`, are allowed, and are carried out as [TOOTH-V3] carries them out. The version of a format 3 package is the tag version of its git archive, when it has one, whatever its `version`.

A dependency of a format 3 tooth file on the variant `client` of a package whose tooth file is of format 4 names the variant `""` of that package.
