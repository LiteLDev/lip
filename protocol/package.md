# Package

## 1. Terminology

- **ASCII case-insensitive**: Two strings are equal ASCII case-insensitively when equal after `A`–`Z` is replaced by `a`–`z` in both.
- **archive**: A tree of regular files and directories, each named by its path from the root; one of:
  - **ZIP archive**: a file in the ZIP format [APPNOTE], by content, whatever its name. Each entry MUST be a regular file or directory; entry paths are decoded as UTF-8, MUST be `/`-separated, and MUST NOT start with `/` or contain a `..` segment or `\`.
  - **directory archive**: a directory, taken as the archive of its contents.
  - **git archive**: the tree of a git commit [GIT-OBJECTS], with only its regular files (modes `100644` and `100755`) and trees.
- **manifest**: The file `manifest.json` at the root of an archive, which MUST be valid against `manifest.schema.json`.
- **package**: An archive that has a manifest.
- **meta package**: A package whose manifest has the key `targets`.
- **concrete package**: Any other package.
- **id**: A string matching `^[A-Za-z0-9][A-Za-z0-9._-]*$`, compared ASCII case-insensitively.
- **version**: A [SEMVER] version, without a leading `v`.
- **version of a package**: The tag version (Section 2.1) of its git archive, if any; otherwise its manifest's `version`.
- **range**: A string with the syntax and matching semantics of a [NODE-SEMVER] range, as `semver.satisfies` applies it without the options `loose` and `includePrerelease`. A version satisfies a range that matches it.
- **variant**: `""`, or a string matching `^[a-z0-9_]+(/[a-z0-9_]+)?$`.
- **platform**: `"server"` or `"client"`.
- **runtime**: A .NET runtime identifier [RID].
- **environment**: A pair (platform, runtime) for which packages are resolved.

## 2. Sources

A **source** is a string naming packages, in one of four forms:

| Form | Names | May be |
|---|---|---|
| Remote repository | A git repository; a git archive per selected commit | Meta package only |
| URL | A ZIP archive downloaded over HTTPS | Any package |
| Local file | A ZIP archive on the local file system | Any package |
| Local directory | A directory archive on the local file system | Any package |

```abnf
source        = repo-source / url-source / local-source
repo-source   = repo [ "#" variant ] [ "@" ref ]
url-source    = url [ "#" variant ]
local-source  = local-path [ "#" variant ]
repo          = <a Go module path, see Section 2.1>
url           = "https://" <the rest of an absolute URI, RFC 3986>
local-path    = <a path, see Section 2.3>
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

The git archive of each selected commit MUST be a meta package. When it has a tag version and its manifest has `version`, that `version` MUST equal the tag version.

### 2.2. URLs

A **URL** names the ZIP archive in the response body of an HTTPS GET of the location, following redirects; the location MUST be an absolute URI [RFC3986].

### 2.3. Local Paths

A **local file** is a local path naming a file, which MUST be a ZIP archive. A **local directory** is a local path naming a directory, which is a directory archive.

Inside a manifest, a local path MUST start with `./`, followed by a non-empty path of `/`-separated segments, optionally ending with `/`, that MUST NOT contain an empty segment other than the trailing one, a `.` or `..` segment, or `\`. It names a file or directory in the archive containing the manifest, relative to its root.

Outside a manifest, a local path names a file or directory on the local file system; its syntax is not defined here.

## 3. Resolution

Resolve(source, base, range, environment) is the ordered sequence of **candidates**, concrete packages, that `source` names for `environment`. `base` is the archive containing the manifest that holds `source`, if any (Section 2.3). An omitted `range` matches every version.

The **chain** of a candidate is the sequence of sources followed to reach it, starting with `source`.

1. Archives. The archives that `source` names, in this order:
   - a remote repository without a ref: the commit of each version tag whose version satisfies `range`, from highest to lowest [SEMVER] precedence, and at equal precedence the lexicographically greater tag name first;
   - any other source: its single archive.

   An archive that cannot be obtained, or is not a package, contributes no candidate.

2. Meta packages. For a meta package:
   - Of its `targets`, keep each whose `variant` (default `""`) equals the variant of the source, and whose `platform` and `runtime`, when present, contain the corresponding component of `environment`. A single value contains only itself.
   - Of those, select the one that writes the most of `platform` and `runtime`. When none is left, or more than one writes the most, the meta package contributes no candidate.
   - The candidates are those of Resolve(s, this archive, range, environment) for each source s of the selected target's `source`, concatenated in order.
   - Before s is parsed, `{{id}}` and `{{version}}` in it are replaced by the member of the same name in the selected target, or else in the manifest, and `{{platform}}` and `{{runtime}}` by the components of `environment`; for a git archive with a tag version, `version` is the version of the package. A source with a template that has no value contributes no candidate.

3. Concrete packages. A concrete package is the single candidate if all of these hold, and none otherwise:
   - the variant of the source is `""`;
   - its manifest's `platform` and `runtime` are each omitted or equal to the corresponding component of `environment`;
   - its version satisfies `range`.

Loops. A chain contributes no candidate when it has more than 8 sources, or when the same archive with the same variant of the source occurs twice in it.
