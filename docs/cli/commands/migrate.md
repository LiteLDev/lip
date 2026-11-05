# lip migrate

Migrate the `tooth.json` in the current directory from format version 1, 2 or 3 to format version 4.

## Synopsis

```shell
lip migrate
```

## Description

Run it in the root directory of your package repository. It rewrites the `tooth.json` there, of format version 1, 2 or 3, in place as a `tooth.json` of format version 4 (see [tooth.json](../../concepts/tooth_json.md)), indented with two spaces:

- `tooth`, `version` and `info` are copied; an `info.avatar_url` starting with `https://` becomes `info.logo`, and is dropped otherwise.
- Each label becomes a variant. For each runtime it applies to, its variants are merged as lip v0.37.0 merged them, into an entry for the server and an entry for the client. With a `client` label, the variant `""` gets server entries only, and the `client` entries are also used as the client entries of `""`.
- Each entry is converted exactly as lip reads a `tooth.json` of an [earlier format](../../concepts/tooth_json.md#earlier-formats): assets keep their `type` and `urls`, and their `placements` become `place`; `preserve_files` becomes `preserve` (a pattern without `/` gets a `**/` prefix) and `remove_files` becomes `purge` (a pattern without a trailing `/` is also added with one); the scripts become `hooks`. Placements and patterns that are not valid in format 4 are left out.
- A runtime whose merged variants have an asset of type `tar`, `tgz` or `uncompressed` gets no entry, since format 4 has no such asset types.

Format 4 has no RID patterns. When a value the result needs is missing or invalid, such as the RIDs a platform pattern like `win-*` stands for, or the RIDs that variants without a platform apply to, lip asks for it on the terminal until the answer is valid. When standard input or standard error is not a terminal, it fails instead, naming the value it would ask for, and changes nothing.

If `tooth.json` does not exist, is already of format version 4, or cannot be read, `lip migrate` fails and changes nothing.

Everything that is dropped or converted with another meaning is printed as a warning naming the field it comes from (in its format 3 form). Warnings do not make the command fail. The output is a starting point, not a guarantee: review it before publishing, and commit the result. See [Migrating Packages](../../guides/migrating_packages.md) for the details and limitations.
