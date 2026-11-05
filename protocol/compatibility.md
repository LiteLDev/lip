# lip Compatibility Requirements

## Abstract

This document defines which packages written for lip v0.x `lip` MUST keep supporting: every package in tooth.json format 3, and a fixed set of package versions published by LiteLDev together with the package versions a new installation of them installs.

## Status of This Memo

This document is part of the lip protocol and is normative.

## Table of Contents

1. [Introduction](#1-introduction)
   1. [Requirements Language](#11-requirements-language)
2. [Terminology](#2-terminology)
3. [Format 3 Packages](#3-format-3-packages)
4. [Required Package Versions](#4-required-package-versions)
   1. [Selection](#41-selection)
   2. [Published Versions](#42-published-versions)
   3. [Dependencies](#43-dependencies)
5. [Security Considerations](#5-security-considerations)
6. [IANA Considerations](#6-iana-considerations)
7. [References](#7-references)
   1. [Normative References](#71-normative-references)
   2. [Informative References](#72-informative-references)

## 1. Introduction

Packages published for lip v0.x describe themselves with a `tooth.json` instead of the manifest of [PACKAGE]. Many of them are still published that way, and authors are not expected to republish them. This document requires `lip` to install them as they are (Section 3), and names the package versions that MUST work (Section 4).

### 1.1. Requirements Language

The key words "MUST", "MUST NOT", "REQUIRED", "SHALL", "SHALL NOT", "SHOULD", "SHOULD NOT", "RECOMMENDED", "NOT RECOMMENDED", "MAY", and "OPTIONAL" in this document are to be interpreted as described in BCP 14 [RFC2119] [RFC8174] when, and only when, they appear in all capitals, as shown here.

## 2. Terminology

Terms not defined in this document are defined in [PACKAGE], [WORKSPACE] and [CLI].

- **lip v0.37**: The release `v0.37.0` of `lip` [LIP-V0.37], the last release that installs tooth.json packages.
- **tooth.json**: The file `tooth.json` at the root of an archive.
- **format 3 package**: An archive whose tooth.json is valid against the tooth.json schema of lip v0.37 [TOOTH-V3], so that its `format_version` is `3` and its `format_uuid` is `"289f771f-2c9a-4d73-9f3f-8492495a924d"`.
- **tooth path**: A string such as `github.com/LiteLDev/LeviLamina` that names a git repository as a remote repository (Section 3.1 of [PACKAGE]) does. A **version of a tooth path** is a version V for which the repository has the tag `vV`.

## 3. Format 3 Packages

`lip` MUST support every format 3 package directly:

1. **As published.** A format 3 package MUST be resolved, installed, uninstalled and updated from where it is published (a remote repository at a version tag, a URL, a local file or a local directory) as it is. `lip` MUST NOT require the package to be converted first, by `lip migrate` (Section 6.8 of [CLI]) or otherwise, and MUST NOT require its author to publish anything that lip v0.37 did not require.
2. **Same meaning.** A format 3 package means what it means to lip v0.37 [TOOTH-V3]: its `variants` and their `label`, `platform`, `dependencies`, `assets`, `placements`, `preserve_files`, `remove_files` and `scripts`, and the templates such as `{{version}}` in its strings. Installing, uninstalling and updating it MUST have the effects on the workspace that lip v0.37 has.
3. **Dependencies.** The dependencies of a format 3 package are followed as lip v0.37 follows them, including a dependency on another variant of the same tooth path (`<tooth path>#<label>`). A dependency that names a package of [PACKAGE] is resolved as Section 5 of [PACKAGE] defines.
4. **Workspace.** A format 3 package takes part in operations like a package of [PACKAGE]: the rules of Sections 6 to 8 of [WORKSPACE] apply to it. It is recorded in the workspace file with a `kind` other than `"mcmod"` (Section 8.1 of [WORKSPACE]); its entry is not defined by this document.

Format 3 packages are read, never written: `lip` does not create a tooth.json, and `lip migrate` remains the only command that reads a tooth.json to write a manifest.

Supporting format 3 packages does not require supporting a tooth.json of any other format.

## 4. Required Package Versions

`lip` MUST install, uninstall and update every package version listed in this section, with every variant it has, in every environment that variant applies to, as Section 3 requires. Each is named by its tooth path and version, and is the git archive of the tag `v<version>` of that repository.

### 4.1. Selection

The versions of Section 4.2 are those published from 2026-10-05 to 2026-11-05 inclusive, by date of their tag, by the LiteLDev projects named on levimc.org [LEVIMC] whose repository has a tooth.json:

| Project | Tooth path | Versions in the period |
|---|---|---|
| LeviLamina | `github.com/LiteLDev/LeviLamina` | 5 |
| LegacyScriptEngine | `github.com/LiteLDev/LegacyScriptEngine` | 4 |
| LeviAntiCheat | `github.com/LiteLDev/LeviAntiCheat` | 6 |
| MoreDimensions | `github.com/LiteLDev/MoreDimensions` | 1 |
| LeviOptimize | `github.com/LiteLDev/LeviOptimize` | 0 |

The other projects named there, LeviLauncher, LeviLaunchroid, LeviStone and lip, are not packages: their repositories have no tooth.json.

### 4.2. Published Versions

| Tooth path | Version | Tag date |
|---|---|---|
| `github.com/LiteLDev/LeviLamina` | 26.51.7 | 2026-10-12 |
| `github.com/LiteLDev/LeviLamina` | 26.51.8 | 2026-10-24 |
| `github.com/LiteLDev/LeviLamina` | 26.51.9 | 2026-10-27 |
| `github.com/LiteLDev/LeviLamina` | 26.60.0 | 2026-11-03 |
| `github.com/LiteLDev/LeviLamina` | 26.60.1 | 2026-11-05 |
| `github.com/LiteLDev/LegacyScriptEngine` | 0.22.3 | 2026-10-11 |
| `github.com/LiteLDev/LegacyScriptEngine` | 0.22.4 | 2026-10-31 |
| `github.com/LiteLDev/LegacyScriptEngine` | 0.23.0 | 2026-11-03 |
| `github.com/LiteLDev/LegacyScriptEngine` | 0.23.1 | 2026-11-04 |
| `github.com/LiteLDev/LeviAntiCheat` | 0.17.3 | 2026-10-06 |
| `github.com/LiteLDev/LeviAntiCheat` | 0.17.4 | 2026-10-07 |
| `github.com/LiteLDev/LeviAntiCheat` | 0.17.5 | 2026-10-21 |
| `github.com/LiteLDev/LeviAntiCheat` | 0.17.6 | 2026-10-22 |
| `github.com/LiteLDev/LeviAntiCheat` | 0.17.7 | 2026-11-03 |
| `github.com/LiteLDev/LeviAntiCheat` | 0.18.0 | 2026-11-03 |
| `github.com/LiteLDev/MoreDimensions` | 0.15.0 | 2026-10-12 |

All of them are format 3 packages.

### 4.3. Dependencies

The **dependencies** of a version of Section 4.2 are the package versions that a new installation of it, with one of its variants, installs into a workspace that has no installed packages: for each tooth path and label that its `dependencies`, in any asset, name directly or through other dependencies, the highest version that satisfies every range on it, `{{version}}` in a range being replaced by the version of the depending package.

Determined on 2026-11-05 over every variant of every version of Section 4.2, the dependencies are, besides versions of Section 4.2:

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

All of them are format 3 packages. Versions published after 2026-11-05 are not required by this section.

## 5. Security Considerations

A format 3 package runs the commands of its `scripts` when it is installed and uninstalled, and downloads its assets from the URLs it names without checking their content against a recorded digest. Installing a format 3 package therefore trusts both its repository and every host its assets are downloaded from.

## 6. IANA Considerations

This document has no IANA actions.

## 7. References

### 7.1. Normative References

- [CLI] "lip Command Line Interface", [`cli.md`](cli.md).
- [LIP-V0.37] LiteLDev, "lip v0.37.0", <https://github.com/LiteLDev/lip/tree/v0.37.0>.
- [PACKAGE] "lip Package Protocol", [`package.md`](package.md).
- [RFC2119] Bradner, S., "Key words for use in RFCs to Indicate Requirement Levels", BCP 14, RFC 2119, DOI 10.17487/RFC2119, March 1997, <https://www.rfc-editor.org/info/rfc2119>.
- [RFC8174] Leiba, B., "Ambiguity of Uppercase vs Lowercase in RFC 2119 Key Words", BCP 14, RFC 8174, DOI 10.17487/RFC8174, May 2017, <https://www.rfc-editor.org/info/rfc8174>.
- [TOOTH-V3] LiteLDev, "Package Manifest", lip v0.37.0, <https://github.com/LiteLDev/lip/blob/v0.37.0/docs/concepts/package_manifest.md>, and its JSON Schema, <https://github.com/LiteLDev/lip/blob/v0.37.0/schemas/manifest.schema.json>.
- [WORKSPACE] "lip Workspace Protocol", [`workspace.md`](workspace.md).

### 7.2. Informative References

- [LEVIMC] LeviMC, "LeviMC", <https://levimc.org/>.
