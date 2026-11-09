# Migrating Packages

Packages for lip v0.37.0 and earlier have a `tooth.json` of format version 1, 2 or 3. Packages now have a `lip.toml` instead (see [lip.toml](../concepts/lip_toml.md)). lip still installs packages with such a `tooth.json`, but new versions of your package should use `lip.toml`. The `lip migrate` command helps you convert it.

## Using `lip migrate`

Run `lip migrate` without arguments in the root directory of your package repository, the one that contains `tooth.json`:

```sh
cd MyTool
lip migrate
git add lip.toml tooth.json
git commit -m "Migrate tooth.json to lip.toml"
```

It writes `lip.toml` converted from `tooth.json`, then deletes `tooth.json`. When `lip.toml` already exists, or `tooth.json` is missing or is not a `tooth.json` of format 1, 2 or 3 that lip can read, the command fails and changes nothing.

### Questions

`lip.toml` has no RID patterns: every entry names one platform, a RID. When the result needs a value that the old file does not provide, lip asks for it on the terminal and asks again until the answer is valid:

-   the platforms (RIDs) a `platform` pattern of `tooth.json` such as `win-*` stands for, as a comma-separated list (each must match the pattern);
-   the platforms (RIDs) a `platform` of `tooth.json` that is not a RID, such as `windows/amd64`, stands for, as a comma-separated list;
-   the platforms (RIDs) that variants without a `platform` apply to, when there are such variants.

When standard input or standard error is not a terminal (in CI, for example), `lip migrate` fails instead, naming the value it would ask for, and changes nothing.

### Warnings

Everything that is dropped, or converted with another meaning, is printed as a warning that names the field it comes from, such as `variants (label '', win-x64).assets[1]`. Fields of `tooth.json` format 1 and 2 files are named as in format 3, which lip converts them to first. Warnings do not make the command fail; review them, fix `lip.toml` where needed, and commit.

## What Changes

`format` is `4`, `tooth` becomes `id`, and `version` and `info` are copied, except that `info.avatar_url` becomes `info.logo` when it starts with `https://` and is dropped otherwise, with a warning. The variants are rewritten as follows:

-   Each label becomes a variant of the same name; the empty label becomes the variant `""`.
-   For each platform a label applies to (the `platform` of its variants, with patterns and values that are not RIDs expanded to the RIDs you gave), the variants of the label that apply to that platform are merged as lip v0.37.0 merged them, and the result becomes one entry with that `platform`. Its `side` is `client` when the merged variants depend on `github.com/LiteLDev/LeviLamina#client`, and `server` when they depend on another variant of `github.com/LiteLDev/LeviLamina`; without a dependency on LeviLamina, the result becomes two entries, one for each side.
-   `dependencies` become `deps`, with their ranges. A range that is not a valid range now is asked for on the terminal until the answer is one; without a terminal the command fails and changes nothing.
-   Each asset keeps its `type` and `urls`, and its `placements` become `place`, converted exactly as lip reads a `tooth.json` of an [earlier format](../concepts/lip_toml.md#earlier-formats): after `\` is replaced by `/` and empty and `.` segments are removed, a `dir` placement takes the directory form (`src` and `dest` ending with `/`, or `""`), a `file` placement whose `src` contains `*`, `?` or `[` the glob form (`dest` ending with `/`, or `""`), and any other `file` placement the file form. A placement that is not valid in `lip.toml` is left out, with a warning.
-   `lip.toml` has no `tar`, `tgz` and `uncompressed` asset types. A platform whose merged variants have such an asset gets no entry, with a warning, just as lip installs no entry of it for that platform.
-   `preserve_files` becomes `preserve` and `remove_files` becomes `purge`, with `\` replaced by `/`; a `preserve_files` pattern without `/` gets a `**/` prefix, and a `remove_files` pattern without a trailing `/` is also written with one (see [Limitations](#limitations)).
-   The scripts become `hooks`: `pre_install`, `post_install` (`install` first, then `post_install`), `pre_uninstall` (`pre_uninstall`, then `uninstall`) and `post_uninstall`.
-   Other empty values, outside `info`, are omitted.

## Limitations

`lip migrate` is a helper, not a guarantee. Always review its output.

-   `lip.toml` must have the tag version as its `version` at a tag, and the path of the repository as its `id`. lip v0.37.0 installed a `tooth.json` that broke these rules; check yours before you publish.
-   Placements are converted by the rules above, without looking at the files they place, and are then carried out as for `lip.toml`: a `file` placement whose `src` is a pattern keeps the subdirectories of the files it matches, where `tooth.json` placed them by file name only, and a placement of a missing file fails the installation, where `tooth.json` skipped it. Check such placements.
-   A `preserve_files` pattern without `/` matched the file name in any directory; it becomes `**/<pattern>`. A `remove_files` pattern also removed matching directories; it is also written with a trailing `/`. Patterns that are not valid globs, such as ones starting with `/`, are dropped.
-   Dependencies of format 1 and 2 whose range cannot be read in format 3 (e.g. `2.16.x`) were already ignored when installing the `tooth.json`; they are reported so that you can add them by hand.
-   Scripts other than the install and uninstall hooks (such as `pre_pack` or custom scripts), and the `prerequisites` of format 2, have no equivalent and are dropped.
-   A migrated package is a file package that places files where the old `tooth.json` did. If your package is a LeviLamina mod, consider publishing it as an `mcmod` asset instead (see [A Mod](creating_packages.md#a-mod)), which is installed to `mods/<name>/`.
-   Dependencies on other packages that still have a `tooth.json` keep working, because lip still installs them.

Tags are versions only when they are named exactly `v<SemVer>` (e.g. `v1.2.3`). Tags such as `v1.2` are not versions.
