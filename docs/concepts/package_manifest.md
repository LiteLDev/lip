# Package Manifest

A lip package is described by a `manifest.json` at the root of its archive. It is the
`manifest.json` of LeviLamina, extended with the keys of lip package format version 4.
LeviLamina ignores the keys lip adds, and lip never changes the meaning of a LeviLamina key.

The format is described by the JSON Schema
[`manifest.v4.schema.json`](https://github.com/LiteLDev/lip/blob/main/schemas/manifest.v4.schema.json).
The older `tooth.json` format is still read, see [Legacy: tooth.json](#legacy-tooth-json).

## Archives

A package lives in an **archive**, a file tree with `manifest.json` at its root:

- a **git archive**: the commit of a version tag of a git repository;
- a **ZIP archive**: a ZIP file. A ZIP archive with the `.mcmod` extension is called an
  **mcmod**. Whether a file is a ZIP archive depends only on its content, not its extension;
- a **directory archive**: a local directory.

`manifest.json` must be strict UTF-8 JSON (no comments) with an object at the top level.

## Example

A mod, as it would be found at the root of `LegacyMoney-server-win-x64.mcmod`:

```json
{
  "formatVersion": 4,
  "formatUuid": "0c6b977a-64ad-441f-9a29-c7f4b196882f",
  "name": "LegacyMoney",
  "entry": "LegacyMoney.dll",
  "type": "native",
  "version": "0.22.0",
  "platform": "server",
  "rid": "win-x64",
  "refs": ["github.com/LiteLDev/LegacyMoney"],
  "dependencies": [
    {
      "name": "iListenAttentively",
      "version": "^0.17.0",
      "refs": ["github.com/MiracleForest/iListenAttentively"]
    }
  ],
  "installDependencies": [
    { "version": ">=26.51.0 <26.52.0", "refs": ["github.com/LiteLDev/LeviLamina"] }
  ]
}
```

## Format Markers

| Field | Type | Description |
| --- | --- | --- |
| `formatVersion` | `int` | Must be `4`. |
| `formatUuid` | `string` | Must be `"0c6b977a-64ad-441f-9a29-c7f4b196882f"`. |

A document with both markers is a format 4 document. A document with neither is a plain
LeviLamina manifest: lip accepts it only as a self-contained concrete package, ignores the
`refs` of its dependencies and its `installDependencies` (it installs nothing else), and
never treats it as a routing document. A document with only one marker, or a wrong value, is
invalid.

## Routing and Concrete Documents

A document with an `artifacts` key is a **routing document**. It installs nothing itself: it
points to other archives depending on the requested variant and the environment.

A document without `artifacts` is a **concrete document**, and its archive is a **concrete
package**. The `name` of a concrete package is its identity; names are compared ignoring
ASCII case.

## Refs

A ref points to an archive. It has the form `target[#variant]`:

| Target | Example | Points to |
| --- | --- | --- |
| Relative directory | `./quickjs/` | A directory inside the archive of the current document. |
| Relative file | `./LegacyMoney.mcmod` | A ZIP file inside the archive of the current document. |
| URL | `https://example.com/pkg.mcmod` | A ZIP archive downloaded over HTTPS (redirects are followed). |
| Spec | `github.com/LiteLDev/LeviLamina` | The git repository `https://<spec>.git`. |

- A relative target starts with `./`, must not contain empty, `.` or `..` segments or `\`.
- A spec must be a valid [Go module path](https://go.dev/ref/mod#module-path). Its versions are
  the tags named exactly `v<version>`, where `<version>` is a
  [SemVer 2.0.0](https://semver.org/) version (e.g. `v1.2.3`). Other tags, such as `v1.2`, are
  ignored.
- Other schemes, such as `http:` and `file:`, are not allowed.
- The variant must match `^[a-z0-9_]+(/[a-z0-9_]+)?$`. A ref without `#` has the variant `""`.

## Routing Document

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `artifacts` | `array` | Yes | Non-empty. See [Artifact](#artifact). |
| `name` | `string` | No | Used only in templates. |
| `version` | `string` | No | SemVer 2.0.0. Used only in templates. |
| `refs` | `string[]` | No | Upstream locations, for reference only. |

### Artifact

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `variant` | `string` | No | Default `""`. |
| `platform` | `string` or `string[]` | No | `"server"`, `"client"`, or both. Omitted means any. |
| `rid` | `string` or `string[]` | No | [.NET RIDs](https://learn.microsoft.com/en-us/dotnet/core/rid-catalog). Omitted means any. |
| `refs` | `string[]` | Yes | Non-empty. Alternatives, tried in order. |

lip keeps the artifacts whose `variant` equals the variant of the ref and whose selectors
(`platform`, `rid`) all contain the current [environment](#environment). Of those, it picks the
one that writes the most selector keys. Two artifacts with the same variant must not write the
same selector keys with overlapping values.

### Templates

<span v-pre>Strings in `refs` may contain `{{name}}`, `{{version}}`, `{{platform}}` and
`{{rid}}`. `{{name}}` and `{{version}}` are the keys of the document (it is an error if the key
is missing); `{{platform}}` and `{{rid}}` are the values of the environment. Any other
`{{...}}` makes the document invalid.</span>

```json
{
  "formatVersion": 4,
  "formatUuid": "0c6b977a-64ad-441f-9a29-c7f4b196882f",
  "name": "LegacyMoney",
  "version": "0.22.0",
  "artifacts": [
    {
      "platform": ["server", "client"],
      "rid": "win-x64",
      "refs": [
        "https://github.com/LiteLDev/LegacyMoney/releases/download/v{{version}}/{{name}}-{{platform}}-{{rid}}.mcmod"
      ]
    }
  ]
}
```

## Concrete Document

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `name` | `string` | Yes | Identity. Must match `^[A-Za-z0-9][A-Za-z0-9._-]*$`. |
| `version` | `string` | Yes | SemVer 2.0.0. At a tag, it must equal the tag version. |
| `type` | `string` | Yes | `"files"` for a [file package](#file-packages-and-mods); any other value is a LeviLamina mod type (e.g. `"native"`). |
| `platform` | `string` | No | `"server"`, `"client"` or `"universal"` (default). |
| `rid` | `string` | No | .NET RID. Omitted means any. |
| `refs` | `string[]` | No | Upstream locations, for reference only. |
| `dependencies` | `array` | No | LeviLamina dependencies. Entries with `refs` are also installed by lip. See [Dependency](#dependency). |
| `optionalDependencies` | `array` | No | LeviLamina optional dependencies. lip does not install them. |
| `conflicts` | `array` | No | Packages that must not be installed together with this one. `refs` are ignored. |
| `installDependencies` | `array` | No | Packages installed by lip only; LeviLamina does not read them. `refs` is required. |
| `placements` | `array` | No | File packages only. See [Placement](#placement). |
| `preserveFiles` | `string[]` | No | Globs of files that are kept when they already exist, and on uninstall. |
| `removeFiles` | `string[]` | No | Globs of extra paths to remove on uninstall. |
| `scripts` | `object` | No | Lifecycle scripts. See [Scripts](#scripts). |

Other LeviLamina keys, such as `entry`, are not used by lip.

A concrete package is a candidate only when the ref has no variant, its `platform` is the
environment platform or `"universal"`, its `rid` is omitted or equals the environment rid,
and its `version` is in the requested range.

### Dependency

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `name` | `string` | Yes (No in `installDependencies`) | Identity the installed package must have. |
| `version` | `string` | No | Version range. Omitted means any version. |
| `refs` | `string[]` | No (Yes in `installDependencies`) | Where to find the package. Non-empty when present. |

Version ranges use the syntax of LeviLamina's `ll::data::VersionRequirement`, for example
`^1.2.0`, `~1.2`, `>=1.0.0 <2.0.0`, `1.x` or `>=1.0.0 || =0.9.0`. A bare full version without
an operator, such as `1.2.3`, is rejected; write `=1.2.3`.

### Placement

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `src` | `string` | Yes | Path in the archive. |
| `dest` | `string` | Yes | Path in the workspace. |

Neither may start with `/` or contain `..`, `\` or a drive letter. A value that is `""` or ends
with `/` is a directory: all files under `src` are written under `dest`. Otherwise it is a
file. `src` and `dest` must be of the same form. The root `manifest.json` is never written.

### Globs

`*` matches any characters except `/`; a `**` segment matches zero or more segments; a pattern
ending with `/` matches a directory and everything below it. For a mod, globs are relative to
the mod directory; for a file package, to the workspace root.

### Scripts

| Field | Type | Description |
| --- | --- | --- |
| `preInstall` | `string[]` | Commands to run before the files are written. |
| `postInstall` | `string[]` | Commands to run after the files are written. |
| `preUninstall` | `string[]` | Commands to run before the files are removed. |
| `postUninstall` | `string[]` | Commands to run after the files are removed. |

Commands run in the workspace root with `cmd /c` on Windows and `sh -c` elsewhere. A command
that exits with a non-zero code fails the operation.

## File Packages and Mods

- A **mod** (any `type` other than `"files"`): the whole archive is written to
  `plugins/<name>/` on a server, or `mods/<name>/` on a client.
- A **file package** (`"type": "files"`): the `placements` are executed in order.

```json
{
  "formatVersion": 4,
  "formatUuid": "0c6b977a-64ad-441f-9a29-c7f4b196882f",
  "name": "PeEditor",
  "type": "files",
  "version": "3.9.0",
  "rid": "win-x64",
  "placements": [{ "src": "PeEditor.exe", "dest": "PeEditor.exe" }]
}
```

Installing a file that another installed package owns is an error.

## Environment

The environment of a workspace is a pair `(platform, rid)`:

- **platform**: `--platform`, else the value in the [lockfile](lockfiles.md), else `server` if
  `bedrock_server.exe` or `bedrock_server_mod.exe` is in the workspace root, else `client` if
  `Minecraft.Windows.exe` is there, else unknown.
- **rid**: `--rid`, else the value in the lockfile, else the RID lip itself runs on (e.g.
  `win-x64`).

An unknown platform is only an error when something needs it: an artifact with a `platform`
selector, a concrete package with platform `server` or `client`, <span v-pre>`{{platform}}`</span>, or installing
a mod. lip then asks you to pass `--platform server` or `--platform client`.

## Legacy: tooth.json

Packages of lip v0.x use `tooth.json` (format versions 1 to 3). lip still reads them, but no
longer writes them; convert yours with [`lip migrate`](../cli/commands/migrate.md).

- If an archive has both `tooth.json` and `manifest.json`, `tooth.json` wins.
- The package's identity is the `tooth` value, and the variant of a ref is the variant label.
  When the platform is `client` and the ref has no variant, a variant labeled `client` is used
  if there is one.
- The dependencies of the selected variant are installed like `installDependencies`.
- If the `tooth.json` at a tag declares another version than the tag, the tag version wins and
  lip prints a warning.

The format 3 schema is
[`manifest.schema.json`](https://github.com/LiteLDev/lip/blob/main/schemas/manifest.schema.json)
(formats 1 and 2: `manifest.v1.schema.json`, `manifest.v2.schema.json`):

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
