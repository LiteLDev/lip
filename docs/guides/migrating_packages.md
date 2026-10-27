# Migrating Packages

lip packages used to be described by `tooth.json` (format versions 1 to 3). They are now described by `manifest.json` (format version 4, see [Package Manifest](../concepts/package_manifest.md)). lip still installs `tooth.json` packages, but new versions of your package should use `manifest.json`. The `lip migrate` command helps you convert it.

## Using `lip migrate`

Run `lip migrate` without arguments in the root directory of your package repository, the one that contains `tooth.json` (format 1, 2 or 3):

```sh
cd MyTool
lip migrate
git add -A
git commit -m "Migrate to manifest.json"
```

It converts `tooth.json` to `manifest.json` as described below, writes `manifest.json` and then deletes `tooth.json` (if an archive has both, lip reads `tooth.json`, so it has to go). `tooth.json` is deleted only once `manifest.json` has been written. Without a `tooth.json` in the current directory, the command fails and changes nothing.

Everything that cannot be expressed in `manifest.json`, or needs your decision, is printed as a warning that names the `tooth.json` field it is about, such as `variants[0].preserve_files`. Fields of format 1 and 2 files are named as in format 3, which lip converts them to first. Warnings do not make the command fail; review them, fix `manifest.json` where needed, and commit.

### An existing `manifest.json`

Many repositories already have a `manifest.json`, for example the LeviLamina manifest of a mod. `lip migrate` merges into it instead of replacing it:

-   Keys the file does not have yet are appended at the end of the object, in the converted order and in the file's own indentation and line endings. This includes the format markers `formatVersion` and `formatUuid`.
-   Keys the file already has are never overwritten. When the existing value differs from the converted one, lip prints a warning with both values.
-   The rest of the file, including its key order and formatting, is left as it is.

lip then checks the merged file and warns if it is not a valid `manifest.json` yet. This is the case, for example, for a LeviLamina mod template whose `version` is a build placeholder such as `"${modVersion}"`: a routing document's `version` must be a SemVer version, equal to the tag it is published at. Replace such placeholders with real values, or move the build template elsewhere, before you publish.

## Migration Logic

The `name` is derived from the last segment of the `tooth` path (e.g. `github.com/user/MyTool` becomes `MyTool`), and `version` is copied. The result is one of two kinds of document.

**A file package**, when the `tooth.json` has a single variant without a label and all its assets are `self`:

-   `"type": "files"`, and `rid` from the variant's `platform`.
-   The placements are converted to `placements`.
-   The dependencies become `installDependencies`, with their ranges converted to LeviLamina's syntax.
-   `preserve_files` and `remove_files` become `preserveFiles` and `removeFiles`.
-   The scripts become `preInstall`, `postInstall` (`install` first, then `post_install`), `preUninstall` (`pre_uninstall`, then `uninstall`) and `postUninstall`.

**A routing document** otherwise, with one artifact per variant: `variant` is the label and `rid` the platform. Each artifact points to where the concrete package of that variant should be published:

-   for a `github.com/<owner>/<repo>` path, a release asset <span v-pre>`https://github.com/<owner>/<repo>/releases/download/v{{version}}/{{name}}-<label>[-{{rid}}].mcmod`</span> (`default` for the empty label);
-   otherwise, a directory `./<label>/` of the repository.

When the only labels are the empty one and `client` (the usual server/client split, which lip resolves by platform), the two become artifacts of the default variant with `"platform": "server"` and `"platform": "client"`, and the release asset is <span v-pre>`{{name}}-{{platform}}[-{{rid}}].mcmod`</span>.

lip checks the result with its own parser and warns if it is not a valid `manifest.json` yet, e.g. when two variants with the same label overlap, or when a key kept from an existing `manifest.json` conflicts.

lip does not create those packages: build a concrete `manifest.json` with the files of each variant yourself, as lip prints for every variant. See [Creating Packages](creating_packages.md).

## Limitations

`lip migrate` is a helper, not a guarantee. Always review its output.

-   The `info` section (name, description, tags, avatar) has no equivalent and is dropped.
-   Assets other than `self` (`zip`, `tar`, `tgz`, `uncompressed`) are downloaded from URLs, which `manifest.json` cannot do: such a variant becomes an artifact of a routing document, and its files have to be packed into its concrete package.
-   In a file package, every placement is checked against the files in the repository directory, which lip v3 placed them from. A placement that `manifest.json` cannot write so that it places the same files, such as a `file` placement whose `src` is a glob pattern, is expanded into one file placement per file it placed, with a warning: files added later are not placed until you edit the list.
-   A `platform` glob pattern cannot be expressed as a `rid`; the package is then not restricted to a RID.
-   Version ranges that LeviLamina's syntax cannot express are left out. A converted range that accepts different versions than the `tooth.json` range (lip probes the versions around its bounds, e.g. prereleases) is kept with a warning.
-   A `preserve_files` pattern without `/` matched the file name in any directory; it becomes `**/<pattern>`, with a warning.
-   Glob patterns that use more than `*` and `**` are copied, but may not match the same files.
-   In a file package, a placement that matches no file of the repository (e.g. a `file` placement naming a directory) placed nothing in `tooth.json`, but would make the installation fail; it is left out with a warning. The repository directory may include files that are not committed, which lip cannot tell apart.
-   Dependencies of format 1 and 2 whose range cannot be read in format 3 (e.g. `2.16.x`) were already ignored when installing the `tooth.json`; they are reported so that you can add them by hand.
-   Scripts other than the install and uninstall hooks (such as `pre_pack` or custom scripts), and the `prerequisites` of format 2, have no equivalent and are dropped.
-   A converted file package places files exactly where the `tooth.json` did. If your package is a mod, consider making it a mod instead (any `type` other than `"files"`), which is installed to `mods/<name>/`.
-   Dependencies on other `tooth.json` packages keep working, because lip still installs `tooth.json` packages (it converts them to format 4 in memory).

Tags are versions only when they are named exactly `v<SemVer>` (e.g. `v1.2.3`). Tags such as `v1.2` are ignored.
