# Migrating Packages

lip packages used to be described by `tooth.json` (format versions 1 to 3), and LeviLamina mods by a `manifest.json` without the `format` key. Packages are now described by a `manifest.json` with `"format": "bf45b1"` (see [Package Manifest](../concepts/package_manifest.md)). lip still installs both older kinds of package, but new versions of your package should use the new `manifest.json`. The `lip migrate` command helps you convert it.

## Using `lip migrate`

Run `lip migrate` without arguments in the root directory of your package repository, the one that contains `tooth.json` (format 1, 2 or 3) or the LeviLamina `manifest.json`:

```sh
cd MyTool
lip migrate
git add -A
git commit -m "Migrate to manifest.json"
```

It converts the file as described below and writes `manifest.json`, indented with two spaces. A `tooth.json` is then deleted (if an archive has both, lip reads `tooth.json`, so it has to go), only once `manifest.json` has been written. If the directory has a `tooth.json` and a `manifest.json` at the same time, the command fails and changes nothing: remove the one you do not want to convert first. Without either file, or when `manifest.json` already has the `format` key, the command fails too.

### Questions

When the result needs a value that the old file does not provide in a valid form, lip asks for it on the terminal and asks again until the answer is valid. For example:

-   the `id`, when the name of the tooth or the `name` of the LeviLamina manifest is not a valid id;
-   the `version`, when it is not a SemVer version, e.g. a build placeholder such as `"${modVersion}"` in a LeviLamina mod template;
-   `mod.loader` and `mod.entry` of a LeviLamina manifest, when they are missing;
-   the RIDs a `tooth.json` platform pattern such as `win-*` stands for, as a comma-separated list;
-   the URL prefix the packages will be published under, for a tooth path not on GitHub.

When standard input or standard error is not a terminal (in CI, for example), `lip migrate` fails instead, naming the value it would ask for, and changes nothing. The `Migrate` method of [`lip rpc`](../cli/commands/rpc.md) never asks: it fails in the same way.

### Warnings

Everything that is dropped, or converted with another meaning, is printed as a warning that names the field it comes from, such as `variants[0].preserve_files`. Fields of `tooth.json` format 1 and 2 files are named as in format 3, which lip converts them to first. Warnings do not make the command fail; review them, fix `manifest.json` where needed, and commit.

## From tooth.json

The `id` is the tooth name, the last segment of the `tooth` path (e.g. `github.com/user/MyTool` becomes `MyTool`; a major version suffix such as `/v2` is skipped). `version` is copied, and `info` takes the `name`, `description`, `tags` and, as `icon`, the `avatar_url` of the `tooth.json`. The result is one of two kinds of document.

**A package document**, when the `tooth.json` has a single variant without a label, all its assets are `self`, and its `platform` is empty or not a glob pattern: the content of the variant (see below) is written at the top level, and its `platform` becomes `runtime`.

**A meta document** otherwise, whose `targets` hold the content of the variants:

-   For each label, the variants are merged per RID as lip v0.x merged them; RIDs with the same merged content share one target, whose `runtime` lists them. Variants with an empty `platform` make one more target without `runtime`.
-   A target has `variant` set to the label (omitted for the empty label), the merged content, and for a labeled variant the `id` `<tooth name>-<label>`, which is the identity lip v0.x packages of that variant have.
-   When there is a `client` label (the usual server/client split, which lip resolves by platform), the unlabeled targets get `"platform": "server"`, and the `client` targets are written once more as unlabeled targets with `"platform": "client"`, keeping their `id` (`<tooth name>-client`).
-   Each target's `source` is where the package of that target should be published: for a `github.com/<owner>/<repo>` path, a release asset <span v-pre>`https://github.com/<owner>/<repo>/releases/download/v{{version}}/{{id}}[-{{platform}}][-{{runtime}}].mcmod`</span>, with <span v-pre>`-{{platform}}`</span> and <span v-pre>`-{{runtime}}`</span> present when the target has `platform` and `runtime`; for other paths, the same file name under the URL prefix lip asks you for.

lip does not create those packages: build each one with [`lip pack`](../cli/commands/pack.md) and attach it to the release. See [Creating Packages](creating_packages.md).

The content of a (merged) variant:

-   The dependencies become `dependencies`. The key is the identity the dependency has (its tooth name, followed by `-<label>` for a labeled one), and the value `{ "version": <range>, "source": "<path>[#label]" }`. Ranges are converted to [node-semver](https://github.com/npm/node-semver#ranges) syntax; a range that accepts every version is left out.
-   Assets other than `self` become `archives` named `asset<i>`, `i` being the index of the asset, with its `urls` as `url` and its type as `format` (`"file"` for `uncompressed`).
-   The placements become `files`, taking from the archive of their asset (`from`), or from the package itself for a `self` asset. A `dir` placement becomes a directory (`src` ending with `/`, or `""` for `""` and `"."`), and a `file` placement a single file, or a glob when its `src` is a pattern. For an `uncompressed` asset, `src` is the name of its archive. `\` in `dest` becomes `/`.
-   `preserve_files` and `remove_files` become `preserve` and `remove`.
-   The scripts become `hooks`: `preInstall` (`pre_install`), `postInstall` (`install` first, then `post_install`), `preUninstall` (`pre_uninstall`, then `uninstall`) and `postUninstall` (`post_uninstall`).
-   Empty values are omitted.

## From a LeviLamina Manifest

A `manifest.json` without `format` is rewritten in place as a mod:

| LeviLamina key | Becomes |
| --- | --- |
| `name` | `id` |
| `version` | `version` |
| `platform` | `platform`, when it is `"server"` or `"client"` |
| `description` | `info.description` |
| `author` | `info.authors` |
| `type`, `entry`, `passive`, `extraInfo` | `mod.loader`, `mod.entry`, `mod.passive`, `mod.extra` |
| `dependencies` | `dependencies`: each `name` mapped to its `version`, or `"*"` |
| `conflicts` | `conflicts`: each `name` mapped to its `version`, or `"*"` |
| `optionalDependencies`, `loadAfter` | `mod.loadAfter`: their names, without duplicates |
| `loadBefore` | `mod.loadBefore`: their names, `__before_all_mods__` becoming `"*"` |

Other keys are moved into `mod.extra`. The resulting dependencies have no `source`, so lip does not install them: add a `source` to those lip should install, such as `"LeviLamina": { "version": "...", "source": "github.com/LiteLDev/LeviLamina" }`.

## Limitations

`lip migrate` is a helper, not a guarantee. Always review its output.

-   A `file` placement whose `src` is a glob pattern placed every matching file at `dest/<file name>` in `tooth.json`, but a glob in `files` keeps the path below the part of the pattern without wildcards. When the two differ, e.g. for `**/*.dll`, lip warns; for `self` assets it checks against the files in the repository directory.
-   Version ranges that node-semver cannot express, or whose conversion accepts other versions than the `tooth.json` range, are reported.
-   A `preserve_files` pattern without `/` matched the file name in any directory; it becomes `**/<pattern>`. A `remove_files` pattern also removed matching directories; it is also written with a trailing `/`. Patterns that are not valid globs, such as ones starting with `/`, are dropped.
-   Dependencies of format 1 and 2 whose range cannot be read in format 3 (e.g. `2.16.x`) were already ignored when installing the `tooth.json`; they are reported so that you can add them by hand.
-   Scripts other than the install and uninstall hooks (such as `pre_pack` or custom scripts), and the `prerequisites` of format 2, have no equivalent and are dropped.
-   In a LeviLamina manifest, the versions in `optionalDependencies`, `loadAfter` and `loadBefore` are dropped. In all of `dependencies`, `conflicts`, `optionalDependencies`, `loadAfter` and `loadBefore`, entries whose name is not a valid id are dropped, and a `version` that is not a node-semver range becomes `"*"`.
-   A converted `tooth.json` becomes a file package that places files exactly where the `tooth.json` did. If your package is a mod, consider making it a mod instead (with a `mod` key), which is installed to `mods/<id>/`.
-   Dependencies on other `tooth.json` packages keep working, because lip still installs `tooth.json` packages (it converts them in memory).

Tags are versions only when they are named exactly `v<SemVer>` (e.g. `v1.2.3`). Tags such as `v1.2` are ignored.
