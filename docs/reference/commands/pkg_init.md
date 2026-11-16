# lip pkg init

Create a `lip.toml` for a new package in the current directory.

## Synopsis

```
lip pkg init [-h | --help]
```

## Description

`lip pkg init` asks for the package id, then writes `lip.toml` in the current working directory.

1. When `lip.toml` already exists there, the command fails and writes nothing.
2. lip asks on the terminal for the package id, a [Go module path](https://pkg.go.dev/golang.org/x/mod/module#CheckPath) such as `github.com/user/repo`, and asks again until the answer is valid. For a package in a git repository, the id is the path of the repository.
3. It writes a [package file](../../concepts/lip_toml.md) with exactly these values, `<id>` being your answer:

   ```toml
   format = 4
   id = "<id>"
   version = "0.1.0"

   [[variants.""]]
   side = "server"
   platform = "win-x64"
   ```

When standard input or standard error is not a terminal, lip cannot ask, and the command fails instead. There is no option that gives the id; in a script, write `lip.toml` yourself.

The package installs nothing yet. See [Publish your mod with lip](https://levimc.org/tutorials/develop/publish-a-package/) for what to add.

`lip pkg init` is not a workspace operation: it needs no server, takes no lock, and touches nothing but `lip.toml`.

## Options

| Option | Description |
| --- | --- |
| `-h`, `--help` | Write the help and exit. |

## Output

`lip pkg init` has no result: it writes nothing to standard output. The question and the final line are messages on standard error:

```
Enter the package id of the package (a Go module path, e.g. github.com/owner/repo):
```

## Exit status

| Code | Meaning |
| --- | --- |
| `0` | `lip.toml` was written. |
| not `0` | A usage error; `lip.toml` exists; lip cannot ask; or the file cannot be written. |

## Examples

```shell
cd MyMod
lip pkg init
```

## See also

- [Publish your mod with lip](https://levimc.org/tutorials/develop/publish-a-package/)
- [lip.toml](../../concepts/lip_toml.md)
- [Tools](../../interface/tools.md): **Create lip.toml** in the interface.
