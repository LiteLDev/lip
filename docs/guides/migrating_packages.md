# Migrating Packages

Packages for lip v0.37.0 and earlier have a `tooth.json` of format version 1, 2 or 3. The current format is format version 4 (see [tooth.json](../concepts/tooth_json.md)). lip still installs packages of the earlier formats, but new versions of your package should use format 4. The `lip migrate` command helps you convert it.

## Using `lip migrate`

Run `lip migrate` without arguments in the root directory of your package repository, the one that contains `tooth.json`:

```sh
cd MyTool
lip migrate
git add tooth.json
git commit -m "Migrate tooth.json to format 4"
```

It rewrites `tooth.json` in place as format 4, indented with two spaces. Without `tooth.json`, when it is already of format 4, or when it cannot be read, the command fails and changes nothing.

### Questions

Format 4 has no RID patterns: every entry names one runtime. When the result needs a value that the old file does not provide, lip asks for it on the terminal and asks again until the answer is valid:

-   the RIDs a platform pattern such as `win-*` stands for, as a comma-separated list (each must match the pattern);
-   the RIDs that variants without a `platform` apply to, when there are such variants.

When standard input or standard error is not a terminal (in CI, for example), `lip migrate` fails instead, naming the value it would ask for, and changes nothing. The `Migrate` method of [`lip rpc`](../cli/commands/rpc.md) never asks: it fails in the same way.

### Warnings

Everything that is dropped, or converted with another meaning, is printed as a warning that names the field it comes from, such as `variants (label '', win-x64).assets[1]`. Fields of `tooth.json` format 1 and 2 files are named as in format 3, which lip converts them to first. Warnings do not make the command fail; review them, fix `tooth.json` where needed, and commit.

## What Changes

`format_version` and `format_uuid` are set to those of format 4, and `tooth`, `version` and `info` are copied, except that `info.avatar_url` becomes `info.logo` when it starts with `https://` and is dropped otherwise, with a warning. The variants are rewritten as follows:

-   Each label becomes a variant of the same name; the empty label becomes the variant `""`.
-   For each runtime a label applies to (its platforms, with patterns expanded to the RIDs you gave), the variants of the label that apply to that runtime are merged as lip v0.37.0 merged them, and the result becomes an entry for the server and an entry for the client, with that runtime.
-   When there is a `client` label (the usual server/client split), the variant `""` gets server entries only, and the entries of the `client` variant are also written as the client entries of the variant `""`.
-   `dependencies` are copied with their ranges. A range that cannot be written in the format 4 syntax with the same meaning is left out, with a warning.
-   `self` and `zip` assets are kept, and their `placements` become `place` of format 4: a `dir` placement takes the directory form (`src` and `dest` ending with `/`, or `""`), and a `file` placement the file form, or the glob form when its `src` is a pattern. `\` in `dest` becomes `/`. The `tar`, `tgz` and `uncompressed` asset types have no equivalent and are dropped.
-   `preserve_files` becomes `preserve` and `remove_files` becomes `purge`, with `\` replaced by `/` (see [Limitations](#limitations) for the patterns that change).
-   The scripts become `hooks`: `pre_install`, `post_install` (`install` first, then `post_install`), `pre_uninstall` (`pre_uninstall`, then `uninstall`) and `post_uninstall`.
-   Empty values are omitted.

## Limitations

`lip migrate` is a helper, not a guarantee. Always review its output.

-   Format 4 requires the `version` at a tag to equal the tag version, and the `tooth` of a repository to equal its path. lip v0.37.0 installed a `tooth.json` that broke these rules; check yours before you publish.
-   A placement of a `self` asset is checked against the files of the repository directory. A placement that placed nothing is left out, and one whose format 4 form would place other files (for example a `file` pattern that also matches files in subdirectories, which `tooth.json` placed by file name only) is expanded into one placement per file, so files added later are not placed. Both are reported. For `zip` assets, the files are not known and such patterns are only reported.
-   A `preserve_files` pattern without `/` matched the file name in any directory; it becomes `**/<pattern>`. A `remove_files` pattern also removed matching directories; it is also written with a trailing `/`. Patterns that are not valid globs, such as ones starting with `/`, are dropped.
-   Dependencies of format 1 and 2 whose range cannot be read in format 3 (e.g. `2.16.x`) were already ignored when installing the `tooth.json`; they are reported so that you can add them by hand.
-   A dependency on the `client` variant of another package keeps its `#client`. Remove it once that package has moved to format 4, whose variant `""` covers the client.
-   Scripts other than the install and uninstall hooks (such as `pre_pack` or custom scripts), and the `prerequisites` of format 2, have no equivalent and are dropped.
-   A migrated `tooth.json` is a file package that places files where the old `tooth.json` did. If your package is a LeviLamina mod, consider publishing it as an `mcmod` asset instead (see [A Mod](creating_packages.md#a-mod)), which is installed to `mods/<name>/`.
-   Dependencies on other packages of earlier formats keep working, because lip still installs them.

Tags are versions only when they are named exactly `v<SemVer>` (e.g. `v1.2.3`). Tags such as `v1.2` are not versions.
