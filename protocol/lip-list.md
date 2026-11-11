# lip-list(1)

## NAME

lip-list - list the installed packages

## SYNOPSIS

```
lip list [--json] [-h | --help]
```

## DESCRIPTION

The result is the packages that the state file ([state.json(5)](state.json.md)) records; for a workspace without `.lip/state.json`, the state file that [lip-compatibility(7), "tooth_lock.json"](lip-compatibility.md#tooth_lockjson) derives for the side `"server"` and the platform `"win-x64"`, each as `<identity>@<version>`, in installation order, in two groups: the explicitly installed packages, then the dependency packages. `lip list` changes nothing.

## OPTIONS

- `--json`: The result is written as JSON ([lip(1), "Output"](lip.md#output)), as a JSON object with exactly these members:

  ```json
  { "ExplicitInstalled": ["<identity>@<version>", ...], "ImplicitInstalled": ["<identity>@<version>", ...] }
  ```

## SEE ALSO

[lip(1)](lip.md), [state.json(5)](state.json.md)
