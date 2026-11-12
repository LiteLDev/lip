# lip Protocol

The lip protocol is the manual below, with the schemas it names. It is normative.

## Pages

Commands (section 1):

| Page | Describes |
|---|---|
| [lip(1)](lip.md) | Invoking `lip`: arguments, output, exit status, package arguments, workspace operations. |
| [lip-install(1)](lip-install.md) | `lip install` |
| [lip-uninstall(1)](lip-uninstall.md) | `lip uninstall` |
| [lip-update(1)](lip-update.md) | `lip update` |
| [lip-list(1)](lip-list.md) | `lip list` |
| [lip-run(1)](lip-run.md) | `lip run` |
| [lip-cache(1)](lip-cache.md) | `lip cache clean` |
| [lip-config(1)](lip-config.md) | `lip config get`, `set`, `list` and `delete` |
| [lip-pkg(1)](lip-pkg.md) | `lip pkg init`, `pack`, `view` and `migrate` |
| [lip-tui(1)](lip-tui.md) | The interactive interface that `lip` runs without arguments. |

File formats (section 5):

| Page | Describes | Schema |
|---|---|---|
| [lip.toml(5)](lip.toml.md) | The package file. | [`lip.schema.toml`](lip.schema.toml) |
| [config.toml(5)](config.toml.md) | The configuration file. | [`config.schema.toml`](config.schema.toml) |
| [state.json(5)](state.json.md) | The state file of a workspace. | [`state.schema.json`](state.schema.json) |
| [journal(5)](journal.md) | The journal of a workspace, and recovery from it. | |

Concepts (section 7):

| Page | Describes |
|---|---|
| [lip-package(7)](lip-package.md) | Archives, packages, sources and resolution. |
| [lip-workspace(7)](lip-workspace.md) | Workspaces, operations on them, and the programs run in them. |
| [lip-userspace(7)](lip-userspace.md) | The per-user directories, and the cache. |
| [lip-compatibility(7)](lip-compatibility.md) | Reading and migrating the files of formats 1 to 3, and the packages that `lip` must install. |

## Conventions

### Pages

The pages are laid out as manual pages ([man-pages(7)]): NAME, SYNOPSIS, DESCRIPTION, OPTIONS, EXIT STATUS, FILES and SEE ALSO, in this order, without the sections that would be empty.

`name(N)` is the page `name` of section N. A heading in quotes, such as "Atomicity", is the section of that name: of the page named with it, or of the same page when none is.

### Requirements

Every statement of a page is a requirement, with two exceptions: "should" states a recommendation, and "may" states a permission.

"Not defined here" means not defined by the page that says it.

### Terms

A term is defined where it appears in bold, and has that meaning in every page that does not say otherwise.

### Synopses

A synopsis is written as a utility synopsis ([POSIX Utility Conventions]):

| Notation | Meaning |
|---|---|
| `word` | Written as it is. |
| `<name>` | A placeholder, replaced by a value. |
| `[x]` | `x` is optional. |
| `x \| y` | Either `x` or `y`. |
| `(x \| y)` | Groups alternatives. |
| `x...` | `x`, one or more times. |

The synopsis of a command states which operands and options the command has, and which of them are required.

### Files

A **TOML file** is a [TOML] document in UTF-8, read as the [JSON] value it represents: a table is an object, an array is an array, a string is a string, an integer or a float is a number, and a boolean is a boolean. A TOML file holding a date-time, a date or a time, or the float `inf` or `nan`, is invalid. The TOML schemas (`*.schema.toml`) are JSON Schemas (draft-07) written as TOML files, and constrain that value.

A **JSON file** is a [JSON] text in UTF-8, with no two members of the same name in an object. The JSON schemas (`*.schema.json`) are JSON Schemas (draft-07) written as JSON files, and constrain its value.

A file is **written atomically** by writing its whole content to another file in the same directory, whose name ends in `.tmp`, then renaming that file to its name.

[man-pages(7)]: https://man7.org/linux/man-pages/man7/man-pages.7.html
[POSIX Utility Conventions]: https://pubs.opengroup.org/onlinepubs/9799919799/basedefs/V1_chap12.html
[TOML]: https://toml.io/en/v1.0.0
[JSON]: https://www.rfc-editor.org/info/rfc8259
