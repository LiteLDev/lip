# Command line

This page tells you how to invoke `lip`: which commands there are, how arguments are read, where the output goes, what the exit status means, and what a dry run and Ctrl+C do. Each command has its own page; this one holds what they all share.

## Synopsis

```
lip
lip (-h | --help)
lip (-v | --version)
lip (cache | config | daemon | pkg) (-h | --help)
lip <command> [<argument>...]
```

In a synopsis, `word` is written as it is, `<name>` is replaced by a value, `[x]` is optional, `x | y` is either one, `(x | y)` groups alternatives, and `x...` is `x` one or more times.

| Invocation | What lip does |
| --- | --- |
| `lip` | Runs the [interface](interface.md) when standard input and standard output are both terminals. Otherwise writes the help for `lip` to standard output. |
| `lip -h`, `lip --help` | Writes the help for `lip` to standard output, whatever follows. |
| `lip -v`, `lip --version` | Writes its version, a [SemVer](https://semver.org/spec/v2.0.0.html) version, and a line break to standard output, whatever follows. |
| `lip cache -h`, `lip config -h`, `lip daemon -h`, `lip pkg -h` | Writes the help for the command group to standard output. `--help` does the same. |
| `lip --daemon` | Runs as the [daemon](commands/daemon.md) until it exits, and writes nothing. lip starts it this way itself; you never need to. |
| `lip <command> ...` | Carries out the command. |

Writing the help or the version exits with code 0. Any other first argument that starts with `-` is a [usage error](#usage-errors).

## Commands

Commands for running a server:

| Command | Does |
| --- | --- |
| [`lip setup`](commands/setup.md) | Chooses the game and the loader of the workspace. |
| [`lip install`](commands/install.md) | Installs packages into the workspace. |
| [`lip uninstall`](commands/uninstall.md) | Uninstalls packages from the workspace. |
| [`lip update`](commands/update.md) | Updates installed packages. |
| [`lip list`](commands/list.md) | Lists the installed packages. |
| [`lip run`](commands/run.md) | Runs the server of the workspace. |
| [`lip cache clean`](commands/cache_clean.md) | Deletes the cache. |
| [`lip config get`, `set`, `list`, `delete`](commands/config.md) | Reads and writes the configuration. |

Commands for the daemon:

| Command | Does |
| --- | --- |
| [`lip daemon status`](commands/daemon.md) | Shows whether the daemon runs. |
| [`lip daemon stop`](commands/daemon.md) | Stops the daemon. |

Commands for developing packages:

| Command | Does |
| --- | --- |
| [`lip pkg init`](commands/pkg_init.md) | Creates a `lip.toml` for a new package. |
| [`lip pkg pack`](commands/pkg_pack.md) | Packs a directory into a ZIP archive. |
| [`lip pkg view`](commands/pkg_view.md) | Shows the package file and the versions of a package, as JSON. |
| [`lip pkg migrate`](commands/pkg_migrate.md) | Migrates the `tooth.json` in the current directory to `lip.toml`. |

A command for lip itself:

| Command | Does |
| --- | --- |
| [`lip self-update`](commands/self_update.md) | Updates lip to its latest release. |

`cache`, `config`, `daemon` and `pkg` are **command groups**: the first word of a command name of two words.

## Arguments

Command names, group names and option names are case-sensitive.

- The first argument is a command name of one word or a command group. After a command group comes `-h`, `--help`, or the second word of a command of that group. Anything else is a usage error, and so is a command group with nothing after it.
- `--` is not a separator in lip: any argument that is `--` is a usage error.
- Options and operands may come in any order after the command name. Operands are assigned in the order given.

Each argument of a command that starts with `-` is an option:

| Form | Meaning |
| --- | --- |
| `--<name>` | The long option `<name>`. |
| `-<c>` | The short option `<c>`, one character. |
| `--<name>=<value>` | The long option `<name>` with the value `<value>`. |
| `-<c>=<value>` | The short option `<c>` with the value `<value>`. |

- An option that takes a value and is written without `=` takes the next argument as its value. That argument must not start with `-`.
- A value must not be empty.
- A **flag** is an option that takes no value. A flag given more than once counts once.
- An option that takes a value and is given more than once has the value of its last occurrence.
- Short options cannot be combined: what `-nh` means is not defined. Neither is a flag written with a value, such as `--json=true`. Do not use them.

Every other argument is an **operand**.

## Usage errors

The arguments of a command are a usage error when:

1. an option is not an option of the command;
2. an option that takes a value has none;
3. an operand or an option that the synopsis requires is absent; or
4. there are more operands than the command takes.

On a usage error, lip writes a message to standard error, exits with a non-zero code, and does nothing else. It writes no [failure report](files.md#failure-reports).

## Options of every command

| Option | Description |
| --- | --- |
| `-h`, `--help` | Writes the help of the command to standard output and exits with code 0. The command does nothing else. |

`-h` wins over a missing operand or an unknown option: `lip install -h` and `lip install -h --bogus` write the help. It does not win over an option value that is missing, or over too many operands; those stay usage errors.

## Working directory

The **workspace** of every command is the current working directory. A relative path in an argument is relative to it. There is no option that names another workspace: change the directory first.

## Output

- A **result** is the data a command is asked for. It goes to standard output. Only `list`, `pkg view`, `config get`, `config list`, `daemon status` and `self-update --check` have one.
- A **message** is everything else: progress, information, warnings, errors, the final success line. It goes to standard error. Its wording may change between versions; do not parse it.
- Help goes to standard output.

So `lip list > packages.txt` saves only the list, and `2> /dev/null` hides only the messages. Messages are always in English.

### JSON

These commands take `--json`. Standard output then receives only the result, as JSON, followed by a line break:

| Command | JSON result |
| --- | --- |
| [`lip list --json`](commands/list.md#output) | `{ "ExplicitInstalled": [...], "ImplicitInstalled": [...] }` |
| [`lip config get <KEY> --json`](commands/config.md#output) | The value as a JSON string, or `null`. |
| [`lip config list --json`](commands/config.md#output) | An object that maps each key to its value, or `null`. |
| [`lip daemon status --json`](commands/daemon.md#output) | `{ "running": false }`, or `{ "running": true, "version": "...", "pid": ... }`. |

[`lip pkg view`](commands/pkg_view.md) always writes JSON and has no `--json` option. Without `--json`, the layout of a result is for people and may change.

## Exit status

| Code | Meaning |
| --- | --- |
| `0` | The command succeeded. |
| not `0` | The command failed, a usage error included. A message on standard error says why. |

[`lip run`](commands/run.md#exit-status) is the exception: once it has started the server, it exits with the exit code of the server.

A command that fails for another reason than a usage error may write a **failure report**, a file below `.lip/logs/` in the workspace, and names it in a message. See [Failure reports](files.md#failure-reports).

## Package arguments

`install`, `update` and `pkg view` take **package arguments**. lip classifies each one by the first rule that applies:

1. **URL.** An argument that starts with `https://` is the URL of a ZIP archive. Any other scheme followed by `://`, such as `http://` or `file://`, is invalid.
2. **Local path.** Take the argument without the part from its last `#` on, or the whole argument when it names an existing file or directory. It is a local path when that part
   - starts with `./`, `../`, `.\` or `..\`;
   - is `.` or `..`;
   - is an absolute path; or
   - ends with `.zip` (in any case) or names an existing file or directory, and the argument contains no `@`.

   A local path that does not exist is invalid.
3. **Remote repository.** Anything else: `<repo>[#<variant>][@<ref>]`. A `<repo>` without `/` is a repository of LiteLDev: `LeviLamina@1.9.7` is `github.com/LiteLDev/LeviLamina@1.9.7`.

A URL and a local path may end in `#<variant>`. The ref of a remote repository is one of:

| Ref | Selects |
| --- | --- |
| none | The version tags of the repository (`v<version>`), highest first. |
| `@<version>` | The commit of the tag `v<version>`. |
| `@branch:<name>` | The commit the branch points to. |
| `@tag:<name>` | The commit the tag points to. |
| `@rev:<commit>` | The commit with that id: all 40 hexadecimal digits. |

A command with an invalid package argument fails before anything changes. [`lip install`](commands/install.md) has examples of each form.

## Environment

lip installs packages for one **environment**: the side `server` and the platform `win-x64`, whatever system lip runs on. `setup`, `install`, `uninstall`, `update` and `run` fail in a workspace whose [state file](../concepts/state_file.md) records another side, or a platform other than `win-x64` and `linux-x64`. A state file of `linux-x64` is [read as one of `win-x64`](../concepts/state_file.md#workspaces-of-linux-x64).

## Workspace operations

`setup`, `install`, `uninstall` and `update` without `--dry-run` each carry out one **workspace operation**: they change the workspace as a whole or not at all.

- An operation takes the workspace lock, `.lip/lock`. When another process holds it, the command fails at once and changes nothing.
- When an earlier operation was interrupted, the next one first completes or undoes it.
- A failure before the workspace starts to change changes nothing. A failure after that is undone. Hook commands that already ran are not reversed.

[How lip works](../concepts/how_lip_works.md) explains this in full.

## Dry run

`setup`, `install`, `uninstall` and `update` take `-n` or `--dry-run`. A dry run carries out every step that changes nothing, and writes as messages what the operation would do to which packages. It:

- does not take the workspace lock, so it works while another command changes the workspace;
- does not recover an interrupted operation: when `.lip/journal` exists, it says so and plans with the workspace as it is;
- runs no hook command;
- asks no question; and
- creates, replaces and deletes no file or directory, with one exception: when it fails, it may write a failure report.

A dry run still uses the network to resolve packages, and writes nothing to the cache.

## Ctrl+C

What Ctrl+C does depends on what lip is doing:

| lip is | The first Ctrl+C | A second Ctrl+C |
| --- | --- | --- |
| planning an operation, before the workspace changes | Ends lip at once. Nothing has changed. | |
| changing the workspace | Cancels the operation and undoes its changes. | Ends lip at once. The next operation on the workspace recovers it. |
| running the server with `lip run` | Sends `stop` to the server. lip exits when the server has. | Nothing more: lip never exits before the server. |
| attached to a managed server (`lip run --managed`) | Sends `stop` to the server. You stay attached until it exits. | The same. |

See [`lip run`](commands/run.md#stopping) for the other signals.

## The daemon

Every command except those of the group `daemon`, and the interface, are carried out by the **daemon**: a background `lip` process of your user that the first command starts. You see no difference: the command uses your working directory, your environment variables, your terminal and your Ctrl+C, and `lip` exits with the exit code of the command.

When lip cannot start or reach the daemon, it writes a message and carries out the command itself. Only `lip run --managed` needs the daemon. See [`lip daemon`](commands/daemon.md).

## See also

- [Interface reference](interface.md): what each page of the interface maps to in commands.
- [Configuration](configuration.md) and [Files](files.md).
- [A server from the command line](https://levimc.org/tutorials/server/command-line/) and [Managing packages](../guides/managing_packages.md).
