# lip.toml(5)

## NAME

lip.toml - the package file

## SYNOPSIS

`lip.toml`, at the root of an archive ([lip-package(7)]).

## DESCRIPTION

`lip.toml` is a TOML file. The package file is that file read with its templates rendered.

A package file is valid when it is valid against [`lip.schema.toml`](lip.schema.toml), which states the structure of "Keys" for programs, and satisfies everything this page says of it.

### Templates

Each string of the TOML file `lip.toml`, other than a key, is a **template**: text in the Scriban language ([Scriban] 7.5.0). The package file is the value of that TOML file with each template replaced by its rendering, in which each member of the top-level object of the value as written is a variable of the same name, holding that member's value as written; e.g. `{{version}}` renders as the `version` written. A template that cannot be parsed or rendered makes the archive not a package. Everything else that this protocol says of the package file, its schema included, applies to the rendered value.

### Keys

Keys not described here are ignored.

The top-level table:

| Key | Presence | Value |
|---|---|---|
| `format` | required | `4`. |
| `id` | required | A string: the package id, a valid [Go module path]. |
| `version` | required | A string: a version. |
| `info` | optional | A table of information for people; not used by `lip`. |
| `info.name` | optional | A string. |
| `info.description` | optional | A string. |
| `info.tags` | optional | An array of strings. |
| `info.logo` | optional | A string starting with `https://`: a URL whose response, directly or after 3xx redirects, is the logo image. |
| `variants` | required | A table that maps each variant, `""` for the default variant, to its entries: an array of one or more tables. |

An entry:

| Key | Presence | Value |
|---|---|---|
| `side` | required | A string: a side. |
| `platform` | required | A string: a platform. |
| `deps` | optional | A table of strings. Each key is `<package id>` or `<package id>#<variant>`, the variant other than `""`, and each value is a range. |
| `assets` | optional | An array of assets. At most one has the `type` `"mcmod"`. |
| `preserve` | optional | An array of globs. |
| `purge` | optional | An array of globs. |
| `hooks` | optional | A table with the optional keys `pre_install`, `post_install`, `pre_uninstall` and `post_uninstall`, each an array of strings. |

No two entries of the same variant have the same `side` and the same `platform`.

No entry has the key `runtime`, and no entry's `platform` is `"server"` or `"client"`: these are the keys of lip before v0.40.

An asset is a table whose `type` is one of:

| `type` | `urls` | `place` | The asset is |
|---|---|---|---|
| `"self"` | ignored | optional | The archive of the package file itself. |
| `"zip"` | required | optional | A ZIP archive downloaded from `urls`. |
| `"mcmod"` | required | not allowed | A ZIP archive downloaded from `urls`, with a LeviLamina `manifest.json` at its root, written whole into `mods/<the name in that manifest.json>/`. |

- `urls`: An array of one or more strings: HTTPS URLs of the same file, tried in order.
- `place`: An array of tables, each with the required keys `src` and `dest`.

[lip-workspace(7), "Installing and uninstalling"](lip-workspace.md#installing-and-uninstalling) defines what `assets`, `preserve`, `purge` and `hooks` do.

### Places

The `src` and the `dest` of a member of `place` are `/`-separated strings. They must not start with `/` or with an ASCII letter and `:`, or contain `//`, a `..` segment or `\`.

A member of `place` has one of three forms:

| Form | `src` | `dest` |
|---|---|---|
| Glob form | Contains `*`, `?` or `[`; a glob. | `""`, or ends with `/`. |
| Directory form | Otherwise, is `""` or ends with `/`. | `""`, or ends with `/`. |
| File form | Otherwise. | Does not end with `/`, and is not `""`. |

`dest` is relative to the workspace.

### Globs

A **glob** is a POSIX `fnmatch` pattern with `FNM_PATHNAME` and without `FNM_PERIOD`, matched segment by segment (`*`, `?`, bracket expressions; no escape character). A `**` segment matches zero or more segments. A trailing `/` matches directories and everything below them. A glob must not be empty, start with `/`, or contain `//` or `\`.

The globs of `preserve` and `purge` are matched against paths relative to the workspace.

## SEE ALSO

[lip-init(1)](lip-init.md), [lip-view(1)](lip-view.md), [lip-package(7)], [lip-workspace(7)](lip-workspace.md)

[lip-package(7)]: lip-package.md
[Scriban]: https://github.com/scriban/scriban/blob/7.5.0/site/docs/language.md
[Go module path]: https://pkg.go.dev/golang.org/x/mod/module#CheckPath
