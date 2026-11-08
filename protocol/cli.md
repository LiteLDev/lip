# Command Line Interface

## 1. Terminology

- **argument**: One element of the argument list that `lip` is started with, the program name excluded.
- **command**: An invocation of `lip` with a command name, such as `lip install` or `lip config get`; not a hook command. The command names are `install`, `uninstall`, `update`, `list`, `versions`, `view`, `init`, `migrate`, `cache clean`, `config get`, `config set`, `config list`, `config delete` and `pack`.
- **command group**: `cache` or `config`, the first word of a command name of two words.
- **option**: An argument, or two consecutive arguments, that Section 2.1 reads as an option name with or without a value.
- **flag**: An option that takes no value.
- **operand**: An argument of a command that is not part of an option.
- **usage error**: A list of arguments that Section 2.1 or Section 2.2 rejects.
- **result**: The data a command is asked for; only `list`, `versions`, `view`, `config get` and `config list` have one.
- **message**: Any text other than a result or help (progress, information, warnings, errors, the final success line); its wording is not defined here.
- **help**: Text describing how to invoke `lip` or a command; its wording is not defined here.
- **workspace operation**: An operation (Section 1 of [WORKSPACE]) that `install`, `uninstall` or `update` carries out without `-n` or `--dry-run`.
- **RID lip runs on**: The runtime of the process running `lip`.

## 2. Invocation

### 2.1. Arguments

Command names, command group names and option names are compared case-sensitively.

The arguments are read from first to last:

1. When any argument is `--`, the list is a usage error.
2. When the first argument starts with `-`, the list is read as Section 2.2 defines.
3. Otherwise, the first argument MUST be a command name of one word or a command group. When it is a command group, the second argument MUST be `-h`, `--help`, or the second word of a command name of that group; it is a usage error otherwise, and also when the second argument is absent. The arguments after the command name are the arguments of the command.

Each argument of a command that starts with `-` is an option, in one of these forms:

| Form | Meaning |
|---|---|
| `--<name>` | The long option `<name>`. |
| `-<c>` | The short option `<c>`, a single character. |
| `--<name>=<value>` | The long option `<name>`, with the value `<value>`. |
| `-<c>=<value>` | The short option `<c>`, with the value `<value>`. |

An option that takes a value and is not written with `=` takes the next argument as its value; that argument MUST NOT start with `-`. A value MUST NOT be empty. The meaning of a flag written with `=<value>`, such as `--json=true`, and of any argument that starts with `-` and has none of these forms, such as `-nh`, is not defined here. Every argument of a command that does not start with `-` and is not the value of an option is an operand.

Options and operands MAY appear in any order after the command name. Operands are assigned to the operands of the command in the order given.

A flag given more than once has the same effect as given once. An option that takes a value and is given more than once has the value of its last occurrence.

The arguments of a command are a usage error when:

1. an option is not an option of the command;
2. an option that takes a value has none;
3. a REQUIRED operand or a REQUIRED option is absent; or
4. there are more operands than the command takes.

**Help.** When the arguments of a command contain `-h` or `--help`, and are not a usage error, or are a usage error only by rule 1 or rule 3, the command writes its help to standard output and exits with code 0; it does nothing else. Each command lists `-h` and `--help` among its options in Section 5.

### 2.2. `lip`

```
lip
lip -h | --help
lip -v | --version
lip cache -h | --help
lip config -h | --help
```

- `lip` without arguments writes the help for `lip` to standard output.
- When the first argument is `-h` or `--help`, `lip` writes the help for `lip` to standard output, whatever arguments follow.
- When the first argument is `-v` or `--version`, `lip` writes its version [SEMVER] followed by a line break to standard output, whatever arguments follow.
- When the first argument is another argument starting with `-`, the list is a usage error.
- `lip cache -h`, `lip cache --help`, `lip config -h` and `lip config --help` write the help for the command group to standard output.

Each of these exits with code 0, except the usage error.

### 2.3. Working Directory

The workspace of every command is the current working directory. A relative path in an argument is relative to the current working directory.

### 2.4. Output

- A result is written to standard output; a message is written to standard error.
- When a command that has a `--json` option is given it, standard output receives only the result, as JSON [RFC8259] of the shape the command defines, followed by a line break. When it is not given, the layout of the result is not defined here.
- The result of `view` is always written as JSON, as Section 5.6 defines.

### 2.5. Exit Status and Failure Reports

A command that succeeds exits with code 0. A command that fails, including on a usage error, writes a message describing the cause to standard error and exits with a non-zero code.

A command that fails other than on a usage error MAY write a **failure report**: a file below `.lip/logs/` of its workspace, whose content is not defined here. When it writes one, it names the file in a message.

## 3. Package Arguments

A **package argument** is an operand that names a source; Section 5 says which operands are package arguments. It is classified by the first of these rules that applies:

1. An argument starting with `https://` is the URL source of that string. An argument starting with another URI scheme followed by `://`, such as `http://` or `file://`, is invalid.
2. Let L be the argument without the part from its last `#` on; or the whole argument, when the argument names an existing file or directory. The argument is a local path when:
   - L starts with `./`, `../`, `.\` or `..\`; or
   - L is `.` or `..`; or
   - L is an absolute path; or
   - the argument contains no `@`, and L ends with `.zip` (compared ASCII case-insensitively) or names an existing file or directory.

   Its source is the `file:` URI [RFC8089] of the absolute path of L, followed by the rest of the argument. When L does not exist, the argument is invalid.
3. Any other argument is the remote repository source of that string.

An argument whose source is not valid (Section 2 of [PACKAGE]) is invalid. A command with an invalid package argument fails before anything changes.

A package argument is made into the request (source, range) whose range matches every version.

## 4. Environment and Workspace Operations

`lip` manages only workspaces whose platform is `"server"`. The environment of `install`, `uninstall` and `update` is:

- platform: `"server"`. When the workspace file records another platform, the command fails.
- runtime: the value of the `--rid` option of the command, when given; otherwise the runtime the workspace file records; otherwise the RID lip runs on.

A workspace without a recorded environment gets the environment above from its first workspace operation.

Each workspace operation is carried out as Section 7.4 of [WORKSPACE] defines.

When the user interrupts `lip` (Ctrl+C) while a workspace operation changes the workspace, the operation is cancelled and undone (Section 6 rule 2 of [WORKSPACE]); a second interruption ends `lip` at once, leaving the operation to be recovered (Section 6 rule 5 of [WORKSPACE]). An interruption before the workspace starts to change ends `lip` at once.

A workspace operation that finds the workspace lock held by another process fails at once, and changes nothing.

## 5. Commands

### 5.1. `lip install`

```
lip install <PACKAGES>... [-n | --dry-run] [--ignore-scripts] [--no-dependencies] [--rid <RID>] [-h | --help]
```

Operands:

| Operand | Presence | Definition |
|---|---|---|
| `<PACKAGES>...` | REQUIRED, one or more | Each is a package argument (Section 3). |

Options:

| Option | Value | Presence | Definition |
|---|---|---|---|
| `-n`, `--dry-run` | none | OPTIONAL | The command carries out every step that changes nothing, and writes as messages the packages that the operation would install, uninstall or mark explicitly installed. It does not take the workspace lock. When `.lip/journal` exists, it writes a message that an operation was interrupted, does not recover it, and plans with the workspace as it is. It creates, replaces or deletes no file or directory other than a failure report (Section 2.5), and runs no hook command. |
| `--ignore-scripts` | none | OPTIONAL | The operation runs no hook command. Nothing else changes. |
| `--no-dependencies` | none | OPTIONAL | Selects the behavior below. |
| `--rid` | `<RID>` | OPTIONAL | The runtime of the environment (Section 4) is `<RID>`. |
| `-h`, `--help` | none | OPTIONAL | Writes the help for `lip install` (Section 2.1). |

Without `--no-dependencies`, `lip install` installs a set of packages that satisfies Section 3 of [WORKSPACE] for the requests of the package arguments and the requests that made the installed explicitly installed packages:

- The requested packages, including those already installed as dependency packages, become explicitly installed.
- An installed package keeps its version when that satisfies Section 3 of [WORKSPACE]; otherwise a dependency package MAY change version. Each dependency package that no explicitly installed package needs is uninstalled.
- When a requested package is already explicitly installed, or two package arguments request the same identity, the command fails.

With `--no-dependencies`, `lip install` installs, for each package argument, exactly the first candidate of its request as an explicitly installed package, replacing an installed package of the same identity. It follows no dependency, and installs or uninstalls no other package. An installed package with the identity, version and commit of that candidate is only marked explicitly installed. Section 3 of [WORKSPACE] may no longer hold afterwards.

`lip install` has no result.

### 5.2. `lip uninstall`

```
lip uninstall <PACKAGES>... [-n | --dry-run] [--ignore-scripts] [--no-dependencies] [--rid <RID>] [-h | --help]
```

Operands:

| Operand | Presence | Definition |
|---|---|---|
| `<PACKAGES>...` | REQUIRED, one or more | Each names installed packages, as defined below. They are not package arguments. |

Options:

| Option | Value | Presence | Definition |
|---|---|---|---|
| `-n`, `--dry-run` | none | OPTIONAL | The command carries out every step that changes nothing, and writes as messages the packages that the operation would uninstall or mark no longer explicitly installed. It does not take the workspace lock. When `.lip/journal` exists, it writes a message that an operation was interrupted, does not recover it, and plans with the workspace as it is. It creates, replaces or deletes no file or directory other than a failure report (Section 2.5), and runs no hook command. |
| `--ignore-scripts` | none | OPTIONAL | The operation runs no hook command. Nothing else changes. |
| `--no-dependencies` | none | OPTIONAL | Selects the behavior below. |
| `--rid` | `<RID>` | OPTIONAL | The runtime of the environment (Section 4) is `<RID>`. |
| `-h`, `--help` | none | OPTIONAL | Writes the help for `lip uninstall` (Section 2.1). |

Each operand names the installed packages that match it by the first of these rules that matches any installed package:

1. The identity of the package equals the operand.
2. The source of the package equals the operand made into a source as in Section 3, with the locations compared ASCII case-insensitively. An operand without a variant matches every variant, and an operand without a ref matches every ref.

When an operand names no installed package, the command fails.

Without `--no-dependencies`:

- Every named package MUST be explicitly installed; otherwise the command fails.
- The named packages stop being explicitly installed. Each package that no remaining explicitly installed package needs is uninstalled; the others stay installed.

With `--no-dependencies`, exactly the named packages are uninstalled, dependency packages included, and no other package is. Section 3 of [WORKSPACE] may no longer hold afterwards.

`lip uninstall` has no result.

### 5.3. `lip update`

```
lip update [<PACKAGES>...] [-n | --dry-run] [--ignore-scripts] [--rid <RID>] [-h | --help]
```

Operands:

| Operand | Presence | Definition |
|---|---|---|
| `<PACKAGES>...` | OPTIONAL, zero or more | Each is a package argument (Section 3). |

Options:

| Option | Value | Presence | Definition |
|---|---|---|---|
| `-n`, `--dry-run` | none | OPTIONAL | The command carries out every step that changes nothing, and writes as messages the packages that the operation would install, uninstall or mark explicitly installed. It does not take the workspace lock. When `.lip/journal` exists, it writes a message that an operation was interrupted, does not recover it, and plans with the workspace as it is. It creates, replaces or deletes no file or directory other than a failure report (Section 2.5), and runs no hook command. |
| `--ignore-scripts` | none | OPTIONAL | The operation runs no hook command. Nothing else changes. |
| `--rid` | `<RID>` | OPTIONAL | The runtime of the environment (Section 4) is `<RID>`. |
| `-h`, `--help` | none | OPTIONAL | Writes the help for `lip update` (Section 2.1). |

With operands, `lip update` makes the request of each package argument again, as "Updating" in Section 5 of [WORKSPACE] defines for its source. Every requested package MUST already be installed; otherwise the command fails. The requested packages become explicitly installed.

Without operands, `lip update` updates every explicitly installed package as "Updating" in Section 5 of [WORKSPACE] defines, except that a package whose source is a local path that no longer exists is kept as it is.

A package is replaced only as "Updating" in Section 5 of [WORKSPACE] defines. Dependency packages are updated only as far as Section 3 of [WORKSPACE] requires. Updating a package installed from a source with a ref, by giving that source without the ref, moves the package back to the version tags.

`lip update` has no result.

### 5.4. `lip list`

```
lip list [--json] [-h | --help]
```

Operands: none.

Options:

| Option | Value | Presence | Definition |
|---|---|---|---|
| `--json` | none | OPTIONAL | The result is written as JSON (Section 2.4), of the shape below. |
| `-h`, `--help` | none | OPTIONAL | Writes the help for `lip list` (Section 2.1). |

The result is the packages that the workspace file records, each as `<identity>@<version>`, in installation order, in two groups: the explicitly installed packages, then the dependency packages. `lip list` changes nothing.

With `--json`, the result is a JSON object with exactly these members:

```json
{ "ExplicitInstalled": ["<identity>@<version>", ...], "ImplicitInstalled": ["<identity>@<version>", ...] }
```

### 5.5. `lip versions`

```
lip versions <PACKAGE> [--json] [-h | --help]
```

Operands:

| Operand | Presence | Definition |
|---|---|---|
| `<PACKAGE>` | REQUIRED, exactly one | A package argument (Section 3) that MUST be a remote repository without a ref; otherwise the command fails. Its variant does not affect the result. |

Options:

| Option | Value | Presence | Definition |
|---|---|---|---|
| `--json` | none | OPTIONAL | The result is written as JSON (Section 2.4), as a JSON array of strings. |
| `-h`, `--help` | none | OPTIONAL | Writes the help for `lip versions` (Section 2.1). |

The result is the versions of the version tags of the repository, in ascending precedence [SEMVER], each once. `lip versions` changes nothing.

### 5.6. `lip view`

```
lip view <PACKAGE> [-h | --help]
```

Operands:

| Operand | Presence | Definition |
|---|---|---|
| `<PACKAGE>` | REQUIRED, exactly one | A package argument (Section 3). |

Options:

| Option | Value | Presence | Definition |
|---|---|---|---|
| `-h`, `--help` | none | OPTIONAL | Writes the help for `lip view` (Section 2.1). |

The result is the package file of the first archive that the source of `<PACKAGE>` names (Section 4 step 1 of [PACKAGE]), as it is read for the environment of Section 4, written as JSON followed by a line break. `lip view` has no `--json` option, and changes nothing.

### 5.7. `lip init`

```
lip init [-h | --help]
```

Operands: none.

Options:

| Option | Value | Presence | Definition |
|---|---|---|---|
| `-h`, `--help` | none | OPTIONAL | Writes the help for `lip init` (Section 2.1). |

`lip init` writes `lip.yaml` in the current working directory: a package file with exactly the members of `{ "format": 4, "id": "<id>", "version": "0.1.0", "variants": { "": [ { "platform": "server", "runtime": "win-x64" } ] } }`. `lip` asks the user on the terminal for `<id>` until the answer is a Go module path [GO-MODULE]. When standard input or standard error is not a terminal, the command fails instead.

When `lip.yaml` exists, the command fails and writes nothing.

`lip init` has no result.

### 5.8. `lip migrate`

```
lip migrate [-h | --help]
```

Operands: none.

Options:

| Option | Value | Presence | Definition |
|---|---|---|---|
| `-h`, `--help` | none | OPTIONAL | Writes the help for `lip migrate` (Section 2.1). |

`lip migrate` migrates the current working directory as Section 5 of [COMPATIBILITY] defines.

`lip migrate` has no result.

### 5.9. `lip cache clean`

```
lip cache clean [-h | --help]
```

Operands: none.

Options:

| Option | Value | Presence | Definition |
|---|---|---|---|
| `-h`, `--help` | none | OPTIONAL | Writes the help for `lip cache clean` (Section 2.1). |

`lip cache clean` deletes the cache directory (Section 1 of [USERSPACE]). Workspaces and the configuration are not affected.

`lip cache clean` has no result.

### 5.10. `lip config`

The commands of the command group `config` read and write the configuration (Section 2 of [USERSPACE]).

#### 5.10.1. `lip config get`

```
lip config get <KEY> [--json] [-h | --help]
```

Operands:

| Operand | Presence | Definition |
|---|---|---|
| `<KEY>` | REQUIRED, exactly one | A configuration key. When it is not one, the command fails. |

Options:

| Option | Value | Presence | Definition |
|---|---|---|---|
| `--json` | none | OPTIONAL | The result is written as JSON (Section 2.4), as a JSON string. |
| `-h`, `--help` | none | OPTIONAL | Writes the help for `lip config get` (Section 2.1). |

The result is the value of `<KEY>`. `lip config get` changes nothing.

#### 5.10.2. `lip config set`

```
lip config set <KEY> <VALUE> [-h | --help]
```

Operands:

| Operand | Presence | Definition |
|---|---|---|
| `<KEY>` | REQUIRED, exactly one | A configuration key. When it is not one, the command fails. |
| `<VALUE>` | REQUIRED, exactly one | The value that `<KEY>` is set to. |

Options:

| Option | Value | Presence | Definition |
|---|---|---|---|
| `-h`, `--help` | none | OPTIONAL | Writes the help for `lip config set` (Section 2.1). |

`lip config set` sets `<KEY>` to `<VALUE>`, rewriting the configuration as Section 2 of [USERSPACE] defines.

`lip config set` has no result.

#### 5.10.3. `lip config list`

```
lip config list [--json] [-h | --help]
```

Operands: none.

Options:

| Option | Value | Presence | Definition |
|---|---|---|---|
| `--json` | none | OPTIONAL | The result is written as JSON (Section 2.4), as a JSON object that maps each configuration key to its value as a string. |
| `-h`, `--help` | none | OPTIONAL | Writes the help for `lip config list` (Section 2.1). |

The result is every configuration key with its value. `lip config list` changes nothing.

#### 5.10.4. `lip config delete`

```
lip config delete <KEY> [-h | --help]
```

Operands:

| Operand | Presence | Definition |
|---|---|---|
| `<KEY>` | REQUIRED, exactly one | A configuration key. When it is not one, the command fails. |

Options:

| Option | Value | Presence | Definition |
|---|---|---|---|
| `-h`, `--help` | none | OPTIONAL | Writes the help for `lip config delete` (Section 2.1). |

`lip config delete` resets `<KEY>` to its default value, rewriting the configuration as Section 2 of [USERSPACE] defines.

`lip config delete` has no result.

### 5.11. `lip pack`

```
lip pack [<DIR>] (-o | --output) <FILE> [-h | --help]
```

Operands:

| Operand | Presence | Definition |
|---|---|---|
| `<DIR>` | OPTIONAL, at most one | The directory to pack. Default: the current working directory. When it is not an existing directory, the command fails and writes nothing. |

Options:

| Option | Value | Presence | Definition |
|---|---|---|---|
| `-o`, `--output` | `<FILE>` | REQUIRED | The path of the ZIP archive to write. |
| `-h`, `--help` | none | OPTIONAL | Writes the help for `lip pack` (Section 2.1). |

`lip pack` writes to `<FILE>`, replacing it, a ZIP archive holding every regular file below `<DIR>` at its path relative to `<DIR>`, except `<FILE>` itself and, at the root of `<DIR>`, everything below `.git/` and `.lip/`. Such an archive can be an asset of type `"mcmod"`.

`lip pack` has no result.
