# tooth.json

A lip package is an archive with a `tooth.json` at its root. `tooth.json` names the package, gives
its version, and describes, for each variant, platform and runtime, what lip installs: the
dependencies, the files to place, the mod to install and the commands to run.
The normative definition is the [package protocol](https://github.com/LiteLDev/lip/blob/main/protocol/package.md).

The current format is format version 4, described by the JSON Schema
[`tooth.schema.json`](https://github.com/LiteLDev/lip/blob/main/protocol/tooth.schema.json).
lip also reads `tooth.json` of format versions 1 to 3, see [Earlier Formats](#earlier-formats).

## Archives

A package lives in an **archive**, a tree of files and directories with `tooth.json` at its root:

- a **git archive**: a commit of a git repository, usually the commit of a version tag. Only its
  regular files and directories belong to it (no symbolic links or submodules);
- a **ZIP archive**: a ZIP file, downloaded over HTTPS or on your disk. Whether a file is a ZIP
  archive depends only on its content, not its name. Its entries must be regular files or
  directories with `/`-separated paths that do not start with `/` or contain `..` or `\`;
- a **directory archive**: a directory on your disk, taken as the archive of its contents.

## Example

A mod that is published as a ZIP file attached to each GitHub release, for the server and the
client on Windows x64:

```json
{
  "format_version": 4,
  "format_uuid": "f74a5fda-cb2a-4de8-959f-a1a76bad3102",
  "tooth": "github.com/example/MyMod",
  "version": "1.0.0",
  "info": {
    "name": "My Mod",
    "description": "A short description",
    "tags": ["levilamina", "mod"]
  },
  "variants": {
    "": [
      {
        "platform": "server",
        "runtime": "win-x64",
        "dependencies": {
          "github.com/LiteLDev/LeviLamina": ">=1.0.0 <2.0.0",
          "github.com/example/MyLib": "^0.3.0"
        },
        "assets": [
          {
            "type": "mcmod",
            "urls": ["https://github.com/example/MyMod/releases/download/v1.0.0/MyMod-server-win-x64.zip"]
          }
        ],
        "preserve_files": ["mods/MyMod/config/"]
      },
      {
        "platform": "client",
        "runtime": "win-x64",
        "dependencies": {
          "github.com/LiteLDev/LeviLamina": ">=1.0.0 <2.0.0",
          "github.com/example/MyLib": "^0.3.0"
        },
        "assets": [
          {
            "type": "mcmod",
            "urls": ["https://github.com/example/MyMod/releases/download/v1.0.0/MyMod-client-win-x64.zip"]
          }
        ],
        "preserve_files": ["mods/MyMod/config/"]
      }
    ]
  }
}
```

## Fields

`tooth.json` must be a JSON object. Keys that are not described here are ignored.

### Top-Level

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `format_version` | `int` | Yes | Must be `4`. |
| `format_uuid` | `string` | Yes | Must be `"f74a5fda-cb2a-4de8-959f-a1a76bad3102"`. |
| `tooth` | `string` | Yes | The **tooth path** of the package, a valid [Go module path](https://go.dev/ref/mod#module-path), e.g. `github.com/user/repo`. Compared ignoring ASCII case. |
| `version` | `string` | Yes | The version of the package, a [SemVer 2.0.0](https://semver.org/) version without a leading `v`. |
| `info` | `object` | No | Information for people. See [Info](#info). |
| `variants` | `object` | Yes | Maps each variant name to its entries. See [Variants](#variants). |

For a package in a git repository, `tooth` must be the path of that repository: the
`tooth.json` of `https://github.com/user/repo.git` must have `"tooth": "github.com/user/repo"`.
At a version tag `v<version>`, `version` must equal the tag version. Otherwise the commit is not a
package, and lip does not install it.

### Info

| Field | Type | Description |
| --- | --- | --- |
| `name` | `string` | Display name. |
| `description` | `string` | Short description. |
| `tags` | `string[]` | Tags. |
| `avatar_url` | `string` | Avatar image URL. |

lip does not use `info`.

### Variants

`variants` maps each **variant** name to a non-empty array of **entries**. The variant `""` is the
default; other names match `^[a-z0-9_]+(/[a-z0-9_]+)?$`, e.g. `lua` or `quickjs`. Users select a
variant by appending `#<variant>` to the source, e.g.
`lip install github.com/LiteLDev/LegacyScriptEngine#lua`; a source without `#` selects `""`.

Each entry applies to exactly one **platform** and one **runtime**. When lip resolves a package,
it takes the entry of the selected variant whose `platform` and `runtime` are exactly those of
the workspace [environment](#environment). There are no wildcards: a package that supports
several runtimes has one entry for each. If the variant has no such entry, the package is not a
candidate for that environment. No two entries of the same variant may have the same `platform`
and `runtime`.

### Entry

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `platform` | `string` | Yes | `"server"` or `"client"`. |
| `runtime` | `string` | Yes | A [.NET RID](https://learn.microsoft.com/dotnet/core/rid-catalog), e.g. `win-x64`. |
| `dependencies` | `object` | No | See [Dependencies](#dependencies). |
| `assets` | `array` | No | See [Assets](#assets). |
| `preserve_files` | `string[]` | No | [Globs](#globs) of files that are kept: an existing file is not overwritten on install, and is not deleted on uninstall. |
| `remove_files` | `string[]` | No | [Globs](#globs) of extra paths to delete on uninstall. A matching directory is deleted with everything below it. |
| `hooks` | `object` | No | See [Hooks](#hooks). |

### Dependencies

`dependencies` maps `<tooth path>` or `<tooth path>#<variant>` to a version range:

```json
{
  "dependencies": {
    "github.com/LiteLDev/LeviLamina": ">=1.0.0 <2.0.0",
    "github.com/LiteLDev/LegacyScriptEngine#lua": "^0.9.0"
  }
}
```

lip installs, for each dependency, a version of that repository (and variant) whose version is
in the range, from the repository's version tags, with an entry for the same environment. A
package installed from a git ref, such as `@branch:main`, also satisfies a dependency on the same
path and variant when its version is in the range.

Version ranges use the syntax and semantics of `SemVersionRange.Parse` of
[Semver for .NET 3.0.0](https://github.com/WalkerCodeRanger/semver/tree/v3.0.0) with its default
options, for example `^1.2.0`, `~1.2.0`, `>=1.0.0 <2.0.0`, `1.2.*` or `>=1.0.0 || 0.9.0`. A bare
version such as `1.2.3` matches exactly that version.

### Assets

`assets` lists where the files of the package come from. Each asset has a `type`:

| Type | Fields | Description |
| --- | --- | --- |
| `self` | `placements` (optional) | The archive of the `tooth.json` itself, e.g. the repository at the tag. |
| `zip` | `urls`, `placements` (optional) | A ZIP archive downloaded over HTTPS. |
| `mcmod` | `urls` | A ZIP archive downloaded over HTTPS that holds a LeviLamina mod; it has no `placements`. See [Mods](#mods). |

`urls` is a non-empty array of HTTPS URLs of the same file, tried in order. An entry has at most
one `mcmod` asset. Assets are written in order; when two assets or placements write the same
path, the later one wins.

### Placements

A placement `{ "src": ..., "dest": ... }` writes files of the asset's archive into the workspace.
`src` is a path in the archive, `dest` a path relative to the workspace. Both are `/`-separated
and must not start with `/` or a drive letter, or contain `//`, a `..` segment or `\`. The form
of a placement is decided by `src`:

- **Glob form**: `src` contains `*`, `?` or `[`. Every file matching the glob is written under
  `dest` (`""` or ending with `/`), keeping its path relative to the leading segments of `src`
  without a wildcard. A glob matching no file writes nothing.
- **Directory form**: `src` is `""` or ends with `/`. Every file below `src` is written under
  `dest` (`""` or ending with `/`), at its path relative to `src`. `src` must contain at least one
  file.
- **File form**: otherwise. The file `src` is written to `dest`, which must not end with `/`.
  The file must exist.

```json
{
  "type": "zip",
  "urls": ["https://example.com/tool-1.0.0-win-x64.zip"],
  "placements": [
    { "src": "bin/", "dest": "tools/" },
    { "src": "README.md", "dest": "tools/README.md" },
    { "src": "plugins/*.dll", "dest": "tools/plugins/" }
  ]
}
```

The root `tooth.json` of the package is never written.

### Globs

`preserve_files`, `remove_files` and the glob form of placements use POSIX `fnmatch` patterns,
matched segment by segment: `*` matches any characters except `/` (a leading `.` included), `?`
one character except `/`, and `[...]` one character of a set. A `**` segment matches zero or more
segments; a pattern ending with `/` matches a directory and everything below it. There is no
escape character. A glob must not be empty, start with `/`, or contain `//` or `\`.

`preserve_files` and `remove_files` are matched against paths relative to the workspace, also for
a mod: write `mods/MyMod/config/`, not `config/`. `remove_files` never matches `mods`, `plugins`
or `.lip` at the workspace root, nor anything below `.lip`, and never deletes anything below
`mods` or `plugins` through them.

### Hooks

| Field | Type | Description |
| --- | --- | --- |
| `pre_install` | `string[]` | Commands to run before the files are written. |
| `post_install` | `string[]` | Commands to run after the files are written. |
| `pre_uninstall` | `string[]` | Commands to run before the files are deleted. |
| `post_uninstall` | `string[]` | Commands to run after the files are deleted. |

Commands run in the workspace root with `cmd /c` on Windows and `sh -c` elsewhere. A command
that exits with a non-zero code fails the operation. If an operation fails, lip undoes its file
changes, but not what hook commands already did, so hook commands should be safe to run again.

## Mods

An entry is a **mod** when it has an `mcmod` asset; otherwise the package is a **file package**,
which only places files.

The archive of the `mcmod` asset must have at its root the LeviLamina `manifest.json` of the mod,
a JSON object whose `name` matches `^[A-Za-z0-9][A-Za-z0-9._-]*$`. That `name` is the **mod
name**. lip writes the whole archive into `mods/<mod name>/`, keeping its paths. On a server,
`plugins` is a link to `mods`, so the mod is also found under `plugins/<mod name>/`.

lip checks the `dependencies` of the mod's `manifest.json` against the `dependencies` of the
entry: let D be `[{ "name": N }, ...]` with one member for each dependency of the entry that is
satisfied by an installed mod, N being that mod's name, in the order of the entry's dependencies.

- When `manifest.json` has `dependencies`, it must be exactly D, or the installation fails.
- When it has none, lip writes `manifest.json` into the mod directory with `dependencies` set to
  D.

Dependencies on file packages do not appear in D. Two mods with the same mod name (ignoring ASCII
case) cannot be installed in one workspace.

An entry can have other assets next to its `mcmod` asset, for example a `zip` asset that places
files outside `mods/`.

## Identity

The **identity** of an installed package is its tooth path, followed by `#<variant>` when the
variant is not `""`, e.g. `github.com/LiteLDev/LegacyScriptEngine#lua`. A workspace holds at
most one package of each identity; [`lip list`](../cli/commands/list.md) shows identities, and
[`lip uninstall`](../cli/commands/uninstall.md) accepts them.

The identity comes from the `tooth` of the package, not from where it was installed from: a ZIP
file whose `tooth.json` has `"tooth": "github.com/user/repo"` has the same identity as the
package from that repository, so the two cannot be installed together.

## Environment

The environment of a workspace is a pair `(platform, runtime)`:

- **platform**: `server` or `client`. lip manages server workspaces only, so it always uses
  `server`. Client workspaces are managed by launchers such as LeviLauncher; lip refuses a
  workspace whose [workspace file](lockfiles.md) records `client`.
- **runtime**: `--rid`, else the runtime the workspace file records, else the RID lip itself
  runs on (e.g. `win-x64`).

## Earlier Formats

lip reads `tooth.json` of format versions 1, 2 and 3, as written for lip v0.37.0 and earlier,
but never writes them; convert yours with [`lip migrate`](../cli/commands/migrate.md). Formats 1
and 2 are first read as format 3, as lip v0.37.0 did. A format 3 `tooth.json` is then read, for
the environment of the workspace, as a format 4 `tooth.json`:

- `tooth`, `version` and `info` are kept.
- For each label, the variants with that label whose `platform` is empty or matches the runtime
  (as a RID pattern, e.g. `win-*`) are merged as lip v0.37.0 merged them, into one entry for the
  platform and runtime of the environment. A label without such variants is no variant.
- On a client, a variant labeled `client` is used as the variant `""`.
- The scripts become `hooks`: `pre_install` stays `pre_install`, `install` and then
  `post_install` become `post_install`, `pre_uninstall` and then `uninstall` become
  `pre_uninstall`, and `post_uninstall` stays `post_uninstall`.
- The assets, `preserve_files` and `remove_files` keep the meaning they had in lip v0.37.0,
  including the `tar`, `tgz` and `uncompressed` asset types and placements with `type`.
- At a version tag, the version of the package is the tag version, whatever its `version` says.
- A dependency on the variant `client` of a package whose `tooth.json` is of format 4 names its
  variant `""`.

A package of an earlier format is always a file package. Its identity follows the same rule, e.g.
`github.com/LiteLDev/LegacyScriptEngine#lua`.

The format 3 schema is
[`legacy/v3/tooth.schema.yaml`](https://github.com/LiteLDev/lip/blob/1e023e6/specs/schemas/legacy/v3/tooth.schema.yaml)
(formats 1 and 2: [`legacy/v1/tooth.schema.yaml`](https://github.com/LiteLDev/lip/blob/1e023e6/specs/schemas/legacy/v1/tooth.schema.yaml), [`legacy/v2/tooth.schema.yaml`](https://github.com/LiteLDev/lip/blob/1e023e6/specs/schemas/legacy/v2/tooth.schema.yaml)),
and lip v0.37.0 documented it in its
[Package Manifest](https://github.com/LiteLDev/lip/blob/v0.37.0/docs/concepts/package_manifest.md)
page:

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
