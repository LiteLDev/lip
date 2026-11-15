# lip-tui(1)

## NAME

lip-tui - the interactive interface of lip

## SYNOPSIS

```
lip
```

## DESCRIPTION

The **interface** is what `lip` runs when it is started without an argument and its standard input and its standard output are both terminals ([lip(1), OPTIONS](lip.md#options)). The daemon carries it out ([lip-daemon(1)]). It runs until the user quits it. What it shows, how the user controls it, and what `lip` writes to the terminal meanwhile, are not defined here.

### Workspaces

The **workspace list** is the JSON file ([README, "Files"](README.md#files)) `workspaces.json` in the state directory ([lip-userspace(7)]): an array of the absolute paths of directories, as strings, in the order they were added. A list that does not exist, or that is not a JSON array, is read as empty; an element that is not a string holding an absolute path, or that names the same directory as an element before it, is left out. The **canonical form** of an absolute path is the path with its `.` and `..` components resolved, on Windows with `/` written as `\`, and without a separator at its end unless it is a root. Two paths name the same directory when their canonical forms are equal, ignoring case on Windows. `lip` writes the canonical forms of the paths, and writes the list atomically ([README, "Files"](README.md#files)).

The interface has one **selected workspace** at a time. When it starts, `lip` adds its working directory to the end of the workspace list unless the list names it, and selects it. The user may then:

- select a workspace of the list;
- add a directory that exists to the end of the list, unless the list names it, and select it; and
- remove a workspace other than the selected one from the list, which changes no file in it.

Adding and removing read the workspace list again before they write it. The interfaces that one process runs at the same time make their changes one at a time, and all of them are kept.

### Commands

In the interface, the user asks for the commands of [lip(1)]. `lip` carries out each of them as the page of the command defines for the arguments the user gives, with the selected workspace as its working directory, with these differences:

- A result, a message and help are shown in the interface, instead of being written to standard output or standard error as [lip(1), "Output"](lip.md#output) defines.
- `lip pkg init` and `lip pkg migrate` ask the user in the interface, and do not fail because a standard stream is not a terminal.
- A setup ([lip-setup(1)](lip-setup.md)), by `lip setup` or `lip install`, asks the user in the interface whether they accept the license, unless the user gave `--accept-eula`, and does not fail because a standard stream is not a terminal. When the user applies a setup whose dry run named left out packages, that is their answer to whether to uninstall them.
- A command that fails does not end the interface.
- `lip run` is carried out as "Server" defines.

Before a workspace operation, `lip` may carry out the same command as a dry run ([lip(1), "Dry run"](lip.md#dry-run)).

When the user cancels a workspace operation in the interface while it changes the workspace, the operation is cancelled and undone ([lip-workspace(7), "Atomicity"](lip-workspace.md#atomicity), rule 2).

### Server

The interface runs the server as [lip-run(1)] defines, with these differences:

- The standard streams of the server are not those of `lip`. The server reads from its standard input the lines that the user enters for it in the interface, each ended by LF (U+000A), in their order. `lip` reads the standard output and the standard error of the server, and shows them in the interface. The standard input of the server does not end before the server exits.
- `lip` is also asked to stop ([lip-run(1), "Stopping"](lip-run.md#stopping)) when the user asks the interface to stop the server, and, for every server that the interface started, when the user quits the interface while that server runs. The interface ends once those servers have exited.
- When the server exits, the interface goes on.

There is at most one server per workspace. Its workspace is the selected workspace when the user starts it, and the interface shows the server of the selected workspace. The interfaces that one daemon carries out at the same time share their servers: each shows what a server of its selected workspace writes, sends it the lines its user enters, and asks it to stop when its user asks to, whichever interface started it.

### Configuration

The interface shows the **configuration files** of the selected workspace, and lets the user edit them:

- `server.properties`, `allowlist.json` and `permissions.json` at the workspace root, the files of the server; and
- the configuration files of each installed mod ([lip-workspace(7), "Terms"](lip-workspace.md#terms)), found in its mod directory `mods/<mod name>/`: every regular file below `config/` in it; when there is none, the regular files directly in it, other than `manifest.json`, whose names end in `.json`, `.ini`, `.toml`, `.yml` or `.yaml`, and `data/config.json` in it.

A configuration file that does not exist is not created, except `allowlist.json` and `permissions.json`, which `lip` reads as the empty array `[]` when they do not exist. The server and the mods should create their files when the server first starts.

`lip` writes a configuration file only when the user saves the changes they made to it, and writes it atomically ([README, "Files"](README.md#files)). It writes nothing else to the workspace for it. A change takes effect when the server is next started.

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

## SEE ALSO

[lip(1)], [lip-daemon(1)], [lip-run(1)], [config.toml(5)], [lip-workspace(7)](lip-workspace.md), [lip-userspace(7)]

[lip(1)]: lip.md
[lip-daemon(1)]: lip-daemon.md
[lip-run(1)]: lip-run.md
[lip-userspace(7)]: lip-userspace.md
[config.toml(5)]: config.toml.md
[JSON]: https://www.rfc-editor.org/info/rfc8259
