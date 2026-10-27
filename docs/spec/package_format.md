# lip Package Specification (manifest.json / .mcmod)

This specification only **adds** keys on top of the LeviLamina `manifest.json` specification; it does not change or remove the meaning of any of its keys.
LeviLamina ignores the keys added by this specification. "must / must not / may" correspond to MUST / MUST NOT / MAY of RFC 2119.
In every case this specification calls a "failure", lip must abort the current operation and must not modify the workspace.

## 1. Terms

- **Archive**: a file tree, in one of three forms:
  - **ZIP archive**: a ZIP file; called an mcmod when its extension is `.mcmod`.
  - **git archive**: the tree of a commit.
  - **directory archive**: a local directory.
- **Document**: the `manifest.json` at the root of an archive. It must be strict JSON (RFC 8259, no comments) encoded in UTF-8, and its top level must be an object.
- **Routing document**: a document with the `artifacts` key.
- **Concrete document**: a document without the `artifacts` key. An archive whose root is a concrete document is called a **concrete package**.
- **Identity**: the `name` of a concrete package. Names are compared ignoring ASCII case.
- **Workspace**: the root directory lip operates on.
- **Environment**: a pair of values `(platform, rid)`, see §5.1.

## 2. Archives

1. Entry paths in a ZIP archive are separated by `/`, must not start with `/`, must not contain a `..` segment or `\`, and are interpreted as UTF-8. Only two kinds of entries are allowed: regular files and directories.
2. A git archive contains only regular files (mode `100644` / `100755`) and trees. Symbolic links and submodules are not part of the archive.
3. Whether a file is a ZIP archive depends only on its content, not its extension. An mcmod file should use the `.mcmod` extension.
4. When the root of an archive has a `tooth.json`, it is handled according to §9. When it has neither `tooth.json` nor `manifest.json`, the archive is invalid.

## 3. Refs

```
ref     = target [ "#" variant ]          ; split at the first "#"
variant = matches ^[a-z0-9_]+(/[a-z0-9_]+)?$
target  = relative / url / spec
```

A ref without `#` has the variant `""`. A ref with `#` followed by nothing is invalid.

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

Other schemes, including `http:`, `file:` and so on, must not appear in a document.

## 4. Documents

Keys defined by neither this specification nor LeviLamina are ignored. When the value of a key does not have the defined type, the document is invalid and processing fails.
Format markers are described in §4.4.

### 4.1 Routing Documents

| Key | Type | Required | Definition |
|---|---|---|---|
| `artifacts` | Artifact[] | Yes | Non-empty |
| `name` | string | No | Used only in templates |
| `version` | string | No | SemVer 2.0.0, used only in templates |
| `refs` | ref[] | No | Upstream locations, for reference only; not used in resolution |

Other keys in a routing document are not used in resolution. A routing document must carry the format markers (§4.4).

**Artifact:**

| Key | Type | Required | Definition |
|---|---|---|---|
| `variant` | string | No | Defaults to `""`; when non-empty, it must match the variant regex of §3 |
| `platform` | `"server"` \| `"client"` \| a non-empty array of them | No | Omitted means any |
| `rid` | string \| non-empty string array | No | .NET RID; omitted means any |
| `refs` | ref[] | Yes | Non-empty; alternatives in order |

<span v-pre>**Templates**: strings in `refs` may contain `{{name}}`, `{{version}}`, `{{platform}}` and `{{rid}}`, substituted as follows:</span>
- <span v-pre>`{{name}}` and `{{version}}` take the value of the key of the same name in this document; if this document lacks that key, the ref produces no candidates (see §5.2) instead of failing the whole operation.</span>
- <span v-pre>`{{platform}}` and `{{rid}}` take the value of the current environment.</span>
- Templates are substituted first, then the ref is parsed according to §3.
- <span v-pre>When any other `{{`…`}}` appears, the document is invalid.</span>

**Validity**: let A and B be two entries with the same `variant`. The document is invalid if both of the following hold:
1. A and B write the same set of selector keys (`platform` and `rid` are each written in both or in neither);
2. for every written key, the value sets of A and B intersect.

### 4.2 Concrete Documents

| Key | Type | Required | Definition |
|---|---|---|---|
| `name` | string | Yes | Matches `^[A-Za-z0-9][A-Za-z0-9._-]*$` |
| `version` | string | Yes | SemVer 2.0.0 |
| `type` | string | Yes | `"files"` means a **file package**; any other value means a **mod**, with the semantics defined by LeviLamina |
| `platform` | `"server"` \| `"client"` \| `"universal"` | No | Defaults to `"universal"` |
| `rid` | string | No | .NET RID; omitted means any |
| `refs` | ref[] | No | Upstream locations, for reference only; not used in resolution |
| `dependencies` | Dep[] | No | LeviLamina semantics; entries with `refs` are also installed by lip |
| `optionalDependencies` | Dep[] | No | LeviLamina semantics; lip does not install them |
| `conflicts` | Dep[] | No | LeviLamina semantics; lip checks them according to §6 and ignores their `refs` |
| `installDependencies` | InstallDep[] | No | Installed by lip only; LeviLamina does not read them |
| `placements` | Placement[] | No | Allowed only in documents whose `type` is `"files"` |
| `preserveFiles` | glob[] | No | See §7 |
| `removeFiles` | glob[] | No | See §7 |
| `scripts` | Scripts | No | See §7 |

`entry` and the other LeviLamina keys are not used by this specification.

**Substructures:**
- **Dep** = `{ "name": string, "version"?: range, "refs"?: ref[] }`, where `refs`, if present, must be non-empty.
- **InstallDep** = `{ "name"?: string, "version"?: range, "refs": ref[] }`, where `refs` must be non-empty.
- **range**: the same syntax and matching semantics as LeviLamina's `ll::data::VersionRequirement`, except that a full version without an operator (for example `1.2.3`) must not be used. Omitted means any version is accepted.
- **Placement** = `{ "src": string, "dest": string }`.
  - `src` is a path inside the archive, `dest` is a path inside the workspace. Both must not start with `/`, and must not contain a `..` segment, `\` or a drive letter.
  - A value that is `""` or ends with `/` is in **directory form**; otherwise it is in **file form**. `src` and `dest` must have the same form.
- **Scripts** = `{ "preInstall"?, "postInstall"?, "preUninstall"?, "postUninstall"? }`, each value being a `string[]`.
- **glob**: a path pattern. For a mod, it is relative to the mod directory `<modsDir>/<name>/` (see §7); for a file package, it is relative to the workspace root.
  - `*` matches any sequence of characters not containing `/`.
  - `**` forming a segment by itself matches zero or more segments.
  - A pattern ending with `/` matches directories; files under a matched directory are also considered matched by the pattern.
  - There are no other metacharacters.

### 4.3 Documents from Tags

When a document is at the root of the git archive of a spec version and has the `version` key, `version` must equal the version of that tag.

### 4.4 Format Markers

A document of this format carries two format markers: `"formatVersion": 4` and `"formatUuid": "0c6b977a-64ad-441f-9a29-c7f4b196882f"`.
When either marker key is present with another value, or only one of the two marker keys is present, the document is invalid.

A document without the format markers is a plain LeviLamina manifest and is accepted only as a self-contained concrete document:
- if it has the `artifacts` key, the document is invalid;
- the `refs` in its `dependencies` entries and its `installDependencies` are ignored, so it creates no install edges (§6).

## 5. Resolution

### 5.1 Environment

The environment `(platform, rid)` belongs to the workspace and is determined in the following order:

| Component | Order |
|---|---|
| `platform` | 1. Explicitly specified by the user<br>2. The value recorded in the lock<br>3. `"server"` when `bedrock_server.exe` exists at the workspace root<br>4. `"client"` when `Minecraft.Windows.exe` exists<br>5. A failure when none of the above holds |
| `rid` | 1. Explicitly specified by the user<br>2. The value recorded in the lock<br>3. The RID lip itself runs on |

### 5.2 Resolve(ref, base, range) → Candidate Sequence

`base` is the archive of the document containing the ref. `range` is the version constraint given by the caller; omitted means any version.
Resolve produces an **ordered sequence of concrete package candidates**. Below, "no candidate" means this path produces no candidate; it is **not** a failure of the whole operation. lip should report paths with no candidate and the reason.

1. **Get the archive by target.**
   - relative: obtained inside `base` according to §3.
   - url: obtained by downloading.
   - spec: take all tag versions satisfying `range`, ordered by SemVer precedence from highest to lowest; at equal precedence, the lexicographically greater tag name comes first. Run steps 2–4 on the archive of each version in turn, and concatenate the candidates each produces in that order.
   - When the archive cannot be obtained, or is invalid (§2), there is no candidate.
2. **Read the document.** When the document is invalid, there is no candidate.
3. **If it is a routing document:**
   - First select the entries whose `variant` equals the variant of the ref, then keep those matching the environment. An entry matches the environment when the value set of each selector key it writes contains the corresponding value of the environment.
   - Among the matching entries, select the one that writes the most selector keys. When no entry matches, or the entry with the most keys is not unique, there is no candidate.
   - For each r in that entry's `refs`, run Resolve(r, this archive, range) in order, and concatenate their candidates in that order.
4. **If it is a concrete document:** when all of the following hold, produce the single candidate "this package"; when any does not hold, there is no candidate.
   - the variant of the ref is `""`;
   - `platform` equals the `platform` of the environment, or is `"universal"`;
   - `rid` is omitted, or equals the `rid` of the environment;
   - `version` satisfies `range`.

**Additional no-candidate conditions:**
- The chain exceeds 8 hops.
- The same `(archive key, variant)` appears twice in one chain. Archive keys are defined as follows:

| Archive source | Archive key |
|---|---|
| url | The URL without its fragment |
| spec | `spec@version` |
| relative | `archive key of base` + `!` + path |
| Workspace root (see §6) | The absolute path of the workspace root |

The identity is always the `name` of the final concrete package, regardless of the routing documents the chain passes through.

## 6. Dependency Graph

The **requests** of an operation are a number of `(ref, range)` pairs. Relative refs in requests use the workspace root directory (as a directory archive) as base.
lip must find, for the requests, a set S of concrete packages satisfying all of the following conditions; when it cannot, it is a failure.

1. Every request is satisfied by some package in S: that package is one of the candidates obtained by running Resolve on the request.
2. For each **install edge** of each package in S, some package in S satisfies it. The install edges are:
   - `dependencies` entries with `refs`;
   - all `installDependencies` entries.

   A document without the format markers has no install edges (§4.4).

   Satisfying an install edge means both:
   - the package is one of the candidates obtained by Resolve(r, the archive of the package holding the entry, the entry's `version`), where r is one of the refs in the entry's `refs`;
   - when the entry writes `name`, the identity of the package equals that `name`.
3. No two packages in S have the same identity.
4. For each `conflicts` entry of each package in S, S contains no package whose identity equals the entry's `name` and whose version satisfies the entry's `version`.

When more than one S satisfies the conditions, lip should prefer packages that come earlier in the candidate sequences. `dependencies` entries without `refs` and `optionalDependencies` entries take no part in the dependency graph.

## 7. Installation and Uninstallation

**Installation order**: dependencies are installed before their dependents. When dependencies form a cycle, the packages in the cycle are installed in ascending order of the ASCII lowercase form of their identities.

Steps to install a concrete package P:

1. Run each command in `scripts.preInstall` in turn (see "Commands" below).
2. Write files:
   - **mod**: write the whole archive of P into `<modsDir>/<name>/`. `modsDir` is `mods` on both the server and the client.
   - **file package**: execute the `placements` one by one in order.
     - Directory form: write all files under the `src` directory into `dest`, keeping their relative paths.
     - File form: write the `src` file to `dest`.
     - The `manifest.json` at the archive root is never written.
     - If a `src` does not exist in the archive, it is a failure.
3. Run each command in `scripts.postInstall` in turn.

**Write rules:**
- When a file to be written is already owned by another installed package, it is a failure.
- When a file to be written already exists and matches P's `preserveFiles`, the original file is kept and not written.
- Otherwise it is overwritten.
- Files written by P are owned by P.

Steps to uninstall P:
1. Run `scripts.preUninstall`.
2. Delete the files owned by P that do not match `preserveFiles`.
3. Delete the paths matching `removeFiles` that are not owned by another package. Directory patterns are deleted recursively.
4. Delete the directories left empty by steps 2 and 3, but never `mods` or `plugins` themselves.
5. Run `scripts.postUninstall`.

**Update**: equivalent to uninstalling the old version, then installing the new version.

**Server layout**: in a server workspace, `plugins` must be a link to `mods`, so that software still looking in `plugins` keeps working (lip creates a directory junction on Windows and a relative symbolic link on other platforms). A path through `plugins/` and the corresponding path through `mods/` are the same file: ownership is compared, and files are recorded in the lock, in the `mods/` form. Before an operation that modifies the workspace (install, uninstall, update), lip must establish this layout by the following rules:

- When `mods` does not exist, create it.
- When `plugins` does not exist, create a link to `mods`.
- When `plugins` is already a link to `mods`, do nothing.
- When `plugins` is a link to anywhere else, it is a failure.
- When `plugins` is a regular directory, move its entries into `mods`, then delete `plugins` and create the link. When any entry has a namesake in `mods`, it is a failure before any entry is moved.

When establishing the layout fails, the workspace must be left unchanged.

**Commands**: each command is run by the platform shell (`cmd /c` on Windows, `sh -c` on other platforms), with the workspace root as working directory. When any command exits with a non-zero code, it is a failure.

## 8. Lock

For each installed package, lip must record the following in the workspace lock:

- the identity and `version`;
- the environment;
- the resolution chain: the ref of each hop, and the index of the Artifact selected at that hop;
- the identifier of the final archive: the SHA-256 for a ZIP archive, the commit id and path for a git archive;
- the list of files owned by the package (workspace-relative paths; files under `plugins/` are recorded in the `mods/` form, see §7).

When reinstalling from the lock, lip must take the final archive recorded in the lock directly and verify its identifier; it must not go through the resolution chain again.

## 9. tooth.json Compatibility

When `tooth.json` exists at the root of an archive, lip ignores `manifest.json`, handles the archive according to the existing tooth.json v1–v3 specifications, and connects it to this specification by the following rules:

1. **Identity and resolution**: the archive is treated as a concrete package whose identity is the value of the `tooth` key. In step 4 of §5.2, it selects a variant by the v3 rules; when a variant is selected and the version satisfies `range`, it produces the single candidate, otherwise there is no candidate.
2. **The variant of a ref is the v3 label.** When the variant is `""`, the environment `platform` is `"client"`, and a variant with the label `client` exists, `client` is used.
3. **Mapping v3 dependencies**: a dependency key `<path>[#label]` in tooth.json is treated as a ref, and its value as the range (interpreted with the v3 range semantics). If the archive the ref points to uses this specification and the label is `client`, the variant is treated as `""`.

## Appendix: Examples (Non-normative)

The routing document of the repository `github.com/LiteLDev/LegacyMoney` at tag `v0.22.0`:

```json
{
  "formatVersion": 4, "formatUuid": "0c6b977a-64ad-441f-9a29-c7f4b196882f",
  "name": "LegacyMoney",
  "version": "0.22.0",
  "artifacts": [
    { "platform": ["server", "client"], "rid": "win-x64",
      "refs": ["https://github.com/LiteLDev/LegacyMoney/releases/download/v{{version}}/{{name}}-{{platform}}-{{rid}}.mcmod"] }
  ]
}
```

The concrete document at the root of `LegacyMoney-server-win-x64.mcmod`:

```json
{
  "formatVersion": 4, "formatUuid": "0c6b977a-64ad-441f-9a29-c7f4b196882f",
  "name": "LegacyMoney", "entry": "LegacyMoney.dll", "type": "native",
  "version": "0.22.0", "platform": "server", "rid": "win-x64",
  "refs": ["github.com/LiteLDev/LegacyMoney"],
  "dependencies": [
    { "name": "iListenAttentively", "version": "^0.17.0", "refs": ["github.com/MiracleForest/iListenAttentively"] }
  ],
  "installDependencies": [
    { "version": ">=26.51.0 <26.52.0", "refs": ["github.com/LiteLDev/LeviLamina"] }
  ]
}
```

A file package:

```json
{
  "formatVersion": 4, "formatUuid": "0c6b977a-64ad-441f-9a29-c7f4b196882f",
  "name": "PeEditor", "type": "files", "version": "3.9.0", "rid": "win-x64",
  "placements": [ { "src": "PeEditor.exe", "dest": "PeEditor.exe" } ]
}
```

A repository routing to sub-packages by variant:

```json
{
  "formatVersion": 4, "formatUuid": "0c6b977a-64ad-441f-9a29-c7f4b196882f",
  "artifacts": [
    { "variant": "quickjs", "refs": ["./quickjs/"] },
    { "variant": "lua", "refs": ["./lua/"] }
  ]
}
```
