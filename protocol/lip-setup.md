# lip-setup(1)

## NAME

lip-setup - choose the game and the loader of the workspace

## SYNOPSIS

```
lip setup --game <VERSION> [--build <BUILD>] [--loader <LOADER> | --no-loader] [--accept-eula] [--accept-rosetta-license] [-n | --dry-run] [-h | --help]
```

## DESCRIPTION

`lip setup` makes the server of the workspace the game `<VERSION>` of the build `<BUILD>`, with the loader `<LOADER>` or without a loader, and keeps the other packages of the workspace that work with them.

### Terms

- **server source**: `github.com/LiteLDev/bds`, the source of the packages of the server ([lip-package(7), "The server"](lip-package.md#the-server)).
- **loader source**: `github.com/LiteLDev/LeviLamina`.
- **game**: The installed package ([lip-workspace(7)]) of the server source, whatever its variant, explicitly installed or not. A workspace has at most one ([lip-workspace(7), "Valid states"](lip-workspace.md#valid-states), rule 3); a workspace without one **has no game**. The **game version** is its version.
- **build**: Of the game, `linux` when its variant is `"linux"`, and `windows` otherwise. Of a setup, the build it is given.
- **loader**: The installed package whose package id is the loader source, explicitly installed or not; a workspace without one **has no loader**.
- **fits**: A package of the loader source fits a game version G when its selected entry ([lip-package(7), "Resolution"](lip-package.md#resolution)) has a dependency whose key is the server source, compared ASCII case-insensitively, and whose range G satisfies.

### Setting up

A **setup** is a workspace operation ([lip(1), "Workspace operations"](lip.md#workspace-operations)) for a game version G, a build B, and a loader, which is either no loader or a version V of the loader source. Its requests are:

- the **game request**: the source `github.com/LiteLDev/bds@G` when B is `windows`, and `github.com/LiteLDev/bds#linux@G` when B is `linux`, with a range that matches every version;
- the **loader request**, when the setup has a loader: the source `github.com/LiteLDev/LeviLamina@V`, with a range that matches every version;
- the request that made each **kept package**: each explicitly installed package other than the game and the loader, by the source it records;
- the requests of the package arguments of `lip install` ([lip-install(1)]), when it carries out the setup.

The setup fails before anything changes when B is `linux` and the setup has a loader, when Resolve(game request) has no candidate, and when the setup has a loader and the first candidate of Resolve(loader request) does not exist or does not fit G.

The setup installs a set of packages that satisfies [lip-workspace(7), "Valid states"](lip-workspace.md#valid-states) for its requests, holds no package of the loader source when the setup has no loader, and in which the packages that satisfy the game request, the loader request and the requests of the package arguments are explicitly installed:

- An installed package keeps its version when that satisfies these; otherwise it may change version.
- The game and the loader that the workspace had are not explicitly installed any more, unless they satisfy the game request or the loader request. Each package that no explicitly installed package needs is uninstalled.
- The package that satisfies the game request and the one that satisfies the loader request record the source of that request ([state.json(5)]).

A kept package P is **left out** when no set satisfies these for the game request, the loader request and the request of P alone: P is then not requested, and stops being explicitly installed. When a set satisfies the requests without the left out packages, lip names the left out packages, and asks the user on the terminal whether to uninstall them; when the user does not answer yes, the command fails before anything changes. When standard input or standard error is not a terminal, the command fails instead, naming them. When no set satisfies the requests, the command fails.

When the workspace already has the game and the loader of the setup, of the same identities and versions, the setup installs and uninstalls no package for them.

### The loader

Without `--loader` and `--no-loader`, the loader is `levilamina` when the build is `windows`, and no loader when the build is `linux`.

`<LOADER>` is `levilamina` or `levilamina@<VERSION>`, compared ASCII case-insensitively before the `@`. `levilamina@V` gives the loader version V. `levilamina` gives the version of the first candidate of Resolve(`github.com/LiteLDev/LeviLamina`, any version, environment) that fits G. The candidates are tried from the first; the search may stop at a candidate whose dependency on the server source is satisfied by a version that the server source names lower than G and by none at least as high as G. When the search finds none, the command fails before anything changes.

### The license

The **license** of the game is the Minecraft End User License Agreement, `https://www.minecraft.net/eula`, and the Microsoft Privacy Policy, `https://go.microsoft.com/fwlink/?LinkId=521839`.

When the setup installs a package of the server source, `lip` writes these two URLs in a message, and asks the user on the terminal whether they accept the license; when the user does not answer yes, the command fails before anything changes. With `--accept-eula`, `lip` does not ask. Without it, when standard input or standard error is not a terminal, the command fails before anything changes. The answer is not recorded; every setup that installs a package of the server source asks again.

### Order

`lip` asks the questions of "Setting up" and "The license", in this order, after it decides what the setup does ([lip-workspace(7), "Carrying out an operation"](lip-workspace.md#carrying-out-an-operation), step 3), and before it obtains the asset of the package of the server source that the setup installs.

When the build is `windows`, the setup prepares the system ([lip-workspace(7), "System dependencies"](lip-workspace.md#system-dependencies)) at step 5 of lip-workspace(7), "Carrying out an operation", even when it runs no hook command.

## OPTIONS

- `--game <VERSION>`: The game version G, a version ([lip-package(7), "Terms"](lip-package.md#terms)). Any other value is a usage error.
- `--build <BUILD>`: The build B, `windows` or `linux`; without it, `windows`. Any other value is a usage error.
- `--loader <LOADER>`: The loader, as "The loader" defines. A value that is not `levilamina`, or `levilamina@` followed by a version, is a usage error.
- `--no-loader`: The setup has no loader. Giving both `--loader` and `--no-loader` is a usage error.
- `--accept-eula`: The user accepts the license ("The license").
- `--accept-rosetta-license`: The user accepts the license of Rosetta 2, so that preparing the system installs Rosetta 2 without asking ([lip-workspace(7), "System dependencies"](lip-workspace.md#system-dependencies)).
- `-n`, `--dry-run`: A dry run ([lip(1), "Dry run"](lip.md#dry-run)). It asks no question. The messages name the packages that the operation would install, uninstall or mark explicitly installed, the left out packages, and whether `lip` would ask the user to accept the license.

## SEE ALSO

[lip(1)](lip.md), [lip-install(1)], [lip-run(1)](lip-run.md), [lip-uninstall(1)](lip-uninstall.md), [lip-package(7)](lip-package.md), [lip-workspace(7)]

[lip-install(1)]: lip-install.md
[lip-workspace(7)]: lip-workspace.md
[state.json(5)]: state.json.md
