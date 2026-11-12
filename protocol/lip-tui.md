# lip-tui(1)

## NAME

lip-tui - the interactive interface of lip

## SYNOPSIS

```
lip
```

## DESCRIPTION

The **interface** is what `lip` runs when it is started without an argument and its standard input and its standard output are both terminals ([lip(1), OPTIONS](lip.md#options)). It runs until the user quits it. What it shows, how the user controls it, and what `lip` writes to the terminal meanwhile, are not defined here.

### Commands

In the interface, the user asks for the commands of [lip(1)]. `lip` carries out each of them as the page of the command defines for the arguments the user gives, in the working directory of `lip`, with these differences:

- A result, a message and help are shown in the interface, instead of being written to standard output or standard error as [lip(1), "Output"](lip.md#output) defines.
- `lip pkg init` and `lip pkg migrate` ask the user in the interface, and do not fail because a standard stream is not a terminal.
- A command that fails does not end the interface.
- `lip run` is carried out as "Server" defines.

Before a workspace operation, `lip` may carry out the same command as a dry run ([lip(1), "Dry run"](lip.md#dry-run)).

When the user cancels a workspace operation in the interface while it changes the workspace, the operation is cancelled and undone ([lip-workspace(7), "Atomicity"](lip-workspace.md#atomicity), rule 2).

### Server

The interface runs the server as [lip-run(1)] defines, with these differences:

- The standard streams of the server are not those of `lip`. The server reads from its standard input the lines that the user enters for it in the interface, each ended by LF (U+000A), in their order. `lip` reads the standard output and the standard error of the server, and shows them in the interface. The standard input of the server does not end before the server exits.
- `lip` is also asked to stop ([lip-run(1), "Stopping"](lip-run.md#stopping)) when the user asks the interface to stop the server, and when the user quits the interface while the server runs.
- When the server exits, the interface goes on.

The interface runs at most one server at a time.

### Catalogue

The **catalogue** is the set of JSON files below `https://levimc.org/api/v4/mods/`:

| File | Describes |
|---|---|
| `index.json` | Every mod of the catalogue. |
| `<id>/index.json` | The mod whose member `id` in `index.json` is `<id>`. |

`lip` reads a file of the catalogue with an HTTPS GET of its URL, following redirects, whatever `github_proxy` ([config.toml(5)]) is. It sends these requests only while the interface runs, and writes nothing that it reads from them to a file. The content of the files is not defined here, except that the member `id` of a mod is a package argument ([lip(1), "Package arguments"](lip.md#package-arguments)) that names the mod.

A file of the catalogue that cannot be read is not a failure of a command.

## EXIT STATUS

`lip` exits with code 0 when the user quits the interface, whatever the commands carried out in it did, and whatever code the server exited with.

## SEE ALSO

[lip(1)], [lip-run(1)], [config.toml(5)], [lip-workspace(7)](lip-workspace.md)

[lip(1)]: lip.md
[lip-run(1)]: lip-run.md
[config.toml(5)]: config.toml.md
