# lip(1)

## NAME

lip - run a LeviLamina server and manage its packages

## SYNOPSIS

```
lip
lip (-h | --help)
lip (-v | --version)
lip (cache | config | pkg) (-h | --help)
lip <command> [<argument>...]
```

## DESCRIPTION

`<command>` is one of the command names:

| Command name | Page |
|---|---|
| `install` | [lip-install(1)] |
| `uninstall` | [lip-uninstall(1)] |
| `update` | [lip-update(1)] |
| `list` | [lip-list(1)] |
| `run` | [lip-run(1)] |
| `cache clean` | [lip-cache(1)] |
| `config get`, `config set`, `config list`, `config delete` | [lip-config(1)] |
| `pkg init`, `pkg pack`, `pkg view`, `pkg migrate` | [lip-pkg(1)] |

The commands of the command group `pkg` are for developing packages; every other command is for running a server.

### Terms

- **argument**: One element of the argument list that `lip` is started with, the program name excluded.
- **command**: An invocation of `lip` with a command name, such as `lip install` or `lip config get`; not a hook command.
- **command group**: `cache`, `config` or `pkg`, the first word of a command name of two words.
- **option**: An argument, or two consecutive arguments, that "Arguments" reads as an option name with or without a value.
- **flag**: An option that takes no value.
- **operand**: An argument of a command that is not part of an option.
- **usage error**: A list of arguments that "Arguments" or OPTIONS rejects.
- **result**: The data a command is asked for; only `list`, `pkg view`, `config get` and `config list` have one.
- **message**: Any text other than a result or help (progress, information, warnings, errors, the final success line); its wording is not defined here.
- **help**: Text describing how to invoke `lip` or a command; its wording is not defined here.
- **workspace operation**: An operation ([lip-workspace(7)]) that `install`, `uninstall` or `update` carries out without `-n` or `--dry-run`.

### Arguments

Command names, command group names and option names are compared case-sensitively.

The arguments are read from first to last:

1. When any argument is `--`, the list is a usage error.
2. When there is no argument, or the first argument starts with `-`, the list is read as OPTIONS defines.
3. Otherwise, the first argument must be a command name of one word or a command group, and the argument after a command group must be `-h`, `--help`, or the second word of a command name of that group. It is a usage error otherwise, and also when nothing follows a command group. The arguments after the command name are the arguments of the command.

Each argument of a command that starts with `-` is an option, in one of these forms:

| Form | Meaning |
|---|---|
| `--<name>` | The long option `<name>`. |
| `-<c>` | The short option `<c>`, a single character. |
| `--<name>=<value>` | The long option `<name>`, with the value `<value>`. |
| `-<c>=<value>` | The short option `<c>`, with the value `<value>`. |

An option that takes a value and is not written with `=` takes the next argument as its value; that argument must not start with `-`. A value must not be empty. The meaning of a flag written with `=<value>`, such as `--json=true`, and of any argument that starts with `-` and has none of these forms, such as `-nh`, is not defined here. Every argument of a command that does not start with `-` and is not the value of an option is an operand.

Options and operands may appear in any order after the command name. Operands are assigned to the operands of the command in the order given.

A flag given more than once has the same effect as given once. An option that takes a value and is given more than once has the value of its last occurrence.

The arguments of a command are a usage error when:

1. an option is not an option of the command;
2. an option that takes a value has none;
3. an operand or an option that the synopsis of the command requires is absent; or
4. there are more operands than the command takes.

### Help

Every command has the flags `-h` and `--help`. When the arguments of a command contain `-h` or `--help`, and are not a usage error, or are a usage error only by rule 1 or rule 3 of "Arguments", the command writes its help to standard output and exits with code 0; it does nothing else.

### Working directory

The workspace of every command is the current working directory. A relative path in an argument is relative to the current working directory.

### Output

- A result is written to standard output; a message is written to standard error.
- When a command that has a `--json` option is given it, standard output receives only the result, as [JSON] of the shape the command defines, followed by a line break. When it is not given, the layout of the result is not defined here.
- The result of `pkg view` is always written as JSON, as [lip-pkg(1)] defines.

### Package arguments

A **package argument** is an operand that names a source ([lip-package(7), "Sources"](lip-package.md#sources)); the page of each command says which operands are package arguments. It is classified by the first of these rules that applies:

1. An argument starting with `https://` is the URL source of that string. An argument starting with another URI scheme followed by `://`, such as `http://` or `file://`, is invalid.
2. Let L be the argument without the part from its last `#` on; or the whole argument, when the argument names an existing file or directory. The argument is a local path when:
   - L starts with `./`, `../`, `.\` or `..\`; or
   - L is `.` or `..`; or
   - L is an absolute path; or
   - the argument contains no `@`, and L ends with `.zip` (compared ASCII case-insensitively) or names an existing file or directory.

   Its source is the [`file:` URI] of the absolute path of L, followed by the rest of the argument. When L does not exist, the argument is invalid.
3. Any other argument is a remote repository source. When its location ([lip-package(7), "Sources"](lip-package.md#sources)) contains no `/`, the source is `github.com/LiteLDev/` followed by the argument; for example, `LeviLamina@1.9.7` is the source `github.com/LiteLDev/LeviLamina@1.9.7`. Otherwise the source is the argument.

An argument whose source is not valid is invalid. A command with an invalid package argument fails before anything changes.

A package argument is made into the request (source, range) whose range matches every version.

### Environment

`lip` manages only workspaces whose side is `"server"`. The environment of `install`, `uninstall`, `update` and `run` is:

- side: `"server"`. When the state file records another side, the command fails.
- platform: the value of the `--platform` option of the command, when given; otherwise the platform the state file records; otherwise `"win-x64"`, whatever system `lip` runs on.

A workspace operation has the environment above, and records it in the state file, replacing the one recorded.

### Workspace operations

Each workspace operation is carried out as [lip-workspace(7), "Carrying out an operation"](lip-workspace.md#carrying-out-an-operation) defines.

A workspace operation that finds the workspace lock held by another process fails at once, and changes nothing.

When the user interrupts `lip` (Ctrl+C) while a workspace operation changes the workspace, the operation is cancelled and undone ([lip-workspace(7), "Atomicity"](lip-workspace.md#atomicity), rule 2); a second interruption ends `lip` at once, leaving the operation to be recovered (rule 5). An interruption before the workspace starts to change ends `lip` at once.

### Dry run

With `-n` or `--dry-run`, the command carries out every step that changes nothing, and writes as messages what the operation would do to which packages; the page of the command says what. It does not take the workspace lock. When `.lip/journal` exists, it writes a message that an operation was interrupted, does not recover it, and plans with the workspace as it is. It creates, replaces or deletes no file or directory other than a failure report (EXIT STATUS), and runs no hook command.

## OPTIONS

- No argument: when the standard input and the standard output of `lip` are both terminals, `lip` runs the interface ([lip-tui(1)]); otherwise `lip` writes the help for `lip` to standard output.
- `-h`, `--help` as the first argument: `lip` writes the help for `lip` to standard output, whatever arguments follow.
- `-v`, `--version` as the first argument: `lip` writes its version ([SemVer]) followed by a line break to standard output, whatever arguments follow.
- Any other first argument starting with `-`: the list is a usage error.
- `lip cache`, `lip config` or `lip pkg`, followed by `-h` or `--help`: `lip` writes the help for the command group to standard output.

Each of these exits with code 0, except the usage error, and the interface, which exits as [lip-tui(1), EXIT STATUS](lip-tui.md#exit-status) defines.

## EXIT STATUS

A command that succeeds exits with code 0. A command that fails, including on a usage error, writes a message describing the cause to standard error and exits with a non-zero code.

`lip run` that ran the server exits as [lip-run(1), EXIT STATUS](lip-run.md#exit-status) defines instead.

A command that fails other than on a usage error may write a **failure report**: a file below `.lip/logs/` of its workspace, whose content is not defined here. When it writes one, it names the file in a message.

## FILES

- The config directory and the cache directory: [lip-userspace(7)].
- `.lip/` in the workspace: [lip-workspace(7), "The .lip directory"](lip-workspace.md#the-lip-directory).

## SEE ALSO

[lip-tui(1)], [lip-package(7)], [lip-workspace(7)], [lip-userspace(7)], [lip-compatibility(7)]

[lip-install(1)]: lip-install.md
[lip-uninstall(1)]: lip-uninstall.md
[lip-update(1)]: lip-update.md
[lip-list(1)]: lip-list.md
[lip-run(1)]: lip-run.md
[lip-cache(1)]: lip-cache.md
[lip-config(1)]: lip-config.md
[lip-pkg(1)]: lip-pkg.md
[lip-tui(1)]: lip-tui.md
[lip-package(7)]: lip-package.md
[lip-workspace(7)]: lip-workspace.md
[lip-userspace(7)]: lip-userspace.md
[lip-compatibility(7)]: lip-compatibility.md
[JSON]: https://www.rfc-editor.org/info/rfc8259
[SemVer]: https://semver.org/spec/v2.0.0.html
[`file:` URI]: https://www.rfc-editor.org/info/rfc8089
