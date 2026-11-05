# Package Manifest

A lip package is described by a `manifest.json` at the root of its archive, marked by
`"format": "bf45b1"`. The same document is read by LeviLamina, which loads the mods it describes;
lip installs them.
The normative definition is the [package protocol](https://github.com/LiteLDev/lip/blob/main/protocol/package.md).

The format is described by the JSON Schema
[`manifest.schema.json`](https://github.com/LiteLDev/lip/blob/main/protocol/manifest.schema.json).
lip also reads LeviLamina manifests without `format` and the `tooth.json` of lip v0.x, see
[Legacy Formats](#legacy-formats).

## Archives

A package lives in an **archive**, a file tree with `manifest.json` at its root:

- a **git archive**: a commit of a git repository, usually the commit of a version tag;
- a **ZIP archive**: a ZIP file. A ZIP archive with the `.mcmod` extension is called an
  **mcmod**. Whether a file is a ZIP archive depends only on its content, not its extension;
- a **directory archive**: a directory inside another archive (a relative ref ending with `/`).

A local directory on its own is not an archive: to install a package from your disk, pack it
into an `.mcmod` file with [`lip pack`](../cli/commands/pack.md).

`manifest.json` must be strict UTF-8 JSON (no comments, no duplicate keys) with an object at the
top level.

## Example

A mod, as it would be found at the root of `LegacyMoney-server-win-x64.mcmod`:

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

## Package and Meta Documents

Every document has the key `"format": "bf45b1"`. A `manifest.json` without `format` is a
[LeviLamina manifest](#levilamina-manifests); any other value of `format` makes the document
invalid. Keys lip does not know are ignored.

A document with a `targets` key is a **meta document**. It installs nothing itself: it points to
other archives depending on the requested variant and the environment, and its other keys are a
template for [packing](#packing).

A document without `targets` is a **package document**, and its archive is a **package**. A
package is a **mod** when its document has the `mod` key, and a **file package** otherwise. The
`id` of a package is its identity; identities are compared ignoring ASCII case.

## Refs

A ref points to an archive. It has the form `target[#variant]`, and a spec target may be
followed by a git ref: `spec[#variant][@git-ref]`.

| Target | Example | Points to |
| --- | --- | --- |
| Relative directory | `./quickjs/` | A directory inside the archive of the current document. |
| Relative file | `./LegacyMoney.mcmod` | A ZIP file inside the archive of the current document. |
| URL | `https://example.com/pkg.mcmod` | A ZIP archive downloaded over HTTPS (redirects are followed). |
| Spec | `github.com/LiteLDev/LeviLamina` | The git repository `https://<spec>.git`. |
| Spec with a git ref | `github.com/LiteLDev/LeviLamina@branch:main` | One commit of that repository. |

- A relative target starts with `./`, must not contain empty, `.` or `..` segments or `\`.
- A spec must be a valid [Go module path](https://go.dev/ref/mod#module-path). Its versions are
  the tags named exactly `v<version>`, where `<version>` is a
  [SemVer 2.0.0](https://semver.org/) version (e.g. `v1.2.3`). Other tags, such as `v1.2`, are
  ignored.
- A git ref pins a spec to one commit instead of its versions: `@branch:<branch>` (the head of
  the branch), `@tag:<tag>` (any tag, e.g. `@tag:nightly`) or `@rev:<commit>` (a full
  40-digit commit id). It comes after the variant: `github.com/LiteLDev/LegacyScriptEngine#lua@branch:develop`.
  Only spec refs may have one.
- Other schemes, such as `http:` and `file:`, are not allowed.
- The variant must match `^[a-z0-9_]+(/[a-z0-9_]+)?$`. A ref without `#` has the variant `""`.

## Package Document

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `format` | `string` | Yes | `"bf45b1"`. |
| `id` | `string` | Yes | Identity. Must match `^[A-Za-z0-9][A-Za-z0-9._-]*$`. |
| `version` | `string` | Yes | SemVer 2.0.0. At a version tag (`v<version>`), it must equal the tag version. |
| `platform` | `string` | No | `"server"` or `"client"`. Omitted means both. |
| `runtime` | `string` | No | [.NET RID](https://learn.microsoft.com/en-us/dotnet/core/rid-catalog). Omitted means any. |
| `info` | `object` | No | Information for people: `name`, `description`, `authors`, `tags`, `icon` (an https URL). lip does not use it. |
| `dependencies` | `object` | No | Maps ids to [dependencies](#dependency). |
| `conflicts` | `object` | No | Maps ids to version ranges: packages that must not be installed together with this one. |
| `mod` | `object` | No | Makes the package a [mod](#mod). |
| `files` | `array` | No | File packages only. See [File](#file). |
| `archives` | `object` | No | File packages only. Downloaded archives that `files` take files from. See [Archive](#archive). |
| `preserve` | `string[]` | No | [Globs](#globs) of files that are kept when they already exist, and on uninstall. |
| `remove` | `string[]` | No | [Globs](#globs) of extra paths to remove on uninstall. |
| `hooks` | `object` | No | Lifecycle commands. See [Hooks](#hooks). |

`mod` must not appear together with `files` or `archives`.

A package is a candidate only when the ref has no variant, its `platform` is omitted or equals
the environment platform, its `runtime` is omitted or equals the environment runtime, and its
`version` is in the requested range.

### Dependency

A dependency is a version range, or an object:

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `version` | `string` | No | Version range. Omitted means any version. |
| `source` | `string` or `string[]` | No | Where to find the package: one ref, or alternatives tried in order. Non-empty. |
| `mod` | `boolean` | No | `false` when the dependency is not a mod that LeviLamina must load. Allowed only with `source`. |

A range alone, such as `"^1.2.0"`, is the same as `{ "version": "^1.2.0" }`. Only dependencies
with `source` are installed by lip, and the package installed for one must have the id of its
key. LeviLamina requires every dependency without `"mod": false` to be a loaded mod.

Version ranges use the syntax and semantics of [node-semver](https://github.com/npm/node-semver#ranges),
for example `^1.2.0`, `~1.2`, `>=1.0.0 <2.0.0`, `1.x`, `1.0.0 - 1.4.0` or `>=1.0.0 || 0.9.0`. A
bare full version without an operator, such as `1.2.3`, matches exactly that version. A
prerelease version matches only a range that names a prerelease of the same `major.minor.patch`.

To depend on a branch, a tag or a commit rather than on released versions, put a git ref on the
spec in `source`; `version` stays a version range, since LeviLamina reads it too:

```json
{
  "dependencies": {
    "LeviLamina": { "version": ">=1.5.0", "source": "github.com/LiteLDev/LeviLamina@branch:main" }
  }
}
```

The package at the commit the git ref names must declare a `version` in the range, otherwise the
dependency cannot be satisfied. In the other direction, a package installed from a git ref
(as a dependency or by [`lip install`](../cli/commands/install.md#git-refs)) also satisfies a
dependency whose ref is the same spec and variant without a git ref, when its version is in that
dependency's range; so a workspace can run a development build of a library that released mods
depend on.

### Mod

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `loader` | `string` | Yes | The loader of the mod, e.g. `"native"`. |
| `entry` | `string` | Yes | The entry of the mod, e.g. `"LegacyMoney.dll"`. |
| `passive` | `boolean` | No | Read by LeviLamina. |
| `loadAfter` | `string[]` | No | Ids of mods to load before this one; `"*"` stands for every mod. |
| `loadBefore` | `string[]` | No | Ids of mods to load after this one; `"*"` stands for every mod. |
| `extra` | `object` | No | Further data for the loader. |

These keys are read by LeviLamina; lip uses `mod` only to tell a mod from a file package.

### File

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `from` | `string` | No | The name of an [archive](#archive) to take the file from. Omitted means the package's own archive. |
| `src` | `string` | Yes | Path or glob in the source. |
| `dest` | `string` | Yes | Path in the workspace. |

Neither may start with `/` or contain an empty segment, `..`, `\` or a drive letter. A File has
one of three forms:

- **Glob form**: `src` contains `*`, `?` or `[`. Every matching file is written under `dest`
  (`""` or ending with `/`), keeping its path relative to the part of `src` before the first
  segment with a wildcard. A glob matching no file writes nothing.
- **Directory form**: `src` is `""` or ends with `/`. All files under `src` are written under
  `dest` (`""` or ending with `/`).
- **File form**: otherwise. The file `src` is written to `dest`, which must not end with `/`.

A missing `src` file or directory is an error. When two Files write the same path, the later one
wins. The root `manifest.json` of the package is never written.

### Archive

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `url` | `string` or `string[]` | Yes | https URLs, tried in order. |
| `sha256` | `string` | No | The SHA-256 the content must have. |
| `format` | `string` | No | `"zip"`, `"tar"` or `"tgz"`, or `"file"` for a single file whose path is the name of the archive. Omitted means one of the three archive formats, detected from the content. |

The names of archives must match `^[A-Za-z0-9][A-Za-z0-9._-]*$`.

### Globs

Globs are matched segment by segment with the patterns of POSIX `fnmatch`: `*` matches any
characters except `/` (a leading `.` included), `?` one character except `/`, and `[...]` one
character of a set (`[!...]` of its complement). A `**` segment matches zero or more segments; a
pattern ending with `/` matches a directory and everything below it. There is no escape
character. For a mod, globs are relative to the mod directory `mods/<id>/`; for a file package,
to the workspace root.

### Hooks

| Field | Type | Description |
| --- | --- | --- |
| `preInstall` | `string[]` | Commands to run before the files are written. |
| `postInstall` | `string[]` | Commands to run after the files are written. |
| `preUninstall` | `string[]` | Commands to run before the files are removed. |
| `postUninstall` | `string[]` | Commands to run after the files are removed. |

Commands run in the workspace root with `cmd /c` on Windows and `sh -c` elsewhere. A command
that exits with a non-zero code fails the operation.

## Mods and File Packages

- A **mod** (with `mod`): the whole archive is written to `mods/<id>/`. On a server, `plugins`
  is a link to `mods`, so the mod is also visible under `plugins/<id>/`. lip installs to server
  workspaces; client workspaces are managed by launchers such as LeviLauncher.
- A **file package** (without `mod`): the `files` are executed in order.

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

Installing a file that another installed package owns is an error.

## Meta Document

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `format` | `string` | Yes | `"bf45b1"`. |
| `targets` | `array` | Yes | Non-empty. See [Target](#target). |
| `id` | `string` | No | Used in templates. |
| `version` | `string` | No | SemVer 2.0.0. Used in templates. |

Any other key of a package document is part of the **template**: lip ignores it when resolving,
and [packing](#packing) completes it into a package document.

### Target

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `variant` | `string` | No | Default `""`. |
| `platform` | `string` or `string[]` | No | `"server"`, `"client"`, or both. Omitted means any. |
| `runtime` | `string` or `string[]` | No | .NET RIDs. Omitted means any. |
| `source` | `string` or `string[]` | Yes | One ref, or alternatives tried in order. Non-empty. |
| any key of a package document except `format` | its type, or `null` | No | Overrides the template when packing; `null` removes the key. |

lip keeps the targets whose `variant` equals the variant of the ref and whose selectors
(`platform`, `runtime`) all contain the current [environment](#environment). Of those, it picks
the one that writes the most selector keys. Two targets with the same variant must not write the
same selector keys with overlapping values.

### Templates

<span v-pre>Refs in `source` may contain `{{id}}`, `{{version}}`, `{{platform}}` and
`{{runtime}}`. `{{id}}` and `{{version}}` take the key of the same name in the target, or else in
the document (if there is none, or the target sets it to `null`, the ref yields no candidate);
`{{platform}}` and `{{runtime}}` are the values of the environment. Any other `{{...}}` makes the
document invalid.</span>

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
      "platform": ["server", "client"],
      "runtime": "win-x64",
      "source": "https://github.com/LiteLDev/LegacyMoney/releases/download/v{{version}}/{{id}}-{{platform}}-{{runtime}}.mcmod"
    }
  ]
}
```

### Packing

Packing turns a meta document into the package document of one target, for a platform and a
runtime:

1. Start from the meta document without `targets`.
2. Every key of the target other than `variant`, `platform`, `runtime` and `source` replaces the
   key of the same name; a `null` removes it.
3. When the target writes `platform`, set `platform` to the packed platform; when it writes
   `runtime`, set `runtime` to the packed runtime.
4. The result must be a valid package document.

Packing the example above for `server` and `win-x64` gives the [example](#example) at the top of
this page, which belongs at the root of `LegacyMoney-server-win-x64.mcmod`, where the target's
`source` points. [`lip pack`](../cli/commands/pack.md) does this and builds the `.mcmod` file.

## Environment

The environment of a workspace is a pair `(platform, runtime)`:

- **platform**: `server` or `client`. lip manages server workspaces only, so it always uses
  `server`. Client workspaces are managed by launchers implementing the same specification,
  such as LeviLauncher; lip refuses a workspace whose [lockfile](lockfiles.md) records `client`.
- **runtime**: `--rid`, else the value in the lockfile, else the RID lip itself runs on (e.g.
  `win-x64`).

## Legacy Formats

lip reads the formats below as package documents, but never writes them; convert yours with
[`lip migrate`](../cli/commands/migrate.md). The exact mappings are §10 of the
[former package format specification](https://github.com/LiteLDev/lip/blob/1e023e6/specs/package_format.md).

### LeviLamina Manifests

A `manifest.json` without `format` is a LeviLamina manifest. lip reads it as a mod whose identity
is its `name` and whose version is its `version` (SemVer 2.0.0); its `platform` is used when it is
`"server"` or `"client"`, and its `conflicts` (`{ "name", "version" }` entries) are respected. lip
installs nothing else for it: its dependencies are not installed, and it has no `runtime`,
`preserve`, `remove` or `hooks`.

### tooth.json

Packages of lip v0.x use `tooth.json` (format versions 1 to 3). lip converts a `tooth.json` in
memory into a file package and handles it like any other package:

- If an archive has both `tooth.json` and `manifest.json`, `tooth.json` wins.
- The variant of a ref is the variant label. When the platform is `client` and the ref has no
  variant, a variant labeled `client` is used if there is one. The variants with that label
  that match the runtime are merged, as in lip v0.x.
- The package's identity is the tooth name, the last element of the `tooth` path (or the one
  before it when the last is a major version suffix such as `v2`), followed by `-<label>` for a
  labeled variant (with `/` in the label replaced by `-`): as in lip v0.x, each labeled variant is
  a package of its own, so `LegacyScriptEngine` and the `LegacyScriptEngine-quickjs` and
  `LegacyScriptEngine-lua` variants it depends on are installed side by side.
- If the `tooth.json` at a tag declares another version than the tag, the tag version wins and
  lip prints a warning.
- The dependencies of the selected variant are installed like dependencies with a `source`, with
  their version ranges read as in lip v0.x; any identity satisfies them.
- Each asset becomes part of the package: `self` is the archive of the `tooth.json` itself (e.g. the repository at the tag), and `zip`, `tar`,
  `tgz` and `uncompressed` assets are downloaded (and cached) when the package is installed. The
  placements are matched against the asset's files at install time, globs included, and write
  the same files lip v0.x wrote.
- `preserve_files`, `remove_files` and the scripts become `preserve`, `remove` and `hooks`
  (`install` runs with `postInstall`, `uninstall` with `preUninstall`). Files are owned, kept and
  removed by the rules of `manifest.json`.
- The lock records the `tooth.json` with `"kind": "tooth"`; lip converts it again to uninstall
  or update the package.

The format 3 schema is
[`legacy/v3/tooth.schema.yaml`](https://github.com/LiteLDev/lip/blob/1e023e6/specs/schemas/legacy/v3/tooth.schema.yaml)
(formats 1 and 2: [`legacy/v1/tooth.schema.yaml`](https://github.com/LiteLDev/lip/blob/1e023e6/specs/schemas/legacy/v1/tooth.schema.yaml), [`legacy/v2/tooth.schema.yaml`](https://github.com/LiteLDev/lip/blob/1e023e6/specs/schemas/legacy/v2/tooth.schema.yaml)):

```json
{
  "format_version": 3,
  "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
  "tooth": "github.com/user/repo",
  "version": "1.0.0",
  "info": { "name": "My Package", "description": "A short description" },
  "variants": [
    {
      "label": "",
      "platform": "win-x64",
      "dependencies": { "github.com/other/package": ">=1.0.0" },
      "assets": [
        {
          "type": "self",
          "placements": [{ "type": "dir", "src": "bin/", "dest": "bin/" }]
        }
      ],
      "preserve_files": ["config/*.json"],
      "remove_files": ["temp/"],
      "scripts": { "post_install": ["echo installed"] }
    }
  ]
}
```

A variant's `platform` is a .NET RID pattern, its `dependencies` map package paths to
[semver ranges](https://github.com/WalkerCodeRanger/semver#version-ranges), and its `assets`
are `self` (files of the repository), `zip`, `tar`, `tgz` or `uncompressed` (downloaded from
`urls`). Scripts are `pre_install`, `install`/`post_install`, `pre_uninstall`/`uninstall` and
`post_uninstall`.
