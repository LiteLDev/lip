# lip-pkg(1)

## NAME

lip-pkg - develop packages

## SYNOPSIS

```
lip pkg init [-h | --help]
lip pkg pack [<DIR>] (-o | --output) <FILE> [-h | --help]
lip pkg view <PACKAGE> [-h | --help]
lip pkg migrate [-h | --help]
```

## DESCRIPTION

The commands of the command group `pkg` create, pack, show and migrate packages ([lip-package(7)]). None of them is a workspace operation.

### `lip pkg init`

`lip pkg init` writes `lip.toml` in the current working directory: a package file ([lip.toml(5)]) with exactly the members of `{ "format": 4, "id": "<id>", "version": "0.1.0", "variants": { "": [ { "side": "server", "platform": "win-x64" } ] } }`. `lip` asks the user on the terminal for `<id>` until the answer is a [Go module path]. When standard input or standard error is not a terminal, the command fails instead.

When `lip.toml` exists, the command fails and writes nothing.

### `lip pkg pack`

`lip pkg pack` writes to `<FILE>`, replacing it, a ZIP archive holding every regular file below `<DIR>` at its path relative to `<DIR>`, except `<FILE>` itself and, at the root of `<DIR>`, everything below `.git/` and `.lip/`. Such an archive can be an asset of type `"mcmod"`.

`<DIR>` is the directory to pack; without it, the current working directory. When it is not an existing directory, the command fails and writes nothing.

### `lip pkg view`

`<PACKAGE>` is a package argument ([lip(1), "Package arguments"](lip.md#package-arguments)).

The result is written as JSON followed by a line break, as a JSON object with exactly these members:

```json
{ "manifest": { ... }, "versions": ["<version>", ...] }
```

- `manifest`: The package file of the first archive that the source of `<PACKAGE>` names ([lip-package(7), "Resolution"](lip-package.md#resolution), step 1), as it is read for the side `"server"` and the platform that `.lip/state.json` records, or `"win-x64"` when that file does not exist; a `tooth_lock.json` is not read. When that archive cannot be obtained or is not a package, the command fails.
- `versions`: When the source is a remote repository, the versions of the version tags of that repository, whatever the variant and the ref of the source, in ascending [SemVer] precedence, each once. Otherwise, an empty array.

`lip pkg view` has no `--json` option, and changes no file or directory other than entries of the cache ([lip-userspace(7)](lip-userspace.md)).

### `lip pkg migrate`

`lip pkg migrate` migrates the current working directory as [lip-compatibility(7), "lip pkg migrate"](lip-compatibility.md#lip-pkg-migrate) defines.

## OPTIONS

- `-o <FILE>`, `--output <FILE>`: Of `lip pkg pack`: the path of the ZIP archive to write.

## SEE ALSO

[lip(1)](lip.md), [lip.toml(5)], [lip-package(7)], [lip-compatibility(7)](lip-compatibility.md)

[lip.toml(5)]: lip.toml.md
[lip-package(7)]: lip-package.md
[Go module path]: https://pkg.go.dev/golang.org/x/mod/module#CheckPath
[SemVer]: https://semver.org/spec/v2.0.0.html
