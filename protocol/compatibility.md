# Compatibility

`lip` MUST read and migrate the files of formats 1 to 3, written by lip v0.x, as this document defines.

## 1. Packages

`lip` MUST install the git archive at the highest version tag (Section 2.1 of [PACKAGE]) of each remote repository below, with every variant in every environment that variant applies to, and with its dependencies:

- `github.com/LiteLDev/LeviLamina`
- `github.com/LiteLDev/LegacyScriptEngine`
- `github.com/LiteLDev/LeviAntiCheat`

## 2. `tooth.json`

An archive without a package file whose `tooth.json` at its root is a JSON [RFC8259] object with `format_version` 1, 2 or 3 is a package, read for an environment as the package file that this section derives from that `tooth.json`. Formats 1 and 2 are first read as format 3, as lip v0.37.0 [TOOTH-V3] reads them.

A format 3 `tooth.json` T is read, for an environment (s, r), as a package file with `format` 4, the `tooth` of T as `id`, the version of the package as `version`, the `info` of T except that its `avatar_url` becomes `logo` when it starts with `https://` and is dropped otherwise, and, for each label L of the variants of T:

- Let V be the variants of T labeled L whose `platform`, a RID pattern of [TOOTH-V3], is `""` or matches r as [TOOTH-V3] matches it. When V is empty, the result has no variant L.
- Otherwise the result maps L to one entry with `"side": s`, `"platform": r`, and, from the variants of V merged as [TOOTH-V3] merges them:
  - `deps`: the `dependencies`, as they are;
  - `assets`: each asset with its `type` and `urls`, and with `place` converted from its `placements` as below. When an asset has the type `tar`, `tgz` or `uncompressed`, the result has no variant L;
  - `preserve`: each pattern P of `preserve_files`, `\` replaced by `/`, as P when it contains `/`, and as `**/` followed by P otherwise;
  - `purge`: each pattern P of `remove_files`, `\` replaced by `/`, as P, and also as P followed by `/` when it does not end with `/`;
  - `hooks`, whose `pre_install` is `pre_install`, `post_install` is `install` followed by `post_install`, `pre_uninstall` is `pre_uninstall` followed by `uninstall`, and `post_uninstall` is `post_uninstall`.

A placement (`type`, `src`, `dest`) becomes a member of `place` with, after `\` is replaced by `/` and empty and `.` segments are removed from both paths:

- `type` `dir`: `src` and `dest` each followed by `/`, unless empty.
- `type` `file`, `src` containing `*`, `?` or `[`: `src`, and `dest` followed by `/`, unless empty.
- `type` `file` otherwise: `src` and `dest`.

A converted glob or placement that is not valid in a package file is left out.

The version of such a package is the tag version of its git archive, when it has one, and otherwise the `version` of T. Installing such a package never writes its `tooth.json`.

## 3. `tooth_lock.json`

A workspace without a state file whose root holds a `tooth_lock.json` that is a JSON object with `format_version` 3 and an array `packages`, each member an object with `manifest` (a `tooth.json` of format 1 to 3), `variant` (a string), `locked` (a boolean) and `files` (an array of strings), has the state file that this section derives from it, for the environment (s, r) the workspace gets (Section 2 of [WORKSPACE]).

That state file has `format` 4, `side` s, `platform` r and, for each member M of `packages` whose `manifest` Section 2 reads, for (s, r), with a variant `variant`, in installation order (Section 5 of [WORKSPACE]), a package with:

- `manifest`: the `manifest` of M as Section 2 reads it, its version being its `version`;
- `variant`: the `variant` of M;
- `explicit`: the `locked` of M;
- `ref`: the `tooth` of the `manifest` of M, followed by `#` and the `variant` of M when that is not `""`;
- `commit`: the commit that the tag `v<version>` of that remote repository points to; when a workspace operation cannot obtain it, it is a failure;
- `files`: each member of `files` of M that names a path below the workspace, relative to the workspace when not absolute, as that path relative to the workspace, `/`-separated.

A workspace operation writes it as its state file (Section 7.4 of [WORKSPACE]), then deletes `tooth_lock.json`.

## 4. `liprc.json`

When the config directory has no `config.toml` and has a `liprc.json` that is a JSON object whose `format_version`, when present, is 3, the configuration is read from it: `github_proxy` takes the value of its `github_proxy` when that is a string. Before `lip` reads the configuration, it writes this configuration atomically as `config.toml`, then deletes `liprc.json`.

## 5. `lip migrate`

`lip migrate` (Section 5.8 of [CLI]) writes atomically, as `lip.toml` in the current directory, a package file migrated from the `tooth.json` there, then deletes `tooth.json`. When `lip.toml` exists, or `tooth.json` is missing or is not one that Section 2 reads, the command fails and changes nothing.

The mapping is not defined here; it SHOULD agree with Section 2 for every environment. A string of the result that is derived from a string of `tooth.json` holding a template (as [TOOTH-V3] renders it) SHOULD be written as a template (Section 3 of [PACKAGE]) that keeps it, with the variable `tooth`, or `path` in formats 1 and 2, renamed to `id`, when that template renders to the same string; any other string is written so that it renders to itself. When a value the output needs is missing or invalid, `lip` asks the user for it on the terminal until the answer is valid. When standard input or standard error is not a terminal, the command fails instead, naming the value it would ask for, and changes nothing.
