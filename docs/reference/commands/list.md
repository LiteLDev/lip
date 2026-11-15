# lip list

List the installed packages.

## Synopsis

```
lip list [--json] [-h | --help]
```

## Description

`lip list` reads the [state file](../../concepts/state_file.md) of the workspace and writes the installed packages, each as `<identity>@<version>`, in two groups:

1. the **explicitly installed** packages: those you asked for, the game and the loader among them;
2. the **dependency packages**: those installed because another package needs them.

The explicitly installed packages come first. Within each group the packages are in installation order: a package comes after the packages it depends on.

The **identity** of a package is its package id, followed by `#<variant>` when the variant is not the default one, such as `github.com/LiteLDev/LegacyScriptEngine#lua`. It is what [`lip uninstall`](uninstall.md) takes.

`lip list` changes nothing and takes no lock, so it works while another command or the server runs. In a workspace without a state file both groups are empty. A workspace of lip v0.x, which has a `tooth_lock.json` instead, is listed from that file without changing it.

## Options

| Option | Description |
| --- | --- |
| `--json` | Write the result as JSON. |
| `-h`, `--help` | Write the help and exit. |

## Output

The result goes to standard output. Without `--json` it is two trees, for people:

```
User-Requested
├── github.com/LiteLDev/bds@1.21.0
├── github.com/LiteLDev/LeviLamina@1.0.0
└── github.com/LiteLDev/LegacyScriptEngine#lua@0.9.0
Dependencies
└── github.com/LiteLDev/LegacyRemoteCall@0.9.0
```

An empty group shows `None`. This layout may change; use `--json` in scripts.

With `--json`, the result is a JSON object with exactly these members:

```json
{
  "ExplicitInstalled": [
    "github.com/LiteLDev/bds@1.21.0",
    "github.com/LiteLDev/LeviLamina@1.0.0",
    "github.com/LiteLDev/LegacyScriptEngine#lua@0.9.0"
  ],
  "ImplicitInstalled": [
    "github.com/LiteLDev/LegacyRemoteCall@0.9.0"
  ]
}
```

| Member | Value |
| --- | --- |
| `ExplicitInstalled` | An array of strings `<identity>@<version>`: the explicitly installed packages. |
| `ImplicitInstalled` | An array of strings `<identity>@<version>`: the dependency packages. |

## Exit status

| Code | Meaning |
| --- | --- |
| `0` | The list was written. |
| not `0` | A usage error, or the state file cannot be read. |

## Examples

List the packages:

```shell
lip list
```

Get the explicitly installed packages in a script, with [jq](https://jqlang.org/):

```shell
lip list --json | jq -r '.ExplicitInstalled[]'
```

## See also

- [Managing packages](../../guides/managing_packages.md)
- [Mods](../../interface/mods.md): the installed mods in the interface.
- [State file](../../concepts/state_file.md)
