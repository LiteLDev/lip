# lip-uri(7)

## NAME

lip-uri - links that open the interface to install a package

## SYNOPSIS

```
lip://install/<module path>[?<parameter>[&<parameter>]...][#<fragment>]
lip --uri <link>
```

## DESCRIPTION

A **link** is a URI ([RFC 3986]) of the scheme `lip`, such as one of a web page, that asks `lip` to install a remote repository ([lip-package(7), "Sources"](lip-package.md#sources)) into a workspace that the user chooses. For example:

```
lip://install/github.com/IceBlcokMC/PLand?version=0.26.1&variant=headless
```

A link only opens the interface ([lip-tui(1)]): it installs nothing until the user confirms the operation there ("Opening a link"). The program that opens the link, and the web page that holds it, choose neither the workspace nor anything else than the package.

LeviLauncher reads the links of the scheme `levilauncher` with the same grammar.

### Reading a link

A link is read as follows; a link that one of these rules rejects is **invalid**.

1. It has at most 2048 characters, each of them one that RFC 3986 allows in a URI, every `%` starting a percent-encoded octet (`%` and two hexadecimal digits).
2. It starts with `lip://`, compared ASCII case-insensitively.
3. The **fragment**, from the first `#` on, is not read. The **query** is what follows the first `?` before the fragment, and is empty when there is none.
4. The **action** is the text after `lip://` up to the first `/`, `?` or `#`, compared ASCII case-insensitively. It must be `install`.
5. The **path** is the text after that `/` up to the query or the fragment, without one `/` at its end. It must not be empty. Its percent-encoded octets are decoded, and the octets read as UTF-8, which they must be. The **module path** is the result, its case kept. It must be a valid module path, as the location of a remote repository is ([lip-package(7), "Sources"](lip-package.md#sources)); so it is not a URL, a local path or a package argument with a variant or a ref, and its first element, the host, has no uppercase letter. There is no shorthand: `lip://install/LeviLamina` is invalid.
6. The query is split at each `&` into **parameters**, empty ones left out. A parameter is a name, and a value after its first `=`, or the empty value without one. The name and the value are decoded as in rule 5; `+` is itself, not a space. These parameters are read, and every other one is not:
   - `version`: The version to install, a SemVer version written without a leading `v`, as the ref `<version>` of a remote repository is ([lip-package(7), "Sources"](lip-package.md#sources)). A branch, a tag or a commit (`branch:`, `tag:`, `rev:`) is invalid. Without it, or with the empty value, the link names no version.
   - `variant`: The variant to install, valid as the variant of a source is ([lip-package(7), "Sources"](lip-package.md#sources)). Without it, or with the empty value, the link names the default variant.

   A link that has `version` or `variant` more than once is invalid.

The **package argument of a link** is its module path, followed by `#` and its variant unless it names the default variant, followed by `@` and its version when it names one. It is a remote repository, never a URL or a local path, which only a command line installs.

### Opening a link

`lip --uri <link>` opens the link `<link>`: it runs the interface as `lip` without arguments does ([lip(1), OPTIONS](lip.md#options)), with these differences:

1. When `<link>` is invalid, or the standard input or the standard output of `lip` is not a terminal, `lip` writes a message to standard error and exits with a non-zero code, before it runs the interface. On Windows, when no other process shares its console, it then waits for the user to press a key before it exits, so that the message stays on the screen.
2. `lip` does not add its working directory to the workspace list ([lip-tui(1), "Workspaces"](lip-tui.md#workspaces)), and does not select it: it selects the last workspace of the list. When the list is empty, no workspace of the list is selected.
3. The interface asks the user, before anything else, whether to install the package argument of the link. It shows the package argument, and what the catalogue ([lip-tui(1), "Catalogue"](lip-tui.md#catalogue)) says of the mod whose `id` is the module path, compared as package names are: its name, its author, the latest version when the link names none, and the packages that the version and the variant need. It shows the selected workspace, which the user may change there as "Workspaces" allows, or that no workspace of the list is selected.
4. When the catalogue lists no such mod, or cannot be read, the interface says so as a warning, and the choice that is selected at first is to install nothing. When the link names a version that the catalogue does not list for the mod, it says so too. `lip` still lets the user install the package: a link names a remote repository, which a user may install with `lip install` all the same.
5. When the user confirms, while a workspace of the workspace list is selected, `lip` carries out `lip install` of the package argument of the link in the selected workspace, as [lip-tui(1), "Commands"](lip-tui.md#commands) defines; the user applies or cancels its plan as for every workspace operation of the interface. When the selected workspace has no game, the interface offers to set up its server instead, and installs nothing. When the user confirms while no workspace of the list is selected, `lip` installs nothing, and has the user choose one.
6. When the user declines, the interface goes on, and `lip` installs nothing.

`--uri` is an option only as the first of exactly two arguments; otherwise the arguments are read as [lip(1), "Arguments"](lip.md#arguments) defines.

### Registration

The Windows Installer package of `lip` registers the URL scheme `lip`, for the user or for every user, as it installs `lip` ([lip-self-update(1), "Terms"](lip-self-update.md#terms)), in the key `Software\Classes\lip` of `HKEY_CURRENT_USER` or `HKEY_LOCAL_MACHINE`:

| Key | Value | Data |
|---|---|---|
| `Software\Classes\lip` | (default) | `URL:lip` |
| `Software\Classes\lip` | `URL Protocol` | the empty string |
| `Software\Classes\lip\DefaultIcon` | (default) | `"<directory>lip.exe",0` |
| `Software\Classes\lip\shell\open\command` | (default) | `"<directory>lip.exe" --uri "%1"` |

`<directory>` is the installation directory, with a separator at its end. Windows starts `lip` with a console of its own when the user opens a link. Uninstalling the package removes these keys. `install.sh` ([install.sh(1)](install.sh.md)) registers no URL scheme.

## EXIT STATUS

`lip --uri` exits as the interface does ([lip-tui(1), EXIT STATUS](lip-tui.md#exit-status)), and with a non-zero code in the cases of rule 1 of "Opening a link".

## SEE ALSO

[lip(1)](lip.md), [lip-tui(1)], [lip-install(1)](lip-install.md), [lip-package(7)](lip-package.md)

[lip-tui(1)]: lip-tui.md
[RFC 3986]: https://www.rfc-editor.org/info/rfc3986
