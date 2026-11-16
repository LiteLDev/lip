# lip pkg view

Show the package file and the versions of a package, as JSON.

## Synopsis

```
lip pkg view <PACKAGE> [-h | --help]
```

## Description

`<PACKAGE>` is a [package argument](../cli.md#package-arguments), in the forms that [`lip install`](install.md) takes: a repository, with or without `#<variant>` and `@<ref>`, an `https://` URL, or a local ZIP file or directory.

`lip pkg view` reads the package that the argument names first and writes its package file and the versions of its repository:

| Argument | Package shown |
| --- | --- |
| `<repo>` | The repository at its highest version tag. |
| `<repo>@<version>` | The repository at the tag `v<version>`. |
| `<repo>@branch:<name>`, `@tag:<name>`, `@rev:<commit>` | The repository at that commit. |
| A URL or a local path | That archive, as it is. |

When that archive cannot be obtained or is not a package, the command fails.

A `tooth.json` of lip v0.x is shown as the package file that lip reads from it for the side `server` and the platform `win-x64`. See [Earlier formats](../../concepts/lip_toml.md#earlier-formats).

`lip pkg view` changes nothing but the cache, where it keeps what it downloads. It is not a workspace operation and needs no server.

## Options

| Option | Description |
| --- | --- |
| `-h`, `--help` | Write the help and exit. |

There is no `--json` option: the result is always JSON.

## Output

The result goes to standard output: a JSON object with exactly these members, followed by a line break.

```json
{ "manifest": { ... }, "versions": ["<version>", ...] }
```

| Member | Value |
| --- | --- |
| `manifest` | The package file, `lip.toml`, of the package, as a JSON object, with its [templates](../../concepts/lip_toml.md#templates) rendered. |
| `versions` | For a repository: the versions of its version tags, the tags named `v` followed by a SemVer version, without the `v`, lowest first, each once. The variant and the ref of the argument do not change the list. For a URL or a local path: an empty array. |

For example, `lip pkg view ./MyMod` for the directory of a mod:

```json
{
  "manifest": {
    "format": 4,
    "id": "github.com/user/MyMod",
    "version": "1.0.0",
    "variants": {
      "": [
        {
          "side": "server",
          "platform": "win-x64",
          "deps": {
            "github.com/LiteLDev/LeviLamina": ">=1.0.0 <2.0.0"
          },
          "assets": [
            {
              "type": "mcmod",
              "urls": [
                "https://github.com/user/MyMod/releases/download/v1.0.0/MyMod-server-win-x64.zip"
              ]
            }
          ]
        }
      ]
    }
  },
  "versions": []
}
```

## Exit status

| Code | Meaning |
| --- | --- |
| `0` | The result was written. |
| not `0` | A usage error; an invalid argument; or the archive cannot be obtained or is not a package. |

## Examples

View the newest version of a package, and list its versions:

```shell
lip pkg view github.com/LiteLDev/LeviLamina
```

List only the versions, with [jq](https://jqlang.org/):

```shell
lip pkg view github.com/LiteLDev/LeviLamina | jq -r '.versions[]'
```

View a given version, and the head of a branch:

```shell
lip pkg view github.com/user/repo@1.0.0
lip pkg view github.com/user/repo@branch:main
```

Check that lip reads the `lip.toml` you are writing:

```shell
lip pkg view .
```

## See also

- [Publish your mod with lip](https://levimc.org/tutorials/develop/publish-a-package/)
- [Tools](../../interface/tools.md): **View package…** in the interface.
- [lip.toml](../../concepts/lip_toml.md)
