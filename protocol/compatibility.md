# Compatibility

## 1. Packages

`lip` MUST install the git archive at the highest version tag (Section 2.1 of [PACKAGE]) of each remote repository below, with every variant in every environment that variant applies to, and with its dependencies, as Section 2 reads its `tooth.json`:

- `github.com/LiteLDev/LeviLamina`
- `github.com/LiteLDev/LegacyScriptEngine`
- `github.com/LiteLDev/LeviAntiCheat`

## 2. Tooth Files of Formats 1 to 3

A tooth file with `format_version` 1, 2 or 3 is read as the tooth file of `format_version` 4 that this section derives from it. Formats 1 and 2 are first read as format 3, as lip v0.37.0 [TOOTH-V3] reads them.

A format 3 tooth file T is read, for an environment (p, r), as a tooth file with the `tooth` of T, the version of the package as `version`, the `info` of T except that its `avatar_url` becomes `logo` when it starts with `https://` and is dropped otherwise, and, for each label L of the variants of T:

- Let V be the variants of T labeled L whose `platform` is `""` or matches r as [TOOTH-V3] matches it. When V is empty, the result has no variant L.
- Otherwise the result maps L to one entry with `"platform": p`, `"runtime": r`, and, from the variants of V merged as [TOOTH-V3] merges them:
  - `dependencies`, as they are;
  - `assets`: each asset with its `type` and `urls`, and with `place` converted from its `placements` as below. When an asset has the type `tar`, `tgz` or `uncompressed`, the result has no variant L;
  - `preserve`: each pattern P of `preserve_files`, `\` replaced by `/`, as P when it contains `/`, and as `**/` followed by P otherwise;
  - `purge`: each pattern P of `remove_files`, `\` replaced by `/`, as P, and also as P followed by `/` when it does not end with `/`;
  - `hooks`, whose `pre_install` is `pre_install`, `post_install` is `install` followed by `post_install`, `pre_uninstall` is `pre_uninstall` followed by `uninstall`, and `post_uninstall` is `post_uninstall`.
- When p is `"client"` and T has a variant labeled `"client"`, the variant `""` of the result is the one derived from the label `"client"`.

A placement (`type`, `src`, `dest`) becomes a member of `place` with, after `\` is replaced by `/` and empty and `.` segments are removed from both paths:

- `type` `dir`: `src` and `dest` each followed by `/`, unless empty.
- `type` `file`, `src` containing `*`, `?` or `[`: `src`, and `dest` followed by `/`, unless empty.
- `type` `file` otherwise: `src` and `dest`.

A converted glob or placement that is not valid in format 4 is left out.

The version of a package whose tooth file is of format 1, 2 or 3 is the tag version of its git archive, when it has one, and otherwise its `version`.

A dependency of a format 3 tooth file on the variant `client` of a package whose tooth file is of format 4 names the variant `""` of that package.
