# lip-list(1)

## NAME

lip-list - list the installed packages

## SYNOPSIS

```
lip list [--json] [-h | --help]
```

## DESCRIPTION

The result is the packages that the state file ([state.json(5)](state.json.md)) records, each as `<identity>@<version>`, in installation order, in two groups: the explicitly installed packages, then the dependency packages. `lip list` changes nothing.

## OPTIONS

- `--json`: The result is written as JSON ([lip(1), "Output"](lip.md#output)), as a JSON object with exactly these members:

  ```json
  { "ExplicitInstalled": ["<identity>@<version>", ...], "ImplicitInstalled": ["<identity>@<version>", ...] }
  ```

## SEE ALSO

[lip(1)](lip.md), [state.json(5)](state.json.md)
