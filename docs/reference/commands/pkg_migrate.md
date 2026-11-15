# lip pkg migrate

Migrate the `tooth.json` of lip v0.x in the current directory to `lip.toml`.

## Synopsis

```
lip pkg migrate [-h | --help]
```

## Description

`lip pkg migrate` writes, as `lip.toml` in the current working directory, a [package file](../../concepts/lip_toml.md) converted from the `tooth.json` there, then deletes `tooth.json`. Run it in the root directory of your package repository.

It fails and changes nothing when:

- `lip.toml` already exists;
- `tooth.json` is missing; or
- `tooth.json` is not one of format version 1, 2 or 3 that lip can read.

What is converted:

- `tooth` becomes `id`. `version` and `info` are copied; an `info.avatar_url` that starts with `https://` becomes `info.logo`, and is dropped otherwise.
- Each label becomes a variant. For each platform (RID) it applies to, its variants are merged as lip v0.37.0 merged them, into an entry for the client when the merged variants depend on `github.com/LiteLDev/LeviLamina#client`, an entry for the server when they depend on another variant of `github.com/LiteLDev/LeviLamina`, and an entry for each side when they do not depend on LeviLamina.
- Each entry is converted as lip reads a `tooth.json` of an [earlier format](../../concepts/lip_toml.md#earlier-formats): `dependencies` become `deps`; assets keep their `type` and `urls`, and their `placements` become `place`; `preserve_files` becomes `preserve`; `remove_files` becomes `purge`; the scripts become `hooks`. Placements and patterns that are not valid in `lip.toml` are left out.
- A platform whose merged variants have an asset of type `tar`, `tgz` or `uncompressed` gets no entry: `lip.toml` has no such asset types.
- [Templates](../../concepts/lip_toml.md#templates) are kept. A value such as `https://example.com/v{{version}}/tool.zip`, or `$(version)` in formats 1 and 2, is written as the same template, with `{{tooth}}` (`$(path)` in formats 1 and 2) renamed to `{{id}}`. A template that would render to another value in `lip.toml` is written as its rendered value.

### Questions

`lip.toml` has no platform patterns: every entry names one platform. When a value that the result needs is missing or invalid, lip asks for it on the terminal until the answer is valid. It asks for:

- the platforms (RIDs) that a pattern such as `win-*` stands for;
- the platforms that a `platform` which is not a RID, such as `windows/amd64`, stands for;
- the platforms that variants without a `platform` apply to; and
- a dependency range that is not a valid range.

When standard input or standard error is not a terminal, the command fails instead, names the value it would ask for, and changes nothing.

### Warnings

Everything that is dropped, or converted with another meaning, is written as a warning that names the field it comes from, in its format 3 form. Warnings do not make the command fail.

The result is a starting point. Review it before you publish; [Migrating packages](../../authors/migrating_packages.md) lists what to check.

`lip pkg migrate` is not a workspace operation.

## Options

| Option | Description |
| --- | --- |
| `-h`, `--help` | Write the help and exit. |

## Output

`lip pkg migrate` has no result: it writes nothing to standard output. The questions, the warnings and the final line are messages on standard error.

## Exit status

| Code | Meaning |
| --- | --- |
| `0` | `lip.toml` was written and `tooth.json` deleted, with or without warnings. |
| not `0` | A usage error; `lip.toml` exists; `tooth.json` is missing or unreadable; or lip cannot ask for a value it needs. Nothing changed. |

## Examples

```shell
cd MyTool
lip pkg migrate
```

## See also

- [Migrating packages](../../authors/migrating_packages.md)
- [Tools](../../interface/tools.md): **Migrate tooth.json** in the interface.
- [lip.toml](../../concepts/lip_toml.md#earlier-formats)
