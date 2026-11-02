# lip Package Specification (manifest.json / .mcmod)

This specification defines `manifest.json` documents, how lip resolves them to packages, and how it installs, uninstalls, packs and locks packages. It also defines how lip reads the legacy formats: LeviLamina manifests without the format marker, and `tooth.json` (§10).
"must / must not / may / should" correspond to MUST / MUST NOT / MAY / SHOULD of RFC 2119.
In every case this specification calls a "failure", lip must abort the current operation and must not modify the workspace: a failure detected after the operation started to change the workspace undoes its changes (§7, "Atomicity"; what commands did is the exception).

## 1. Terms

- **Archive**: a file tree, in one of three forms:
  - **ZIP archive**: a ZIP file; called an mcmod when its extension is `.mcmod`.
  - **git archive**: the tree of a commit.
  - **directory archive**: a directory inside another archive (a relative ref ending with `/`, §3).
- **Document**: the `manifest.json` at the root of an archive. It must be strict JSON (RFC 8259, no comments) encoded in UTF-8, no object in it may have two members with the same name, and its top level must be an object.
- **Meta document**: a document with the `targets` key. It only points to other packages (§5.2); its other keys are a template for packing (§8).
- **Package document**: a document without the `targets` key. An archive whose root is a package document is a **package**. A package is a **mod** when its document has the `mod` key, and a **file package** otherwise.
- **Identity**: the `id` of a package. Identities are compared ignoring ASCII case.
- **Workspace**: the root directory lip operates on.
- **Environment**: a pair of values `(platform, runtime)`, see §5.1.

## 2. Archives

1. Entry paths in a ZIP archive are separated by `/`, must not start with `/`, must not contain a `..` segment or `\`, and are interpreted as UTF-8. Only two kinds of entries are allowed: regular files and directories.
2. A git archive contains only regular files (mode `100644` / `100755`) and trees. Symbolic links and submodules are not part of the archive.
3. Whether a file is a ZIP archive depends only on its content, not its extension. An mcmod file should use the `.mcmod` extension.
4. When the root of an archive has a `tooth.json`, it is read according to §10.2. Otherwise, when it has a `manifest.json` without the `format` key, it is read according to §10.1. When it has neither `tooth.json` nor `manifest.json`, the archive is invalid.

## 3. Refs

```
ref     = target [ "#" variant ]                    ; relative and url targets
        / spec [ "#" variant ] [ "@" git-ref ]      ; spec targets
variant = matches ^[a-z0-9_]+(/[a-z0-9_]+)?$
target  = relative / url / spec
git-ref = "branch:" name / "tag:" name / "rev:" commit
commit  = 40 hexadecimal digits (case-insensitive)
```

A relative or url ref is split at the first `#`. A spec ref is first split at the first `@`, everything after which is the git ref, and the part before it is split at the first `#`. A ref without `#` has the variant `""`. A ref with `#` followed by nothing is invalid.

| Target form | Recognized by | Points to |
|---|---|---|
| **relative** | Starts with `./` | A path inside the archive of the current document, see below |
| **url** | Starts with `https://` | An absolute URL (RFC 3986). lip follows redirects; the response body must be a ZIP archive |
| **spec** | Everything else | Must be a valid Go module path (`golang.org/x/mod/module.CheckPath`). Points to the git repository `https://<spec>.git` |

**Rules for relative:**
- After `./` comes a non-empty path separated by `/`; empty segments, `.` segments, `..` segments and `\` must not appear.
- When it ends with `/`, it points to a directory, which is used as a directory archive.
- Otherwise it points to a file, which must be a ZIP archive.

**Rules for spec:**
- Only tags named `v<SemVer 2.0.0>` are versions of the package; all other tags are ignored.
- The archive of a version is the git archive of the commit the tag points to.

**Rules for git-ref** (a spec ref **pinned** to one commit instead of its versions):
- `branch:<name>` names the commit `refs/heads/<name>` points to; `tag:<name>` names the commit `refs/tags/<name>` points to (an annotated tag is peeled to its commit), for any tag name; `rev:<commit>` names that commit. `<name>` must be a valid git ref name component sequence (`git check-ref-format`): non-empty, without control characters, space, `~`, `^`, `:`, `?`, `*`, `[`, `\`, `..` or `@{`, not `@`, not starting or ending with `/`, not ending with `.`, without `//`, and without a `/`-separated component starting with `.` or ending with `.lock`.
- An abbreviated commit id is invalid: resolving one would require the history of the repository.
- When `@` in a spec ref is not followed by a valid git-ref, the ref is invalid; in particular a version is never written after `@` (versions are constrained by `range`, §5.2).
- Only spec refs may have a git-ref: a relative or url ref containing `@branch:`, `@tag:` or `@rev:` is invalid.

Other schemes, including `http:`, `file:` and so on, must not appear in a document.

## 4. Documents

A document of this format has the key `"format": "bf45b1"`. A `manifest.json` without the `format` key is a legacy LeviLamina manifest (§10.1). A document whose `format` has any other value is invalid.

Keys not defined by this specification are ignored. When the value of a key does not have the defined type, the document is invalid.

### 4.1 Keys

| Key | Type | Required | Definition |
|---|---|---|---|
| `format` | string | Yes | `"bf45b1"` |
| `id` | id | In package documents | The identity |
| `version` | string | In package documents | SemVer 2.0.0 |
| `platform` | `"server"` \| `"client"` | No | The platform the package is for; omitted means both |
| `runtime` | string | No | The .NET RID the package is for; omitted means any |
| `info` | Info | No | Information for people; lip does not use it |
| `dependencies` | object mapping id to Dependency | No | See §6 |
| `conflicts` | object mapping id to range | No | See §6 |
| `mod` | Mod | No | Makes the package a mod (§7) |
| `files` | File[] | No | The files of a file package (§7) |
| `archives` | object mapping name to Archive | No | Downloaded archives that `files` take files from (§7) |
| `preserve` | glob[] | No | See §7 |
| `remove` | glob[] | No | See §7 |
| `hooks` | Hooks | No | See §7 |
| `targets` | Target[] | No | Non-empty; makes the document a meta document (§4.2) |

- **id**: a string matching `^[A-Za-z0-9][A-Za-z0-9._-]*$`. The keys of `dependencies` and `conflicts` must be ids too.
- In a package document, `mod` must not appear together with `files` or `archives`, and the `from` of every File must be a key of `archives`.
- **Info** = `{ "name"?: string, "description"?: string, "authors"?: string[], "tags"?: string[], "icon"?: string }`. `icon` is an https URL.
- **Dependency** = a range, or `{ "version"?: range, "source"?: ref | ref[], "mod"?: boolean }`. An array `source` must not be empty; its refs are alternatives in order. A range alone is the same as `{ "version": range }`. `"mod": false` is allowed only together with `source`.
- **Mod** = `{ "loader": string, "entry": string, "passive"?: boolean, "loadAfter"?: (id | "*")[], "loadBefore"?: (id | "*")[], "extra"?: object }`. `loader` and `entry` must not be empty.
- **File** = `{ "from"?: string, "src": string, "dest": string }`. `src` is a path in the package's archive, or in the archive named by `from`; `dest` is a path in the workspace.
  - Both must not start with `/`, and must not contain an empty segment (other than the whole value `""` or a trailing `/`), a `..` segment, `\` or a drive letter.
  - When `src` contains `*`, `?` or `[`, the File is in **glob form**: `src` must be a glob (§4.4) and `dest` must be `""` or end with `/`.
  - Otherwise, when `src` is `""` or ends with `/`, the File is in **directory form**, and `dest` must be `""` or end with `/`.
  - Otherwise the File is in **file form**, and `dest` must not be `""` and must not end with `/`.
- **Archive** = `{ "url": string | string[], "sha256"?: string, "format"?: "zip" | "tar" | "tgz" | "file" }`. Every `url` is an https URL, and an array must not be empty; they are alternatives in order. `sha256` is 64 hexadecimal digits (case-insensitive). The names of archives match `^[A-Za-z0-9][A-Za-z0-9._-]*$`.
- **Hooks** = `{ "preInstall"?, "postInstall"?, "preUninstall"?, "postUninstall"? }`, each value being a `string[]` of commands.
- **range**: a version range with the syntax and matching semantics of node-semver ranges (`semver.satisfies` without the `loose` and `includePrerelease` options): `*`, `x` ranges, `~`, `^`, comparators `<`, `<=`, `>`, `>=`, `=`, hyphen ranges `A - B`, whitespace for AND and `||` for OR. A full version without an operator matches exactly that version, and a prerelease version matches only a comparator set with a prerelease on the same `major.minor.patch`.

LeviLamina reads `id`, `version`, `platform`, `dependencies`, `conflicts` and `mod` of a mod: a dependency without `"mod": false` must be a loaded mod, `loadAfter` and `loadBefore` order the loading, and `"*"` stands for every mod. lip does not use `mod` beyond its presence.

### 4.2 Meta Documents

In a meta document, only `format`, `targets`, `id` and `version` are used in resolution: `id` and `version` take part in templates (§5.2). `id` and `version`, when present, must still be an id and SemVer 2.0.0. All other keys of a meta document are its **template**, used and checked only by packing (§8).

**Target:**

| Key | Type | Required | Definition |
|---|---|---|---|
| `variant` | string | No | Defaults to `""`; when non-empty, it must match the variant regex of §3 |
| `platform` | `"server"` \| `"client"` \| a non-empty array of them | No | Omitted means any |
| `runtime` | string \| non-empty string array | No | .NET RID; omitted means any |
| `source` | ref \| ref[] | Yes | An array must not be empty; alternatives in order |
| any key of §4.1 except `format` and `targets` | its type, or `null` | No | Overrides the template when packing (§8); `null` removes the key |

The selector keys of a Target are `platform` and `runtime`.

**Templates**: refs in `source` may contain `{{id}}`, `{{version}}`, `{{platform}}` and `{{runtime}}`, substituted as follows:
- `{{id}}` and `{{version}}` take the value of the key of the same name in the Target, or else in the document; a `null` in the Target removes it. When there is no value, the ref produces no candidates (§5.2) instead of failing the whole operation.
- `{{platform}}` and `{{runtime}}` take the value of the current environment.
- Templates are substituted first, then the ref is parsed according to §3.
- When any other `{{`…`}}` appears, the document is invalid.

**Validity**: let A and B be two Targets with the same `variant`. The document is invalid if both of the following hold:
1. A and B write the same set of selector keys (`platform` and `runtime` are each written in both or in neither);
2. for every written key, the value sets of A and B intersect.

### 4.3 Documents from Tags

When a document is at the root of the git archive of a spec version and has the `version` key, `version` must equal the version of that tag.
The archive of a pinned spec ref (§3) has a tag version only when its git-ref is `tag:v<SemVer 2.0.0>`; at a branch, a rev or any other tag there is no tag version, and the document's own `version` is used.

### 4.4 Globs

A glob is a path pattern. It must not be empty, must not start with `/`, must not contain an empty segment (other than a trailing `/`) or `\`, and its bracket expressions must be complete.

- A glob is matched against a path segment by segment, with the pattern matching notation of POSIX (IEEE Std 1003.1-2017, Shell Command Language §2.13.1–2.13.2) as `fnmatch` applies it with `FNM_PATHNAME` and without `FNM_PERIOD`: `*` matches any sequence of characters other than `/`, including a leading `.`; `?` matches one character other than `/`; a bracket expression `[...]` matches one character of its set (`[!...]` of the complement), with ranges, character classes (`[:alpha:]` …), collating symbols (`[.c.]`) and equivalence classes (`[=c=]`) of the POSIX locale. A bracket expression starting with `^` is invalid. There is no escape character.
- A segment that is exactly `**` matches zero or more segments.
- A glob ending with `/` matches directories; files below a matched directory are also matched by it.

The globs of `preserve` and `remove` are relative to the mod directory `mods/<id>/` for a mod, and to the workspace root for a file package.

## 5. Resolution

### 5.1 Environment

The environment `(platform, runtime)` belongs to the workspace and is determined in the following order:

| Component | Order |
|---|---|
| `platform` | 1. Explicitly specified by the user<br>2. The value recorded in the lock<br>3. `"server"` when `bedrock_server.exe` exists at the workspace root<br>4. `"client"` when `Minecraft.Windows.exe` exists<br>5. A failure when none of the above holds |
| `runtime` | 1. Explicitly specified by the user<br>2. The value recorded in the lock<br>3. The RID lip itself runs on |

### 5.2 Resolve(ref, base, range) → Candidate Sequence

`base` is the archive of the document containing the ref. `range` is the version constraint given by the caller; omitted means any version.
Resolve produces an **ordered sequence of package candidates**. Below, "no candidate" means this path produces no candidate; it is **not** a failure of the whole operation. lip should report paths with no candidate and the reason.

1. **Get the archive by target.**
   - relative: obtained inside `base` according to §3.
   - url: obtained by downloading.
   - spec: take all tag versions satisfying `range`, ordered by SemVer precedence from highest to lowest; at equal precedence, the lexicographically greater tag name comes first. Run steps 2–4 on the archive of each version in turn, and concatenate the candidates each produces in that order.
   - pinned spec: the git-ref names a commit (§3); within one operation a git-ref names the same commit throughout, even if its branch or tag moves meanwhile. Run steps 2–4 on the git archive of that commit. When the repository has no such branch or tag, or no such commit, there is no candidate. `range` is not used to select the archive; it still applies in step 4.
   - When the archive cannot be obtained, or is invalid (§2), there is no candidate.
2. **Read the document.** When the document is invalid, there is no candidate.
3. **If it is a meta document:**
   - First select the Targets whose `variant` equals the variant of the ref, then keep those matching the environment. A Target matches the environment when the value set of each selector key it writes contains the corresponding value of the environment.
   - Among the matching Targets, select the one that writes the most selector keys. When no Target matches, or the Target with the most keys is not unique, there is no candidate.
   - For each r in that Target's `source`, run Resolve(r, this archive, range) in order, and concatenate their candidates in that order.
4. **If it is a package document:** when all of the following hold, produce the single candidate "this package"; when any does not hold, there is no candidate.
   - the variant of the ref is `""`;
   - `platform` is omitted, or equals the `platform` of the environment;
   - `runtime` is omitted, or equals the `runtime` of the environment;
   - `version` satisfies `range`.

**Additional no-candidate conditions:**
- The chain exceeds 8 hops.
- The same `(archive key, variant)` appears twice in one chain. Archive keys are defined as follows:

| Archive source | Archive key |
|---|---|
| url | The URL without its fragment |
| spec | `spec@version` |
| pinned spec | `spec@rev:<commit>`, with the commit the git-ref resolved to in lowercase |
| relative | `archive key of base` + `!` + path |
| local file (requests only, see §6) | Its absolute `file:` URI (RFC 8089) |

The identity is always the `id` of the final package, regardless of the meta documents the chain passes through.

## 6. Dependency Graph

The **requests** of an operation are a number of `(ref, range)` pairs given by the user. Requests are inside no archive, so they have no `base`: a relative ref in a request has no candidate. Besides the targets of §3, the target of a request may be a **local file**: a ZIP archive on the local file system, named by its absolute path, which may be followed by `#` variant. It is resolved like a url target whose archive is read from that file instead of downloaded. A local directory is not an archive and cannot be requested. Local files never appear in documents.
lip must find, for the requests, a set S of packages satisfying all of the following conditions; when it cannot, it is a failure.

1. Every request is satisfied by some package in S: that package is one of the candidates obtained by running Resolve on the request (or stands in for one, see below).
2. For each **install edge** of each package in S, some package in S satisfies it. The install edges of a package document are its `dependencies` entries with `source`. Satisfying an install edge means both:
   - the package is one of the candidates obtained by Resolve(r, the archive of the package holding the entry, the entry's `version`), where r is one of the refs in the entry's `source`, or stands in for one (see below);
   - the identity of the package equals the key of the entry.

   **Pinned packages stand in for versions**: a package whose chain starts with a pinned spec ref `s#v@g` stands in for the candidates of an unpinned spec ref `s#v` (same spec, compared ignoring ASCII case, and same variant) whose `range` its version satisfies. A pinned ref is satisfied only by its own candidates: a package from `s@branch:a` does not satisfy `s@branch:b`, and a package from a tag version does not satisfy `s@branch:a`. Thus a dependency's `range` constrains the `version` declared at the pinned commit: a pinned package whose version is outside the range does not satisfy the dependency.
3. No two packages in S have the same identity.
4. For each `conflicts` entry of each package in S, S contains no package whose identity equals the key of the entry and whose version satisfies its range.

When more than one S satisfies the conditions, lip should prefer packages that come earlier in the candidate sequences. `dependencies` entries without `source` take no part in the dependency graph.

## 7. Installation and Uninstallation

**Installation order**: dependencies are installed before their dependents. When dependencies form a cycle, the packages in the cycle are installed in ascending order of the ASCII lowercase form of their identities.

Steps to install a package P:

1. Run each command in `hooks.preInstall` in turn (see "Commands" below).
2. Write files:
   - **mod**: write the whole archive of P into `mods/<id>/`.
   - **file package**: execute the `files` one by one in order. The source of a File is the archive named by its `from`, or else the archive of P.
     - File form: write the `src` file to `dest`. If the source has no such file, it is a failure.
     - Directory form: write all files under the `src` directory into `dest`, keeping their paths relative to `src`. If the source has no file under `src`, it is a failure.
     - Glob form: write every file of the source whose path matches `src` into `dest`, keeping its path relative to the **fixed prefix** of `src`: its leading segments that contain none of `*`, `?` and `[`. A glob matching no file writes nothing.
     - The `manifest.json` at the root of the archive of P is never written.
     - When two Files write the same path, the later one wins.
3. Run each command in `hooks.postInstall` in turn.

**Archives**: an Archive is downloaded from the first of its `url` that can be downloaded and, when `sha256` is given, whose content has that SHA-256; when none can, it is a failure. With `format` `"zip"`, `"tar"` (POSIX tar) or `"tgz"` (gzip-compressed tar), the content must be an archive of that format, and its files are the source. Without `format`, the content must be one of these three formats, detected from the content. With `"file"`, the source is the single file whose path is the name of the Archive. §2.1 applies to the paths of ZIP and tar archives, and entries other than regular files and directories are not part of them.

**Write rules:**
- When a file to be written is `.lip` at the workspace root or below it, where lip keeps the lock, it is a failure.
- When a file to be written is already owned by another installed package, it is a failure.
- When a file to be written already exists and matches P's `preserve`, the original file is kept and not written.
- Otherwise it is overwritten.
- Files written by P are owned by P.

Steps to uninstall P:
1. Run `hooks.preUninstall`.
2. Delete the files owned by P that do not match `preserve`.
3. Delete the paths matching `remove` that are not owned by another package. Directory patterns are deleted recursively. `mods` and `plugins` themselves never match a pattern, so nothing below them is deleted through them. `.lip` at the workspace root and everything below it never match a pattern.
4. Delete the directories left empty by steps 2 and 3, but never `mods` or `plugins` themselves.
5. Run `hooks.postUninstall`.

**Update**: equivalent to uninstalling the old version, then installing the new version.

**Server layout**: in a server workspace, `plugins` must be a link to `mods`, so that software still looking in `plugins` keeps working (lip creates a directory junction on Windows and a relative symbolic link on other platforms). A path through `plugins/` and the corresponding path through `mods/` are the same file: ownership is compared, and files are recorded in the lock, in the `mods/` form. Before an operation that modifies the workspace (install, uninstall, update), lip must establish this layout by the following rules:

- When `mods` does not exist, create it.
- When `plugins` does not exist, create a link to `mods`.
- When `plugins` is already a link to `mods`, do nothing.
- When `plugins` is a link to anywhere else, it is a failure.
- When `plugins` is a regular directory, move its entries into `mods`, then delete `plugins` and create the link. When any entry has a namesake in `mods`, it is a failure before any entry is moved.

When establishing the layout fails, the workspace must be left unchanged. Establishing the layout is part of the operation: when a later step of the operation fails, it is undone too.

**Commands**: each command is run by the platform shell (`cmd /c` on Windows, `sh -c` on other platforms), with the workspace root as working directory. When any command exits with a non-zero code, it is a failure.

**Atomicity**: an operation (install, uninstall or update, together with every dependency it installs, replaces or removes) succeeds or fails as a whole.

1. **Early failures change nothing**: when an operation fails for a reason that does not depend on changing the workspace — a resolution or dependency-graph failure (§5, §6), an archive that cannot be obtained, a missing `src`, a file owned by another package, a server layout that cannot be established — no command of the operation runs, and the workspace and the lock are left unchanged. Each step is checked against the workspace as the steps before it leave it (for example, an update installs the new version into the workspace the uninstallation of the old version leaves).
2. **Later failures are undone**: when a step fails while the workspace is being changed (a file cannot be written or deleted, a command exits with a non-zero code, the user cancels the operation), every change the operation made to the workspace is undone: files it created are deleted, files it overwrote or deleted are restored, and directories and the server layout are restored. The lock is left unchanged. The lock reflects an operation only once every step of it has succeeded.
3. **Commands are not undone**: lip cannot undo what a command did. A failing command (`preInstall`, `postInstall`, `preUninstall` or `postUninstall`) fails the operation like any other step, and lip undoes its own changes, including those made before earlier commands of the operation ran; the commands that already ran are neither run again nor reversed. Commands should therefore be safe to run again, and should not assume that the files of the operation stay in place.
4. The paths that uninstallation steps 2–4 delete are those that match before any command of the operation runs: files a command creates are not deleted by `remove`.
5. **Interruption**: when lip is interrupted while changing the workspace (for example, the process is killed), the next operation on the workspace first brings it back to a consistent state: the interrupted operation is either completed, when the lock already reflects it, or undone otherwise.
6. **Concurrency**: two operations must not apply to the same workspace at the same time.

## 8. Packing

Packing turns a meta document M into the package document of one of its Targets T, for a platform p and a runtime r. Pack(M, T, p, r) is the document obtained as follows:

1. Start from M without `targets`.
2. For each key of T other than `variant`, `platform`, `runtime` and `source`: when its value is `null`, remove the key; otherwise set the key to the value of T, replacing the whole value.
3. When T writes `platform`, set `platform` to p; when T writes `runtime`, set `runtime` to r.
4. The result must be a valid package document (§4); otherwise packing fails.

A Target writing `platform` or `runtime` as arrays stands for one package for each combination of their values, and p and r must be among those values. The package of T for p and r should be published where the first ref of `source` points after template substitution (§4.2) with the environment (p, r). Packing a package document gives the document itself.

## 9. Lock

For each installed package, lip must record the following in the workspace lock:

- the identity and `version`;
- the environment;
- the resolution chain: the ref of each hop (including its git-ref, if any), the index of the Target selected at that hop, and for a spec hop the commit its archive was at;
- the identifier of the final archive: the SHA-256 (and the URL or local file it was read from) for a ZIP archive, the SHA-256 and the identifier of the archive containing it for a ZIP archive inside another archive, the commit id and path for a git archive;
- the list of files owned by the package (workspace-relative paths; files under `plugins/` are recorded in the `mods/` form, see §7).

When reinstalling from the lock, lip installs the final archive recorded in the lock, and fails when its identifier does not match; the result does not depend on where the refs of the resolution chain point now. In particular a package from a pinned spec ref is reinstalled at the recorded commit, wherever its branch or tag points now.

**Updating** an explicitly installed package requests the first ref of its chain again: an unpinned spec ref takes its newest version, a `branch:` takes the current head, a `tag:` the commit the tag points to now, and a `rev:` the same commit; a url is requested again (lip may serve it from its download cache, so the content behind a URL should not change), and a local file is read again. The package is replaced when the result differs in identity, version, archive key or archive identifier.

**Earlier formats**: lip accepts the `tooth_lock.json` that lip v0.x left in a workspace, and keeps managing the packages it records: they can be listed, uninstalled and updated. How it does so is not specified; the rest of this section applies to the entries lip records itself.

## 10. Legacy Formats

lip reads the documents below as package documents. It does not write them.

### 10.1 LeviLamina Manifests

A `manifest.json` without the `format` key is a LeviLamina manifest. It is read as a mod with:

- identity: its `name`, which must be an id (§4.1);
- version: its `version`, which must be SemVer 2.0.0;
- `platform`: its `platform` when it is `"server"` or `"client"`; omitted otherwise;
- `conflicts`: its `conflicts` entries, each `{ "name": string, "version"?: range }` (§4.1), an omitted `version` meaning any version;
- no install edges, no `runtime`, no `preserve`, no `remove` and no `hooks`.

When `name` or `version` is missing or not as above, or a `conflicts` entry is not as above, the document is invalid. Its other keys are ignored.

### 10.2 tooth.json

When `tooth.json` exists at the root of an archive, lip ignores `manifest.json` and reads the tooth.json as a file package by the mapping below. From then on the package is resolved, installed, uninstalled and locked like any other package (§5–§9). tooth.json formats 1 and 2 are first migrated to format 3 as the tooth.json specifications define (including their template substitution); the mapping is defined on format 3.

1. **Identity and version**: the identity is the **tooth name**, followed by `-<label>` with every `/` of the label replaced by `-` when the label selected by rule 2 is not `""`. The tooth name is the last `/`-separated element of the `tooth` path, or the element before it when the last element is a major version suffix (`v` followed by digits, as in `github.com/o/r/v2`). When the identity is not an id (§4.1), there is no candidate. tooth.json installed each labelled variant as a package of its own, and a variant may depend on its sibling variants (a default variant depending on `<tooth>#<label>`), so the variants of one tooth.json have different identities. The version is the value of `version`. In the archive of a spec version (including that of a pinned `tag:v<SemVer 2.0.0>`, §4.3), the version of the tag is used instead and §4.3 does not apply; lip should warn when the two differ. At a branch, a rev or another tag, the value of `version` is used.
2. **Variant selection** (in place of step 4 of §5.2): the variant of the ref is a v3 label, except that `""` means `client` when the environment `platform` is `"client"` and a variant labeled `client` exists. The variants with that label whose `platform` is empty, equals the environment `runtime` or matches it as a glob are merged in order: their `dependencies`, `assets`, `preserve_files` and `remove_files` are concatenated, and each script hook is taken from the one variant that has it. When no variant matches, there is no candidate; when two merged variants declare the same dependency key or the same script hook, it is a failure. The result is a file package without `platform` and `runtime`; when its version satisfies `range`, it is the single candidate, otherwise there is no candidate.
3. **Dependencies**: each dependency `"<path>[#label]": range` becomes an install edge with the single ref `<path>[#label]`, whose range has the v3 semantics, and which any identity satisfies. When the archive the ref points to uses `manifest.json` and the label is `client`, the variant is treated as `""`.
4. **Assets**: asset i of the variant (counting from 0) is mounted at the directory `i` of the package's archive. A `self` asset is the archive of the tooth.json itself. A `zip`, `tar` or `tgz` asset is an archive downloaded from the first of its `urls` that can be downloaded; its format is detected from its content and §2.1 does not apply. An `uncompressed` asset is a single file downloaded the same way, mounted as the file `i`.
5. **Placements**: at install time, every placement of asset i becomes one file-form File for each file of the asset it matches, ordered by asset, then by the file's position in the asset, then by placement. A `file` placement takes the file whose path equals `src`, placed at `dest`; when there is none, every file matching the glob `src` (tooth.json glob syntax), placed at `dest/<file name>`. A `dir` placement takes every file below the directory `src` (`""` and `"."` meaning every file), placed at `dest/<path relative to src>`. `\` in `dest` is read as `/`, and `.` and `..` segments are applied. A placement matching no file places nothing; when two placements write the same path, the later one wins. The rule of §7 that the root `manifest.json` is never written does not apply.
6. **preserve_files and remove_files** become `preserve` and `remove`, with `\` replaced by `/`. A `preserve_files` pattern without `/` is prefixed with `**/`, since v3 also matched it against the file name alone. A `remove_files` pattern not ending with `/` also yields the same pattern followed by `/`, since v3 also removed matching directories. A pattern that is not a valid glob (§4.4), such as one starting with `/`, is dropped.
7. **Scripts**: `pre_install` becomes `preInstall`; `install` followed by `post_install` becomes `postInstall`; `pre_uninstall` followed by `uninstall` becomes `preUninstall`; `post_uninstall` becomes `postUninstall`. Other scripts are not used.
8. **Lock**: the lock records the tooth.json (in format 3, with the version installed) in place of a document. Reinstalling or uninstalling the package from the lock treats it as the file package that recorded tooth.json maps to by this mapping, with the variant of the last hop of the chain and the environment of the lock.

## Appendix: Examples (Non-normative)

The meta document of the repository `github.com/LiteLDev/LegacyMoney` at tag `v0.22.0`:

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

The package document that packing it for `("server", "win-x64")` puts at the root of `LegacyMoney-server-win-x64.mcmod`:

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

A file package taking files from a downloaded archive and from itself:

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

A repository pointing to sub-packages by variant:

```json
{
  "format": "bf45b1",
  "targets": [
    { "variant": "quickjs", "source": "./quickjs/" },
    { "variant": "lua", "source": "./lua/" }
  ]
}
```
