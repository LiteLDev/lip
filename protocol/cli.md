# Command Line Interface

## 1. Terminology

- **command**: An invocation of `lip` with a command name, such as `lip install`; not a hook command.
- **usage error**: An unknown command or option, or a missing required argument.
- **result**: The data a command is asked for; only `list`, `versions`, `view`, `config get` and `config list` have one.
- **message**: Any text other than a result (progress, information, warnings, errors, the final success line); its wording is not defined here.
- **workspace operation**: `install`, `uninstall` or `update` without `--dry-run`; it is an operation.
- **RID lip runs on**: The runtime of the process running `lip`.

## 2. Invocation

```
lip [-h | --help] [-v | --version]
lip <command> [arguments] [options]
```

These exit with code 0:

- `-h`, `--help` writes help (a message) for `lip`, or for the command it follows, to standard output.
- `-v`, `--version` writes the version of `lip` [SEMVER] and a line break to standard output.
- `lip` without arguments writes help.

`lip cache` and `lip config` without a subcommand are usage errors.

Every command's workspace is the current working directory, to which relative paths in arguments are relative.

- A result is written to standard output, a message to standard error.
- With `--json`, standard output receives only the result, as JSON [RFC8259] of the shape the command defines, and a line break. Without it, a result's layout is not defined.
- The result of `view` is always written as with `--json`.

A command that succeeds exits with code 0; one that fails, including on a usage error, writes a message describing the cause and exits with a non-zero code.

## 3. Package Arguments

A **package argument** is an argument of `install`, `update`, `uninstall`, `versions` or `view` that names a source. It is classified by the first of these rules that applies:

1. An argument starting with `https://` is the URL source of that string; one starting with another URI scheme and `://`, such as `http://` or `file://`, is invalid.
2. Let L be the argument without the part from its last `#` on; or the whole argument, when the argument names an existing file or directory. The argument is a local path when:
   - L starts with `./`, `../`, `.\` or `..\`; or
   - L is `.` or `..`; or
   - L is an absolute path; or
   - the argument contains no `@`, and L ends with `.zip` (ASCII case-insensitively) or names an existing file or directory.

   The source is the `file:` URI [RFC8089] of the absolute path of L, followed by the rest of the argument. When L does not exist, the argument is invalid.
3. Any other argument is the remote repository source of that string.

An argument whose source is not valid is invalid. A command with an invalid package argument fails before anything changes.

A package argument is made into the request (source, range) with a range matching every version.

## 4. Environment and Workspace Operations

`lip` manages only workspaces whose platform is `"server"`. The environment of `install`, `uninstall` and `update` is:

- platform: `"server"`. When the workspace file records another platform, the command fails.
- runtime: the value of `--rid` when given; otherwise the runtime the workspace file records; otherwise the RID lip runs on.

A workspace without a recorded environment gets the one above from its first workspace operation.

Each workspace operation is carried out as Section 7.4 of [WORKSPACE] defines.

The **common options** of `install`, `uninstall` and `update` are:

| Option | Behavior |
|---|---|
| `-n`, `--dry-run` | Performs every step that changes nothing and reports, as messages, the packages the operation would install, uninstall or mark explicitly installed. Takes no workspace lock; reports, but does not recover, an interrupted operation, planning with the workspace as it is; writes nothing and runs no hook command. |
| `--ignore-scripts` | Runs no hook command; nothing else changes. |
| `--rid <RID>` | Sets the runtime of the environment to `<RID>`. |

When the user interrupts `lip` (Ctrl+C) while a workspace operation changes the workspace, the operation is cancelled and undone (Section 6 rule 2 of [WORKSPACE]); a second interruption ends `lip` at once, leaving the operation to be recovered (Section 6 rule 5 of [WORKSPACE]). An interruption before the workspace starts to change ends `lip` at once.

A workspace operation that finds the workspace lock held by another process fails at once, changing nothing.

## 5. Commands

### 5.1. `lip install`

```
lip install <PACKAGES>... [common options] [--no-dependencies]
```

Each of `<PACKAGES>` is a package argument.

Without `--no-dependencies`, `lip install` installs a set of packages satisfying Section 3 of [WORKSPACE] for the requests of the package arguments and the requests that made the installed explicitly installed packages:

- The requested packages, including those already installed as dependency packages, become explicitly installed.
- An installed package keeps its version when that satisfies Section 3 of [WORKSPACE]; otherwise a dependency package may change version. Each dependency package that no explicitly installed package needs is uninstalled.
- When a requested package is already explicitly installed, or two package arguments request the same identity, the command fails.

With `--no-dependencies`, `lip` installs, for each package argument, exactly the first candidate of its request as explicitly installed, replacing an installed package of the same identity, following no dependency, and installing or uninstalling no other package. An installed package with the identity, version and commit of that candidate is only marked explicitly installed. Section 3 of [WORKSPACE] may no longer hold afterwards.

### 5.2. `lip uninstall`

```
lip uninstall <PACKAGES>... [common options] [--no-dependencies]
```

Each argument names the installed packages that match it by the first of these rules that matches any installed package:

1. Its identity equals the argument.
2. Its source equals the argument made into a source as in Section 3, with the locations compared ASCII case-insensitively. An argument without a variant matches every variant, and an argument without a ref matches every ref.

When an argument names no installed package, the command fails.

Without `--no-dependencies`:

- Every named package MUST be explicitly installed; otherwise the command fails.
- The named packages stop being explicitly installed. Each package that no remaining explicitly installed package needs is uninstalled; the others stay installed.

With `--no-dependencies`, exactly the named packages are uninstalled, dependency packages included, and no other package is. Section 3 of [WORKSPACE] may no longer hold afterwards.

### 5.3. `lip update`

```
lip update [PACKAGES]... [common options]
```

Each of `[PACKAGES]` is a package argument.

With arguments, `lip update` makes a request of each package argument again, as "Updating" in Section 5 of [WORKSPACE] defines for the source. Every requested package MUST already be installed; otherwise the command fails. The requested packages become explicitly installed.

Without arguments, `lip update` updates every explicitly installed package as "Updating" in Section 5 of [WORKSPACE] defines, except that a package whose source is a local path that no longer exists is kept as it is.

A package is replaced only as "Updating" in Section 5 of [WORKSPACE] defines. Dependency packages are updated as far as Section 3 of [WORKSPACE] requires. Updating a package installed from a source with a ref, giving that source without the ref, moves the package back to the version tags.

### 5.4. `lip list`

```
lip list [--json]
```

Writes as its result the packages the workspace file records, each as `<identity>@<version>`, in installation order, in two groups: explicitly installed packages, then dependency packages. It changes nothing.

With `--json`, the result is:

```json
{ "ExplicitInstalled": ["<identity>@<version>", ...], "ImplicitInstalled": ["<identity>@<version>", ...] }
```

### 5.5. `lip versions`

```
lip versions <PACKAGE> [--json]
```

`<PACKAGE>` MUST be a package argument that is a remote repository without a ref; otherwise the command fails. Its variant does not affect the result. Writes as its result the versions of the version tags of the repository, in ascending precedence [SEMVER], each once.

With `--json`, the result is a JSON array of strings.

### 5.6. `lip view`

```
lip view <PACKAGE>
```

`<PACKAGE>` is a package argument. Writes as its result, as JSON, the package file of the first archive that its source names (Section 4 step 1 of [PACKAGE]), as it is read for the environment of Section 4. It changes nothing.

### 5.7. `lip init`

```
lip init
```

Writes `lip.yaml` in the current directory: a package file with the members of `{ "format": 4, "id": "<id>", "version": "0.1.0", "variants": { "": [ { "platform": "server", "runtime": "win-x64" } ] } }`. `lip` asks the user on the terminal for `<id>` until the answer is a Go module path [GO-MODULE]. When standard input or standard error is not a terminal, the command fails instead.

When `lip.yaml` exists, the command fails and writes nothing.

### 5.8. `lip migrate`

```
lip migrate
```

Migrates the current directory as Section 5 of [COMPATIBILITY] defines.

### 5.9. `lip cache clean`

```
lip cache clean
```

Deletes the cache directory. Workspaces and the configuration are not affected.

### 5.10. `lip config`

```
lip config get <KEY> [--json]
lip config set <KEY> <VALUE>
lip config list [--json]
lip config delete <KEY>
```

- `get` writes as its result the value of `<KEY>`. With `--json`, the result is the value as a JSON string.
- `set` sets `<KEY>` to `<VALUE>`.
- `list` writes as its result every configuration key with its value. With `--json`, the result is a JSON object that maps each configuration key to its value as a string.
- `delete` resets `<KEY>` to its default value.

When `<KEY>` is not a configuration key, the command fails. `set` and `delete` rewrite the configuration as Section 2 of [USERSPACE] defines.

### 5.11. `lip pack`

```
lip pack [<DIR>] -o, --output <FILE>
```

Writes to `<FILE>`, replacing it, a ZIP archive holding every regular file below `<DIR>` (default: the current directory) at its path relative to `<DIR>`, except `<FILE>` itself and, at the root of `<DIR>`, everything below `.git/` and `.lip/`. Such an archive can be an asset of type `"mcmod"`.
