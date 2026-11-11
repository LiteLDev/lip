# lip-uninstall(1)

## NAME

lip-uninstall - uninstall packages from the workspace

## SYNOPSIS

```
lip uninstall <PACKAGES>... [-n | --dry-run] [--ignore-scripts] [--no-dependencies] [--platform <RID>] [-h | --help]
```

## DESCRIPTION

Each of `<PACKAGES>` names installed packages; it is not a package argument. It names the installed packages that match it by the first of these rules that matches any installed package:

1. The identity of the package equals the operand.
2. The source of the package equals the operand made into a source as a package argument is ([lip(1), "Package arguments"](lip.md#package-arguments)), with the locations compared ASCII case-insensitively. An operand without a variant matches every variant, and an operand without a ref matches every ref.

When an operand names no installed package, the command fails.

Without `--no-dependencies`:

- Every named package must be explicitly installed; otherwise the command fails.
- The named packages stop being explicitly installed. Each package that no remaining explicitly installed package needs is uninstalled; the others stay installed.

## OPTIONS

- `-n`, `--dry-run`: A dry run ([lip(1), "Dry run"](lip.md#dry-run)). The messages name the packages that the operation would uninstall or mark no longer explicitly installed.
- `--ignore-scripts`: The operation runs no hook command. Nothing else changes.
- `--no-dependencies`: Exactly the named packages are uninstalled, dependency packages included, and no other package is. [lip-workspace(7), "Valid states"](lip-workspace.md#valid-states) is not guaranteed to hold afterwards.
- `--platform <RID>`: The platform of the environment ([lip(1), "Environment"](lip.md#environment)) is `<RID>`.

## SEE ALSO

[lip(1)](lip.md), [lip-install(1)](lip-install.md), [lip-workspace(7)](lip-workspace.md)
