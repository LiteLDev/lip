# state.json(5)

## NAME

state.json - the state file of a workspace

## SYNOPSIS

`.lip/state.json`, in a workspace ([lip-workspace(7)]).

## DESCRIPTION

The **state file** is the JSON file `.lip/state.json`, valid against [`state.schema.json`](state.schema.json). It records the environment of the workspace and its installed packages. Packages and paths are those of [lip-workspace(7)]: a path is relative to the workspace and `/`-separated. A workspace without a state file has no installed packages and no recorded environment, unless [lip-compatibility(7), "tooth_lock.json"](lip-compatibility.md#tooth_lockjson) reads one for it.

The state file is only written atomically, through `.lip/state.json.tmp` ([lip-workspace(7), "Carrying out an operation"](lip-workspace.md#carrying-out-an-operation)), so a process reading it without the workspace lock reads either the file before an operation or the file after it.

### Members

The top-level object:

| Member | Presence | Value |
|---|---|---|
| `format` | required | `4`. |
| `side` | required | The side of the environment. |
| `platform` | required | A non-empty string: the platform of the environment. |
| `packages` | required | An array with one object for each installed package, in installation order. |

A member of `packages`:

| Member | Presence | Value |
|---|---|---|
| `manifest` | required | The package file of the package, as [lip-package(7), "Resolution"](lip-package.md#resolution) read it. |
| `variant` | required | A string: the variant of the source of the package. |
| `explicit` | required | A boolean: whether a request named the package. Such a package is **explicitly installed**; any other is a **dependency package**. |
| `ref` | required | A string: the source of the package, a local path recorded as the [`file:` URI] of its absolute path. |
| `commit` | for a remote repository | A string of 40 lowercase hexadecimal digits: the commit the archive of the package was at. |
| `files` | required | An array of strings: the paths of the files the package owns. |

A state file whose top-level object has the key `runtime`, or whose `platform` is `"server"` or `"client"`, is invalid: these are the keys of lip before v0.40.

## SEE ALSO

[lip-list(1)](lip-list.md), [journal(5)](journal.md), [lip-workspace(7)]

[lip-workspace(7)]: lip-workspace.md
[`file:` URI]: https://www.rfc-editor.org/info/rfc8089
