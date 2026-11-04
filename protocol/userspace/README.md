# lip Userspace Protocol

## Abstract

This document defines the files of `lip` that belong to the operating system user rather than to a workspace: the configuration, the data directory and the cache directory.

## Status of This Memo

This document is part of the lip protocol and is normative. The format of the configuration file is defined by the JSON Schema [`config.schema.json`](config.schema.json).

## Table of Contents

1. [Introduction](#1-introduction)
   1. [Requirements Language](#11-requirements-language)
2. [Directories](#2-directories)
3. [Configuration](#3-configuration)
4. [Security Considerations](#4-security-considerations)
5. [IANA Considerations](#5-iana-considerations)
6. [References](#6-references)
   1. [Normative References](#61-normative-references)

## 1. Introduction

`lip` keeps per-user state in three directories (Section 2), one of which holds its configuration (Section 3).

### 1.1. Requirements Language

The key words "MUST", "MUST NOT", "REQUIRED", "SHALL", "SHALL NOT", "SHOULD", "SHOULD NOT", "RECOMMENDED", "NOT RECOMMENDED", "MAY", and "OPTIONAL" in this document are to be interpreted as described in BCP 14 [RFC2119] [RFC8174] when, and only when, they appear in all capitals, as shown here.

## 2. Directories

| Directory | Windows | Other |
|---|---|---|
| **config directory** | `%APPDATA%\lip` | `$XDG_CONFIG_HOME/lip` |
| **data directory** | `%LOCALAPPDATA%\lip` | `$XDG_DATA_HOME/lip` |
| **cache directory** | `%LOCALAPPDATA%\lip\Cache` | `$XDG_CACHE_HOME/lip` |

On systems other than Windows, an `XDG_*` variable that is unset, empty or not an absolute path takes its default from [XDG]: `~/.config`, `~/.local/share` and `~/.cache`.

- The config directory holds the configuration (Section 3).
- The data directory holds the state `lip` needs to finish or undo an interrupted operation on a workspace, and to keep two operations from changing one workspace at the same time. Its contents are not defined by this document. They MUST NOT be changed by anything other than `lip`; deleting them while no `lip` process is running loses only the ability to recover an interrupted operation.
- The cache directory holds only data that `lip` can obtain again. Its contents are not defined by this document. It MAY be deleted at any time while no `lip` process is running; `lip cache clean` deletes it.

Besides these directories, `lip` writes only to the workspace it operates on and to paths given to it as arguments; it does not write to the system temporary directory. Commands run by hooks are not bound by this.

## 3. Configuration

The **configuration** is the file `config.yaml` in the config directory: a [YAML] mapping of configuration keys to values, valid against `config.schema.json`. An absent key has its default value; keys that are not configuration keys are ignored. When the file cannot be read, `lip` uses the default values.

The **configuration keys** are:

| Key | Type | Default | Meaning |
|---|---|---|---|
| `github_proxy` | URI prefix | none | The prefix through which `lip` sends requests to GitHub (git repositories and downloads). When a request through it fails, `lip` sends the request to GitHub directly. |

`lip config set` and `lip config delete` rewrite the file, leaving out keys at their default value.

Earlier formats. When `config.yaml` does not exist and the file `liprc.json` written by lip v0.x exists in the config directory, `lip` reads its configuration from `liprc.json`, writes it to `config.yaml` and deletes `liprc.json`. A `liprc.json` that cannot be read is kept, and the default values are used.

## 4. Security Considerations

The `github_proxy` prefix receives every request `lip` sends to GitHub, so it can observe and alter what `lip` downloads from GitHub.

## 5. IANA Considerations

This document has no IANA actions.

## 6. References

### 6.1. Normative References

- [RFC2119] Bradner, S., "Key words for use in RFCs to Indicate Requirement Levels", BCP 14, RFC 2119, DOI 10.17487/RFC2119, March 1997, <https://www.rfc-editor.org/info/rfc2119>.
- [RFC8174] Leiba, B., "Ambiguity of Uppercase vs Lowercase in RFC 2119 Key Words", BCP 14, RFC 8174, DOI 10.17487/RFC8174, May 2017, <https://www.rfc-editor.org/info/rfc8174>.
- [XDG] freedesktop.org, "XDG Base Directory Specification", <https://specifications.freedesktop.org/basedir-spec/latest/>.
- [YAML] "YAML Ain't Markup Language (YAML) Version 1.2", Revision 1.2.2, October 2021, <https://yaml.org/spec/1.2.2/>.
