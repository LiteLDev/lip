# lip-tui(1)

## NAME

lip-tui - the interactive interface of lip

## SYNOPSIS

```
lip
```

## DESCRIPTION

The **interface** is what `lip` runs when it is started without an argument and its standard input and its standard output are both terminals ([lip(1), OPTIONS](lip.md#options)). The daemon carries it out ([lip-daemon(1)]). It runs until the user quits it. What it shows, how the user controls it, and what `lip` writes to the terminal meanwhile, are not defined here.

### Language

The **interface language** is the language of the text of the interface itself: English, Simplified Chinese or Traditional Chinese. It is the value of `language` ([config.toml(5), "User configuration"](config.toml.md#user-configuration)) when `language` has a value, read as "GitHub reachability" reads the configuration; otherwise the client language; and English when there is no client language.

The **client language** is that of the first **locale** of the client ([lip-daemon(1)]) that names one:

- on Windows, the name of the display language of the user, such as `zh-CN`;
- elsewhere, the values of the environment variables `LC_ALL`, `LC_MESSAGES` and `LANG` that are not empty, in this order, each without what follows its first `.` or `@`, such as `zh_CN` for `zh_CN.UTF-8`.

The subtags of a locale are its parts between `-` and `_`, compared ignoring case. A locale whose first subtag is `zh` names Traditional Chinese when none of its other subtags is `Hans` and one of them is `Hant`, `TW`, `HK` or `MO`, and Simplified Chinese otherwise. A locale whose first subtag is `en` names English. Any other locale names no language.

When the user changes `language` in the interface, the interface language follows at once. What a command says, such as its messages and its results, is not in the interface language.

### Workspaces

The **workspace list** is the JSON file ([README, "Files"](README.md#files)) `workspaces.json` in the state directory ([lip-userspace(7)]): an array of the absolute paths of directories, as strings, in the order they were added. A list that does not exist, or that is not a JSON array, is read as empty; an element that is not a string holding an absolute path, or that names the same directory as an element before it, is left out. The **canonical form** of an absolute path is the path with its `.` and `..` components resolved, on Windows with `/` written as `\`, and without a separator at its end unless it is a root. Two paths name the same directory when their canonical forms are equal, ignoring case on Windows. `lip` writes the canonical forms of the paths, and writes the list atomically ([README, "Files"](README.md#files)).

The interface has one **selected workspace** at a time. When it starts, `lip` adds its working directory to the end of the workspace list unless the list names it, and selects it. The user may then:

- select a workspace of the list;
- add a directory to the end of the list, unless the list names it, and select it: a directory that exists, or one that `lip` first creates, with its missing parents, when the user asks for a new one; and
- remove a workspace other than the selected one from the list, which changes no file in it.

Selecting, adding and removing a workspace install nothing in it: a game is set up as "Server setup" defines.

Adding and removing read the workspace list again before they write it. The interfaces that one process runs at the same time make their changes one at a time, and all of them are kept.

### Server setup

The user may ask the interface to **set up** the server of the selected workspace, whether or not it has a game. The user then chooses, in this order:

1. a game version G: a version that the builds list ([lip-package(7), "The server"](lip-package.md#the-server)) names and that step 2 offers a build for, offered with the versions that are not prereleases first, then the others, each from highest to lowest;
2. a build B ([lip-setup(1), "Terms"](lip-setup.md#terms)): `windows` is offered when the builds list has a build of G for `win-x64`, and `linux` when it has one for `linux-x64` and `lip` runs on Linux on x64. When one build only is offered, it is B, and the user is not asked; and
3. when B is `windows`, the loader: a version V of the loader source ([lip-setup(1), "Terms"](lip-setup.md#terms)) among the versions that fit G, or no loader. When B is `linux`, the setup has no loader, and the user is not asked.

The **versions that fit** G are those of the candidates of Resolve(`github.com/LiteLDev/LeviLamina`, any version, environment) ([lip-package(7), "Resolution"](lip-package.md#resolution)) that fit G, from the first; the search may stop as [lip-setup(1), "The loader"](lip-setup.md#the-loader) defines, and at a candidate that does not fit G after one that does. `lip` reads the builds list and resolves these candidates as a setup does.

When the user has made these choices, `lip` carries out `lip setup --game G --build B` with `--loader levilamina@V` or `--no-loader` in the selected workspace, as "Commands" defines: it asks the user in the interface whether they accept the license ([lip-setup(1), "The license"](lip-setup.md#the-license)). When the user cancels before the last choice, nothing is carried out.

### GitHub reachability

When the interface starts and `github_proxy` is not configured ([config.toml(5), "User configuration"](config.toml.md#user-configuration)), `lip` checks whether it reaches GitHub, while the interface goes on. Reading the configuration for this writes no file, as in a dry run ([lip(1), "Dry run"](lip.md#dry-run)).

The **reachability check** is an HTTPS GET of `https://github.com/LiteLDev/lip.git/info/refs?service=git-upload-pack`, sent directly, following redirects. It succeeds when the headers of a response whose status is 2xx arrive within 10 seconds of the start of the request; otherwise it fails. `lip` writes nothing that it reads from the response to a file. The check is made at most once per start of the interface, and only when `github_proxy` is not configured.

When the check fails, the interface asks the user how `lip` reaches GitHub, offering:

- **a proxy**: the user enters a URI. `lip` makes the check through it, as for a `github_proxy` of that value: the request is sent to the URL that the proxy makes of it, and is not sent directly when it fails. When that check succeeds, or when it fails and the user then asks to keep the proxy, `lip` sets `github_proxy` to the URI, as `lip config set github_proxy <URI>` does ([lip-config(1)](lip-config.md)).
- **direct**: `lip` sets `github_proxy` to the empty string, as `lip config set github_proxy ""` does; `github_proxy` is then configured, and the interface does not ask again.
- **later**: `lip` changes nothing, and asks again the next time the interface starts and the check fails.

The interface asks once no other question is open in it; the question changes nothing until the user answers it.

### Commands

In the interface, the user asks for the commands of [lip(1)]. `lip` carries out each of them as the page of the command defines for the arguments the user gives, with the selected workspace as its working directory, with these differences:

- A result, a message and help are shown in the interface, instead of being written to standard output or standard error as [lip(1), "Output"](lip.md#output) defines.
- `lip pkg migrate` asks the user in the interface, and does not fail because a standard stream is not a terminal. The interface creates and edits `lip.toml` as "Package file" defines, instead of carrying out `lip pkg init`.
- A setup ([lip-setup(1)](lip-setup.md)), by `lip setup` or `lip install`, asks the user in the interface whether they accept the license, unless the user gave `--accept-eula`, and does not fail because a standard stream is not a terminal. When the user applies a setup whose dry run named left out packages, that is their answer to whether to uninstall them.
- Preparing the system ([lip-workspace(7), "System dependencies"](lip-workspace.md#system-dependencies)) asks the user in the interface whether to install Rosetta 2, unless the user gave `--accept-rosetta-license`, and does not fail because a standard stream is not a terminal.
- A command that fails does not end the interface.
- `lip run` is carried out as "Server" defines.

Before a workspace operation, `lip` may carry out the same command as a dry run ([lip(1), "Dry run"](lip.md#dry-run)). The operation that the user then asks for may use what that dry run read from the network, such as the version tags, the package files and the archives it downloaded, instead of reading it again; it reads the workspace only after it took the workspace lock ([lip-workspace(7), "Carrying out an operation"](lip-workspace.md#carrying-out-an-operation)). When that dry run finds that the operation would install no package, uninstall none, and leave the state file with the value it has, the interface does not offer to carry out the operation; a state file that does not exist has no value.

While the server of the selected workspace runs ("Server"), the interface carries out no `lip setup`, `lip install`, `lip uninstall` or `lip update` for it.

When the user cancels a workspace operation in the interface while it changes the workspace, the operation is cancelled and undone ([lip-workspace(7), "Atomicity"](lip-workspace.md#atomicity), rule 2).

### Package file

The user may ask the interface to create the package file `lip.toml` ([lip.toml(5)]) of the selected workspace when it has none, and to edit it when it has one. `lip` reads a `lip.toml` that exists as a TOML file ([README, "Files"](README.md#files)), and the interface does not let the user edit one that is not a TOML file, or in which a key that [lip.toml(5), "Keys"](lip.toml.md#keys) describes holds a value of another type than the one it states.

While the user edits it, `lip` may read, for each dependency: the catalogue ("Catalogue"); for `github.com/LiteLDev/bds`, the builds list ([lip-package(7), "The server"](lip-package.md#the-server)); and, for a package id that the catalogue does not list, the version tags of its repository ([lip-package(7), "Remote repositories"](lip-package.md#remote-repositories)). Before it writes `lip.toml`, it may read the versions of the game and of the loader as "New server" reads them. It reads them as a dry run does ([lip(1), "Dry run"](lip.md#dry-run)), and writes nothing that it reads from them to a file.

`lip` writes `lip.toml` only when the user saves it and then confirms, and writes it atomically ([README, "Files"](README.md#files)). What it writes is a TOML file whose value is that of the file it read, or an empty object when there was none, with `format` set to `4` and each key that lip.toml(5) describes set to the value the user gave it, or removed when the user removed it; every other key keeps its value. An optional string that the user leaves empty, and an empty string in `info.tags`, `urls`, `preserve`, `purge` or `hooks`, are not written. `lip` saves no `lip.toml` that is not a valid package file. When `lip.toml` changed after `lip` read it, `lip` writes it only when the user confirms once more. Which characters of the file read, such as its comments, the file written keeps is not defined here.

### Server

The server that the user starts in the interface is the managed server of the selected workspace ([lip-run(1), "Managed servers"](lip-run.md#managed-servers)): `lip` starts it as `lip run --managed --detach` would, with the selected workspace as its working directory and the environment variables of the client of the interface, unless it runs already, except that preparing the system asks the user in the interface whether to install Rosetta 2 ([lip-workspace(7), "System dependencies"](lip-workspace.md#system-dependencies)), as "Commands" defines. When it fails before its server starts, the failure is shown in the interface.

The interface shows the managed server of the selected workspace, whichever client or interface started it, and whether it runs, waits to be restarted ([lip-run(1), "Restarting"](lip-run.md#restarting)), or ran and with which exit code. It shows what that server writes to its standard output and its standard error, and the messages of `lip` about it, as an attached client gets them ([lip-run(1), "Attaching"](lip-run.md#attaching)). It gives that server the lines that the user enters for it, each ended by LF (U+000A), and asks `lip` to stop for it ([lip-run(1), "Stopping"](lip-run.md#stopping)) when the user asks the interface to stop the server. The interface shows, for each workspace of the workspace list, whether its managed server runs.

For "Commands" and "Configuration", the server of a workspace **runs** while its managed server runs, or while a process holds the server lock of the workspace ([lip-workspace(7), "Server lock"](lip-workspace.md#server-lock)), as a `lip run` that is not managed does.

Quitting the interface does not stop a managed server. When the server exits, the interface goes on.

When `lip` itself carries out the interface ([lip-daemon(1), "Fallback"](lip-daemon.md#fallback)), the servers that it starts are not managed servers: `lip` runs them as above, in its own process, and when the user quits the interface while any of them runs, `lip` is asked to stop for each, and the interface ends once they have all exited.

### Configuration

The interface shows the **configuration files** of the selected workspace, and lets the user edit them:

- `server.properties`, `allowlist.json` and `permissions.json` at the workspace root, the files of the server; and
- the configuration files of each installed mod ([lip-workspace(7), "Terms"](lip-workspace.md#terms)), found in its mod directory `mods/<mod name>/`: every regular file below `config/` in it; when there is none, the regular files directly in it, other than `manifest.json`, whose names end in `.json`, `.ini`, `.toml`, `.yml` or `.yaml`, and `data/config.json` in it.

A configuration file that does not exist is not created, except `allowlist.json` and `permissions.json`, which `lip` reads as the empty array `[]` when they do not exist. The server and the mods should create their files when the server first starts.

`lip` writes a configuration file only when the user saves the changes they made to it, and writes it atomically ([README, "Files"](README.md#files)). It writes nothing else to the workspace for it. A change takes effect when the server is next started.

While the server of the selected workspace runs ("Server"), the interface does not save `server.properties`, `allowlist.json` or `permissions.json`. It saves the configuration files of the mods all the same.

A **property** of `server.properties` is a line of it that does not start with `#` and contains `=`: its key is the text before the first `=`, without the spaces around it, and its value the text after that `=`. Saving `server.properties` replaces the value of each property that the user changed, and changes no other character of the file.

`allowlist.json`, `permissions.json`, and a configuration file of a mod whose name ends in `.json`, are read as [JSON] texts in UTF-8 that may contain comments (`//` to the end of the line, and `/*` to `*/`) and a comma after the last member of an object or the last element of an array. Saving one writes it as a JSON file ([README, "Files"](README.md#files)) holding the value read with the user's changes, the members of each object in the order they were read in and the members the user added after them. The whitespace of the file written is not defined here; it holds no comments.

A configuration file that `lip` cannot read as this section defines is shown, and is not written. The editing of other configuration files, and what the interface shows of each, are not defined here.

### Catalogue

The **catalogue** is the set of JSON files below `https://levimc.org/api/v4/mods/`:

| File | Describes |
|---|---|
| `index.json` | Every mod of the catalogue. |
| `<id>/index.json` | The mod whose member `id` in `index.json` is `<id>`. |

`lip` reads a file of the catalogue with an HTTPS GET of its URL, following redirects, whatever `github_proxy` ([config.toml(5)]) is. It sends these requests only while the interface runs, and writes nothing that it reads from them to a file. The content of the files is not defined here, except that the member `id` of a mod is a package argument ([lip(1), "Package arguments"](lip.md#package-arguments)) that names the mod.

A file of the catalogue that cannot be read is not a failure of a command.

## EXIT STATUS

`lip` exits with code 0 when the user quits the interface, whatever the commands carried out in it did, and whatever code the servers exited with.

## FILES

- `workspaces.json` in the state directory: the workspace list ("Workspaces").
- `config.toml` in the config directory: the user configuration, which gives the interface language ("Language"), and which "GitHub reachability" may write ([config.toml(5)]).

## SEE ALSO

[lip(1)], [lip-config(1)](lip-config.md), [lip-daemon(1)], [lip-run(1)], [lip-setup(1)](lip-setup.md), [config.toml(5)], [lip.toml(5)], [lip-workspace(7)](lip-workspace.md), [lip-userspace(7)]

[lip(1)]: lip.md
[lip-daemon(1)]: lip-daemon.md
[lip-run(1)]: lip-run.md
[lip-userspace(7)]: lip-userspace.md
[lip.toml(5)]: lip.toml.md
[config.toml(5)]: config.toml.md
[JSON]: https://www.rfc-editor.org/info/rfc8259
