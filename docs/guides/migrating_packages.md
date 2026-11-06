# Migrating Packages

Packages for lip v0.37.0 and earlier have a `tooth.json` of format version 1, 2 or 3. Packages now have a `lip.yaml` instead (see [lip.yaml](../concepts/lip_yaml.md)). lip still installs packages with such a `tooth.json`, but new versions of your package should use `lip.yaml`. The `lip migrate` command helps you convert it.

## Using `lip migrate`

Run `lip migrate` without arguments in the root directory of your package repository, the one that contains `tooth.json`:

```sh
cd MyTool
lip migrate
git add lip.yaml tooth.json
git commit -m "Migrate tooth.json to lip.yaml"
```

It writes `lip.yaml` converted from `tooth.json`, then deletes `tooth.json`. When `lip.yaml` already exists, or `tooth.json` is missing or is not a `tooth.json` of format 1, 2 or 3 that lip can read, the command fails and changes nothing.

### Questions

`lip.yaml` has no RID patterns: every entry names one runtime. When the result needs a value that the old file does not provide, lip asks for it on the terminal and asks again until the answer is valid:

-   the RIDs a platform pattern such as `win-*` stands for, as a comma-separated list (each must match the pattern);
-   the RIDs that variants without a `platform` apply to, when there are such variants.

When standard input or standard error is not a terminal (in CI, for example), `lip migrate` fails instead, naming the value it would ask for, and changes nothing.

### Warnings

Everything that is dropped, or converted with another meaning, is printed as a warning that names the field it comes from, such as `variants (label '', win-x64).assets[1]`. Fields of `tooth.json` format 1 and 2 files are named as in format 3, which lip converts them to first. Warnings do not make the command fail; review them, fix `lip.yaml` where needed, and commit.

## What Changes

`format` is `4`, `tooth` becomes `id`, and `version` and `info` are copied, except that `info.avatar_url` becomes `info.logo` when it starts with `https://` and is dropped otherwise, with a warning. The variants are rewritten as follows:

-   Each label becomes a variant of the same name; the empty label becomes the variant `""`.
-   For each runtime a label applies to (its platforms, with patterns expanded to the RIDs you gave), the variants of the label that apply to that runtime are merged as lip v0.37.0 merged them, and the result becomes an entry for the server and an entry for the client, with that runtime.
-   When there is a `client` label (the usual server/client split), the variant `""` gets server entries only, and the entries of the `client` variant are also written as the client entries of the variant `""`.
-   `dependencies` become `deps`, with their ranges. A range that is not a valid range now is asked for on the terminal until the answer is one; without a terminal the command fails and changes nothing.
-   Each asset keeps its `type` and `urls`, and its `placements` become `place`, converted exactly as lip reads a `tooth.json` of an [earlier format](../concepts/lip_yaml.md#earlier-formats): after `\` is replaced by `/` and empty and `.` segments are removed, a `dir` placement takes the directory form (`src` and `dest` ending with `/`, or `""`), a `file` placement whose `src` contains `*`, `?` or `[` the glob form (`dest` ending with `/`, or `""`), and any other `file` placement the file form. A placement that is not valid in `lip.yaml` is left out, with a warning.
-   `lip.yaml` has no `tar`, `tgz` and `uncompressed` asset types. A runtime whose merged variants have such an asset gets no entry, with a warning, just as lip installs no entry of it for that runtime.
-   `preserve_files` becomes `preserve` and `remove_files` becomes `purge`, with `\` replaced by `/`; a `preserve_files` pattern without `/` gets a `**/` prefix, and a `remove_files` pattern without a trailing `/` is also written with one (see [Limitations](#limitations)).
-   The scripts become `hooks`: `pre_install`, `post_install` (`install` first, then `post_install`), `pre_uninstall` (`pre_uninstall`, then `uninstall`) and `post_uninstall`.
-   Other empty values, outside `info`, are omitted.

## Limitations

`lip migrate` is a helper, not a guarantee. Always review its output.

-   `lip.yaml` must have the tag version as its `version` at a tag, and the path of the repository as its `id`. lip v0.37.0 installed a `tooth.json` that broke these rules; check yours before you publish.
-   Placements are converted by the rules above, without looking at the files they place, and are then carried out as for `lip.yaml`: a `file` placement whose `src` is a pattern keeps the subdirectories of the files it matches, where `tooth.json` placed them by file name only, and a placement of a missing file fails the installation, where `tooth.json` skipped it. Check such placements.
-   A `preserve_files` pattern without `/` matched the file name in any directory; it becomes `**/<pattern>`. A `remove_files` pattern also removed matching directories; it is also written with a trailing `/`. Patterns that are not valid globs, such as ones starting with `/`, are dropped.
-   Dependencies of format 1 and 2 whose range cannot be read in format 3 (e.g. `2.16.x`) were already ignored when installing the `tooth.json`; they are reported so that you can add them by hand.
-   A dependency on the `client` variant of another package keeps its `#client`. Remove it once that package has moved to `lip.yaml`, whose variant `""` covers the client.
-   Scripts other than the install and uninstall hooks (such as `pre_pack` or custom scripts), and the `prerequisites` of format 2, have no equivalent and are dropped.
-   A migrated package is a file package that places files where the old `tooth.json` did. If your package is a LeviLamina mod, consider publishing it as an `mcmod` asset instead (see [A Mod](creating_packages.md#a-mod)), which is installed to `mods/<name>/`.
-   Dependencies on other packages that still have a `tooth.json` keep working, because lip still installs them.

Tags are versions only when they are named exactly `v<SemVer>` (e.g. `v1.2.3`). Tags such as `v1.2` are not versions.
