# lip-package(7)

## NAME

lip-package - archives, packages, sources and resolution

## DESCRIPTION

### Terms

- **ASCII case-insensitive**: Two strings are equal ASCII case-insensitively when equal after `A`–`Z` is replaced by `a`–`z` in both.
- **archive**: A tree of regular files and directories, each named by its path from the root; one of:
  - **ZIP archive**: a file in the ZIP format ([APPNOTE]), by content, whatever its name. Each entry must be a regular file or directory; entry paths are decoded as UTF-8, must be `/`-separated, and must not be empty, start with `/`, or contain a `..` segment or `\`.
  - **directory archive**: a directory, taken as the archive of its contents. The name of each file and directory below it must not contain `\`.
  - **git archive**: the tree of a git commit ([Git objects]), with only its regular files (modes `100644` and `100755`) and trees. The name of each entry of each of its trees must not be empty, `.` or `..`, or contain `/` or `\`.
- **package file**: The TOML file `lip.toml` at the root of an archive, read with its templates rendered ([lip.toml(5)]).
- **package**: An archive whose package file is valid ([lip.toml(5)]), or one that [lip-compatibility(7), "tooth.json"](lip-compatibility.md#toothjson) reads as such; in the remote repository of "The server", only what that section defines.
- **package id**: The `id` of a package, a [Go module path], compared ASCII case-insensitively.
- **version**: A [SemVer] version, without a leading `v`.
- **version of a package**: The `version` of its package file.
- **range**: A string that [`SemVersionRange.Parse`] of Semver for .NET 3.0.0 accepts with its default options, with the matching semantics of that range. A version satisfies a range that matches it.
- **variant**: `""`, or a string matching `^[a-z0-9_]+(/[a-z0-9_]+)?$`.
- **side**: `"server"` or `"client"`.
- **platform**: A .NET runtime identifier ([RID]).
- **environment**: A pair (side, platform) for which packages are resolved.

### Sources

A **source** is a string naming packages, in one of four forms:

| Form | Syntax | Names |
|---|---|---|
| Remote repository | `<repo>[#<variant>][@<ref>]` | A git repository; a git archive per selected commit |
| URL | `<url>[#<variant>]` | A ZIP archive downloaded over HTTPS |
| Local file | `<local-path>[#<variant>]` | A ZIP archive on the local file system |
| Local directory | `<local-path>[#<variant>]` | A directory archive on the local file system |

| Placeholder | Value |
|---|---|
| `<repo>` | A Go module path; see "Remote repositories". |
| `<url>` | `https://` followed by the rest of an absolute URI ([RFC 3986]). |
| `<local-path>` | A path on the local file system; its syntax is not defined here. |
| `<variant>` | A variant other than `""`. |
| `<ref>` | `<version>`, `branch:<name>`, `tag:<name>` or `rev:<commit>`. |
| `<version>` | A version. |
| `<name>` | A git ref name; see "Remote repositories". |
| `<commit>` | 40 hexadecimal digits. |

The **location** of a source is the part before its `#` (and before its `@`, for a remote repository). By the first match: a location starting with `https://` is a URL; one that is a local path is a local file or local directory, by what it names; any other is a remote repository.

The **variant of the source** is the part after the `#`, or `""` without `#`; a `#` followed by nothing is invalid. A remote repository is split first at its first `@`, then the part before it at its first `#`. A URL is split at its first `#`; a local path at its last `#`.

A source with any other URI scheme is invalid.

### Remote repositories

A **remote repository** names the git repository at `https://<repo>.git`. `<repo>` must be a valid [Go module path], and is compared ASCII case-insensitively.

A **version tag** is a tag named `v` followed by a version.

The **ref** after `@` selects commits:

| Ref | Selects |
|---|---|
| none | The commit of every version tag |
| `<version>` | The commit of the tag `v<version>` |
| `branch:<name>` | The commit `refs/heads/<name>` points to |
| `tag:<name>` | The commit `refs/tags/<name>` points to |
| `rev:<commit>` | The commit with that id, compared case-insensitively |

An annotated tag is peeled to its commit. `<name>` must be a valid ref name ([git-check-ref-format]). Any other ref is invalid.

A commit selected through the tag `vV` (with no ref, the ref `V`, or the ref `tag:vV`) has the **tag version** V; any other selected commit has none.

The git archive of each selected commit must be a package whose package id equals `<repo>`. When it has a tag version, the version of the package must equal it.

### The server

The remote repository `github.com/LiteLDev/bds` names the Bedrock Dedicated Server of Minecraft. `lip` defines its packages itself, and reads no file of the git archive of a commit selected in it.

The **builds file** is `versions.txt` at the root of the git archive of the commit that `refs/heads/main` of that repository points to. A **build** is a line of it, without the white space around it, that is four decimal numbers separated by `.`. The build of a version is the last build whose first three numbers equal the major, minor and patch numbers of that version.

The git archive of a selected commit is a package only when the commit has a tag version V and V has a build B. Its package file then has `format` 4, the `id` `github.com/LiteLDev/bds`, the `version` V, an `info` that is not defined here, and, as `variants[""]`, these two entries:

| Key | First entry | Second entry |
|---|---|---|
| `side` | `"server"` | `"server"` |
| `platform` | `"win-x64"` | `"linux-x64"` |
| `assets` | One asset, with `type` `"zip"`, `urls` `["https://www.minecraft.net/bedrockdedicatedserver/bin-win/bedrock-server-B.zip"]` and `place` `[{ src = "", dest = "" }]`. | The same, with `bin-linux` for `bin-win`. |
| `preserve` | `["server.properties", "allowlist.json", "permissions.json"]` | The same. |
| `purge` | `["bedrock_server.exe.bak", "packet-statistics.txt", "valid_known_packs.json", "behavior_packs/", "config/", "definitions/", "development_behavior_packs/", "development_resource_packs/", "development_skin_packs/", "resource_packs/", "world_templates/"]` | The same, without `"bedrock_server.exe.bak"`. |
| `hooks` | None. | `post_install`: `["chmod +x bedrock_server"]`. |

### URLs

A **URL** names the ZIP archive in the response body of an HTTPS GET of the location, following redirects; the location must be an absolute URI ([RFC 3986]) with a non-empty host.

### Local paths

A **local file** is a local path naming a file, which must be a ZIP archive. A **local directory** is a local path naming a directory, which is a directory archive.

### Resolution

Resolve(source, range, environment) is the ordered sequence of **candidates** that `source` names for `environment`. A candidate is a package together with one of its entries, the **selected entry**. An omitted `range` matches every version.

1. Archives. The archives that `source` names, in this order:
   - a remote repository without a ref: the commit of each version tag whose version satisfies `range`, from highest to lowest [SemVer] precedence, and at equal precedence the lexicographically greater tag name first;
   - any other source: its single archive.

   An archive that cannot be obtained, or is not a package, contributes no candidate.

2. Entries. A package is a candidate, with the entry of `variants[v]` (v the variant of the source) whose `side` and `platform` equal the corresponding components of `environment`, when such an entry exists and the version of the package satisfies `range`. Otherwise it contributes no candidate.

The **identity** of a candidate is its package id, followed by `#` and the variant of the source when that is not `""`.

A member (key k, range R) of the `deps` of the selected entry is a **dependency**; it names the candidates of Resolve(k, R, environment).

A candidate is a **mod** when its selected entry has an asset of type `"mcmod"`. The archive of that asset must have at its root a `manifest.json` that is a [JSON] object whose `name` matches `^[A-Za-z0-9][A-Za-z0-9._-]*$`: the **mod name**, compared ASCII case-insensitively.

## SEE ALSO

[lip.toml(5)], [lip-workspace(7)](lip-workspace.md), [lip-compatibility(7)](lip-compatibility.md)

[lip.toml(5)]: lip.toml.md
[APPNOTE]: https://pkware.cachefly.net/webdocs/casestudies/APPNOTE.TXT
[Git objects]: https://git-scm.com/book/en/v2/Git-Internals-Git-Objects
[git-check-ref-format]: https://git-scm.com/docs/git-check-ref-format
[Go module path]: https://pkg.go.dev/golang.org/x/mod/module#CheckPath
[SemVer]: https://semver.org/spec/v2.0.0.html
[`SemVersionRange.Parse`]: https://github.com/WalkerCodeRanger/semver/tree/v3.0.0
[RID]: https://learn.microsoft.com/dotnet/core/rid-catalog
[RFC 3986]: https://www.rfc-editor.org/info/rfc3986
[JSON]: https://www.rfc-editor.org/info/rfc8259
