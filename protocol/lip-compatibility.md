# lip-compatibility(7)

## NAME

lip-compatibility - the files of formats 1 to 3, and the packages that lip must install

## DESCRIPTION

`lip` reads and migrates the files of formats 1 to 3, written by lip v0.x, as this page defines.

### Packages

`lip` must install the git archive at the highest version tag ([lip-package(7), "Remote repositories"](lip-package.md#remote-repositories)) of each remote repository below, with every variant in every environment that variant applies to, and with its dependencies:

- `github.com/LiteLDev/LeviLamina`
- `github.com/LiteLDev/LegacyScriptEngine`
- `github.com/LiteLDev/LeviAntiCheat`

### `tooth.json`

An archive without a package file whose `tooth.json` at its root is a [JSON] object with `format_version` 1, 2 or 3 is a package, read for an environment as the package file that this section derives from that `tooth.json`. Formats 1 and 2 are first read as format 3, as lip v0.37.0 reads them ([tooth.json v3]: its [package manifest] and the [JSON Schema] of it).

A format 3 `tooth.json` T is read, for an environment (s, r), as a package file with `format` 4, the `tooth` of T as `id`, the version of the package as `version`, the `info` of T except that its `avatar_url` becomes `logo` when it starts with `https://` and is dropped otherwise, and, for each label L of the variants of T:

- Let V be the variants of T labeled L whose `platform`, a RID pattern of [tooth.json v3], is `""` or matches r as [tooth.json v3] matches it. When V is empty, the result has no variant L.
- Otherwise the result maps L to one entry with `"side": s`, `"platform": r`, and, from the variants of V merged as [tooth.json v3] merges them:
  - `deps`: the `dependencies`, as they are;
  - `assets`: each asset with its `type` and `urls`, and with `place` converted from its `placements` as below. When an asset has the type `tar`, `tgz` or `uncompressed`, the result has no variant L;
  - `preserve`: each pattern P of `preserve_files`, `\` replaced by `/`, as P when it contains `/`, and as `**/` followed by P otherwise;
  - `purge`: each pattern P of `remove_files`, `\` replaced by `/`, as P, and also as P followed by `/` when it does not end with `/`;
  - `hooks`, whose `pre_install` is `pre_install`, `post_install` is `install` followed by `post_install`, `pre_uninstall` is `pre_uninstall` followed by `uninstall`, and `post_uninstall` is `post_uninstall`.

A placement (`type`, `src`, `dest`) becomes a member of `place` with, after `\` is replaced by `/` and empty and `.` segments are removed from both paths:

| Placement | `src` | `dest` |
|---|---|---|
| `type` `dir` | `src` followed by `/`, unless empty. | `dest` followed by `/`, unless empty. |
| `type` `file`, `src` containing `*`, `?` or `[` | `src`. | `dest` followed by `/`, unless empty. |
| `type` `file` otherwise | `src`. | `dest`. |

A converted glob or placement that is not valid in a package file ([lip.toml(5)](lip.toml.md)) is left out.

The version of such a package is the tag version of its git archive, when it has one, and otherwise the `version` of T. Installing such a package never writes its `tooth.json`.

### `tooth_lock.json`

A workspace without a state file whose root holds a `tooth_lock.json` that is a JSON object with `format_version` 3 and an array `packages`, each member an object with `manifest` (a `tooth.json` of format 1 to 3), `variant` (a string), `locked` (a boolean) and `files` (an array of strings), has the state file ([state.json(5)](state.json.md)) that this section derives from it, for the environment (s, r) the workspace gets ([lip-workspace(7), "Environment and layout"](lip-workspace.md#environment-and-layout)).

That state file has `format` 4, `side` s, `platform` r and, for each member M of `packages` whose `manifest` "tooth.json" reads, for (s, r), with a variant `variant`, in installation order ([lip-workspace(7), "Operations"](lip-workspace.md#operations)), a package with:

| Member | Value |
|---|---|
| `manifest` | The `manifest` of M as "tooth.json" reads it, its version being its `version`. |
| `variant` | The `variant` of M. |
| `explicit` | The `locked` of M. |
| `ref` | The `tooth` of the `manifest` of M, followed by `#` and the `variant` of M when that is not `""`. |
| `commit` | The commit that the tag `v<version>` of that remote repository points to; when a workspace operation cannot obtain it, it is a failure. |
| `files` | Each member of `files` of M that names a path below the workspace, relative to the workspace when not absolute, as that path relative to the workspace, `/`-separated. |

A workspace operation writes it as its state file ([lip-workspace(7), "Carrying out an operation"](lip-workspace.md#carrying-out-an-operation)), then deletes `tooth_lock.json`.

### `liprc.json`

When the config directory has no `config.toml` and has a `liprc.json` that is a JSON object whose `format_version`, when present, is 3, the configuration ([config.toml(5)](config.toml.md)) is read from it: `github_proxy` takes the value of its `github_proxy` when that is a string. Before `lip` reads the configuration, it writes this configuration atomically as `config.toml`, then deletes `liprc.json`.

### `lip migrate`

`lip migrate` ([lip-migrate(1)](lip-migrate.md)) writes atomically, as `lip.toml` in the current directory, a package file migrated from the `tooth.json` there, then deletes `tooth.json`. When `lip.toml` exists, or `tooth.json` is missing or is not one that "tooth.json" reads, the command fails and changes nothing.

The mapping is not defined here; it should agree with "tooth.json" for every environment. A string of the result that is derived from a string of `tooth.json` holding a template (as [tooth.json v3] renders it) should be written as a template ([lip.toml(5), "Templates"](lip.toml.md#templates)) that keeps it, with the variable `tooth`, or `path` in formats 1 and 2, renamed to `id`, when that template renders to the same string; any other string is written so that it renders to itself. When a value the output needs is missing or invalid, `lip` asks the user for it on the terminal until the answer is valid. When standard input or standard error is not a terminal, the command fails instead, naming the value it would ask for, and changes nothing.

## SEE ALSO

[lip-migrate(1)](lip-migrate.md), [lip.toml(5)](lip.toml.md), [state.json(5)](state.json.md), [config.toml(5)](config.toml.md), [lip-package(7)](lip-package.md)

[JSON]: https://www.rfc-editor.org/info/rfc8259
[tooth.json v3]: https://github.com/LiteLDev/lip/blob/v0.37.0/docs/concepts/package_manifest.md
[package manifest]: https://github.com/LiteLDev/lip/blob/v0.37.0/docs/concepts/package_manifest.md
[JSON Schema]: https://github.com/LiteLDev/lip/blob/v0.37.0/schemas/manifest.schema.json
