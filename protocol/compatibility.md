# Compatibility

## 1. Terminology

- **lip v0.37**: Release `v0.37.0` of `lip` [LIP-V0.37], the last to install tooth.json packages.
- **tooth.json**: The file `tooth.json` at the root of an archive.
- **format 3 package**: An archive whose tooth.json is valid against the schema of [TOOTH-V3], so its `format_version` is `3` and its `format_uuid` is `"289f771f-2c9a-4d73-9f3f-8492495a924d"`.
- **tooth path**: A string, such as `github.com/LiteLDev/LeviLamina`, that names a git repository as a remote repository (Section 2.1 of [PACKAGE]) does. A **version of a tooth path** is a version V for which the repository has the tag `vV`.

Here, a variant is an element of `variants` of a tooth.json, and a range is as in [TOOTH-V3].

## 2. Format 3 Packages

`lip` MUST support every format 3 package directly:

1. **As published.** A format 3 package MUST be resolved, installed, uninstalled and updated as it is, from where it is published (a remote repository at a version tag, a URL, a local file or a local directory). `lip` MUST NOT require converting it first, by `lip migrate` (Section 5.8 of [CLI]) or otherwise, nor require its author to publish anything lip v0.37 did not require.
2. **Same meaning.** A format 3 package means what it means to lip v0.37 [TOOTH-V3]: its `variants` and their `label`, `platform`, `dependencies`, `assets`, `placements`, `preserve_files`, `remove_files` and `scripts`, and templates such as `{{version}}` in its strings. Installing, uninstalling and updating it MUST affect the workspace as lip v0.37 does.
3. **Dependencies.** Its dependencies are followed as lip v0.37 follows them, including one on another variant of the same tooth path (`<tooth path>#<label>`). A dependency naming a package of [PACKAGE] is resolved per Section 3 of [PACKAGE].
4. **Workspace.** It takes part in operations like a package of [PACKAGE]: Sections 5 to 7 of [WORKSPACE] apply to it. It is recorded in the workspace file with a `kind` other than `"mcmod"` (Section 7.1 of [WORKSPACE]); its entry is not defined here.

Format 3 packages are read, never written: `lip` creates no tooth.json, and `lip migrate` remains the only command that reads a tooth.json to write a manifest.

Supporting format 3 packages does not require supporting any other tooth.json format.

## 3. Required Package Versions

`lip` MUST install, uninstall and update every package version listed in this section, with every variant it has, in every environment that variant applies to, as Section 2 requires. Each is the git archive of the tag `v<version>` of the repository its tooth path names.

### 3.1. Published Versions

These are the versions tagged from 2026-10-05 to 2026-11-05 inclusive by the LiteLDev projects named on levimc.org [LEVIMC] whose repository has a tooth.json. The other projects named there (LeviLauncher, LeviLaunchroid, LeviStone, lip) are not packages: their repositories have none.

| Tooth path | Versions (tag date) |
|---|---|
| `github.com/LiteLDev/LeviLamina` | 26.51.7 (2026-10-12), 26.51.8 (2026-10-24), 26.51.9 (2026-10-27), 26.60.0 (2026-11-03), 26.60.1 (2026-11-05) |
| `github.com/LiteLDev/LegacyScriptEngine` | 0.22.3 (2026-10-11), 0.22.4 (2026-10-31), 0.23.0 (2026-11-03), 0.23.1 (2026-11-04) |
| `github.com/LiteLDev/LeviAntiCheat` | 0.17.3 (2026-10-06), 0.17.4 (2026-10-07), 0.17.5 (2026-10-21), 0.17.6 (2026-10-22), 0.17.7 (2026-11-03), 0.18.0 (2026-11-03) |
| `github.com/LiteLDev/MoreDimensions` | 0.15.0 (2026-10-12) |
| `github.com/LiteLDev/LeviOptimize` | none |

All of them are format 3 packages.

### 3.2. Dependencies

The **dependency versions** of a version of Section 3.1 are the package versions a new installation of it, with one of its variants, installs into a workspace with no installed packages: for each tooth path and label that its `dependencies`, in any asset, name directly or transitively, the highest version satisfying every range on it, with `{{version}}` in a range replaced by the depending package's version.

Determined on 2026-11-05 over every variant of every version of Section 3.1, the dependency versions not in Section 3.1 are:

| Tooth path | Versions |
|---|---|
| `github.com/LiteLDev/7-zip-tooth` | 26.0.1 |
| `github.com/LiteLDev/bds` | 1.26.51, 1.26.60 |
| `github.com/LiteLDev/bdsdown` | 1.2.1 |
| `github.com/LiteLDev/bedrock-runtime-data` | 26.51.1-client.11, 26.51.1-client.12, 26.51.1-server.11, 26.51.1-server.12, 26.60.5-client.5, 26.60.5-client.7, 26.60.6-server.5, 26.60.6-server.7 |
| `github.com/LiteLDev/CrashLogger` | 1.3.3, 1.4.0 |
| `github.com/LiteLDev/iListenAttentively` | 0.17.2, 0.18.0 |
| `github.com/LiteLDev/LegacyMoney` | 0.22.0, 0.23.0 |
| `github.com/LiteLDev/LegacyRemoteCall` | 0.22.0, 0.23.0 |
| `github.com/LiteLDev/levilamina-loc` | 1.6.4, 1.7.0 |
| `github.com/LiteLDev/PeEditor` | 3.9.2 |
| `github.com/LiteLDev/PreLoader` | 1.16.5, 1.16.7, 1.16.8 |

All of them are format 3 packages. This section does not require versions published after 2026-11-05.

## 4. Security Considerations

A format 3 package runs its `scripts` when installed and uninstalled, and downloads its assets from the URLs it names without checking them against a recorded digest. Installing one therefore trusts its repository and every host its assets are downloaded from.
