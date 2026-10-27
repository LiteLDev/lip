# Migrating Packages

lip packages used to be described by `tooth.json` (format versions 1 to 3). They are now described by `manifest.json` (format version 4, see [Package Manifest](../concepts/package_manifest.md)). lip still installs `tooth.json` packages, but new versions of your package should use `manifest.json`. The `lip migrate` command helps you convert it.

## Using `lip migrate`

```sh
lip migrate <FILE> <OUTPUT>
```

-   `<FILE>`: The path to your existing `tooth.json` (format 1, 2 or 3).
-   `<OUTPUT>`: The path where the `manifest.json` is written.

### Example

```sh
lip migrate tooth.json manifest.json
```

Everything that needs your decision is printed as a warning. Remove `tooth.json` from the repository once `manifest.json` is ready: if an archive has both, lip reads `tooth.json`.

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

lip checks the result with its own parser and warns if it is not a valid `manifest.json` yet, e.g. when two variants with the same label overlap.

lip does not create those packages: build a concrete `manifest.json` with the files of each variant yourself, as lip prints for every variant. See [Creating Packages](creating_packages.md).

## Limitations

`lip migrate` is a helper, not a guarantee. Always review its output.

-   The `info` section (name, description, tags, avatar) has no equivalent and is dropped.
-   File placements whose `src` is a glob pattern cannot be converted; list the files instead.
-   A `platform` glob pattern cannot be expressed as a `rid`; the package is then not restricted to a RID.
-   Version ranges that LeviLamina's syntax cannot express are left out.
-   Glob patterns that use more than `*` and `**` are copied, but may not match the same files.
-   A converted file package places files exactly where the `tooth.json` did. If your package is a mod, consider making it a mod instead (any `type` other than `"files"`), which is installed to `mods/<name>/`.
-   Dependencies on other `tooth.json` packages keep working, because lip still reads `tooth.json`.

Tags are versions only when they are named exactly `v<SemVer>` (e.g. `v1.2.3`). Tags such as `v1.2` are ignored.
