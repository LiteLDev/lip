# lip migrate

Migrate the `tooth.json` of lip v0.x in the current directory to `lip.toml`.

## Synopsis

```shell
lip migrate
```

## Description

Run it in the root directory of your package repository. It writes a `lip.toml` (see [lip.toml](../../concepts/lip_toml.md)) converted from the `tooth.json` there, of format version 1, 2 or 3, then deletes `tooth.json`:

- `tooth` becomes `id`, and `version` and `info` are copied; an `info.avatar_url` starting with `https://` becomes `info.logo`, and is dropped otherwise.
- Each label becomes a variant. For each platform (RID) it applies to, its variants are merged as lip v0.37.0 merged them, into an entry for the client when the merged variants depend on `github.com/LiteLDev/LeviLamina#client`, an entry for the server when they depend on another variant of `github.com/LiteLDev/LeviLamina`, and an entry for each side when they do not depend on LeviLamina.
- Each entry is converted exactly as lip reads a `tooth.json` of an [earlier format](../../concepts/lip_toml.md#earlier-formats): `dependencies` become `deps`; assets keep their `type` and `urls`, and their `placements` become `place`; `preserve_files` becomes `preserve` (a pattern without `/` gets a `**/` prefix) and `remove_files` becomes `purge` (a pattern without a trailing `/` is also added with one); the scripts become `hooks`. Placements and patterns that are not valid in `lip.toml` are left out.
- A platform whose merged variants have an asset of type `tar`, `tgz` or `uncompressed` gets no entry, since `lip.toml` has no such asset types.
- [Templates](../../concepts/lip_toml.md#templates) are kept: a value such as `https://example.com/v{{version}}/tool.zip`, or `$(version)` in format 1 and 2, is written as the same template in `lip.toml`, with `{{tooth}}` (`$(path)` in format 1 and 2) renamed to `{{id}}`. A template that would render to another value in `lip.toml`, such as `{{format_version}}`, is written as its rendered value.

`lip.toml` has no RID patterns. When a value the result needs is missing or invalid, such as the platforms (RIDs) a `platform` pattern of `tooth.json` like `win-*` or a `platform` that is not a RID like `windows/amd64` stands for, or the platforms that variants without a `platform` apply to, lip asks for it on the terminal until the answer is valid. When standard input or standard error is not a terminal, it fails instead, naming the value it would ask for, and changes nothing.

If `lip.toml` already exists, or `tooth.json` does not exist or is not a `tooth.json` of format version 1, 2 or 3 that lip can read, `lip migrate` fails and changes nothing.

Everything that is dropped or converted with another meaning is printed as a warning naming the field it comes from (in its format 3 form). Warnings do not make the command fail. The output is a starting point, not a guarantee: review it before publishing, and commit the result. See [Migrating Packages](../../guides/migrating_packages.md) for the details and limitations.
