# lip-pack(1)

## NAME

lip-pack - pack a directory into a ZIP archive

## SYNOPSIS

```
lip pack [<DIR>] (-o | --output) <FILE> [-h | --help]
```

## DESCRIPTION

`lip pack` writes to `<FILE>`, replacing it, a ZIP archive holding every regular file below `<DIR>` at its path relative to `<DIR>`, except `<FILE>` itself and, at the root of `<DIR>`, everything below `.git/` and `.lip/`. Such an archive can be an asset of type `"mcmod"`.

`<DIR>` is the directory to pack; without it, the current working directory. When it is not an existing directory, the command fails and writes nothing.

## OPTIONS

- `-o <FILE>`, `--output <FILE>`: The path of the ZIP archive to write.

## SEE ALSO

[lip(1)](lip.md), [lip-package(7)](lip-package.md)
