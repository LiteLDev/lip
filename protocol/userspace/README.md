# lip Userspace Protocol

## Abstract

This document defines the files of `lip` that belong to the operating system user rather than to a workspace: the configuration and the cache.

## Status of This Memo

This document is part of the lip protocol and is normative. The format of the configuration file is defined by the JSON Schema [`config.schema.json`](config.schema.json).

## Table of Contents

1. [Introduction](#1-introduction)
   1. [Requirements Language](#11-requirements-language)
2. [Directories](#2-directories)
3. [Configuration](#3-configuration)
4. [Cache](#4-cache)
5. [Security Considerations](#5-security-considerations)
6. [IANA Considerations](#6-iana-considerations)
7. [References](#7-references)
   1. [Normative References](#71-normative-references)

## 1. Introduction

`lip` keeps per-user state in two directories (Section 2): its configuration (Section 3) and a cache of data it obtained over the network (Section 4). State that belongs to a workspace is kept in the workspace [WORKSPACE].

### 1.1. Requirements Language

The key words "MUST", "MUST NOT", "REQUIRED", "SHALL", "SHALL NOT", "SHOULD", "SHOULD NOT", "RECOMMENDED", "NOT RECOMMENDED", "MAY", and "OPTIONAL" in this document are to be interpreted as described in BCP 14 [RFC2119] [RFC8174] when, and only when, they appear in all capitals, as shown here.

## 2. Directories

| Directory | Windows | Other |
|---|---|---|
| **config directory** | `%APPDATA%\lip` | `$XDG_CONFIG_HOME/lip` |
| **cache directory** | `%LOCALAPPDATA%\lip\Cache` | `$XDG_CACHE_HOME/lip` |

On systems other than Windows, an `XDG_*` variable that is unset, empty or not an absolute path takes its default from [XDG]: `~/.config` and `~/.cache`.

Besides these directories, `lip` writes only to the workspace it operates on and to paths given to it as arguments; it does not write to the system temporary directory. Commands run by hooks are not bound by this.

Earlier formats. lip v0.36 also kept files in `%LOCALAPPDATA%\lip\workspaces` on Windows and `$XDG_DATA_HOME/lip/workspaces` elsewhere. They are neither read nor deleted.

## 3. Configuration

The **configuration** is the file `config.yaml` in the config directory: a [YAML] mapping of configuration keys to values, valid against `config.schema.json`. An absent key has its default value; keys that are not configuration keys are ignored. When the file does not exist or cannot be read, every key has its default value.

The **configuration keys** are:

| Key | Type | Default | Meaning |
|---|---|---|---|
| `github_proxy` | URI prefix | none | The prefix through which `lip` sends requests to GitHub (git repositories and downloads). When a request through it fails, `lip` sends the request to GitHub directly. |

`lip config set` and `lip config delete` rewrite the file, leaving out keys at their default value.

Earlier formats. When `config.yaml` does not exist and the file `liprc.json` written by lip v0.x exists in the config directory, `lip` reads its configuration from `liprc.json`, writes it to `config.yaml` and deletes `liprc.json`. A `liprc.json` that cannot be read is kept, and every key has its default value.

## 4. Cache

The cache directory holds only data that can be obtained again. Every process that uses it, `lip` or not, follows this section; other processes MAY read and write it.

**Entries.** The cache directory contains these entries:

| Entry | Content |
|---|---|
| `http-<H>` | A regular file: the body of a successful response to an HTTPS GET of a URL U, following redirects. `<H>` is the SHA-256 of the UTF-8 encoding of U, in lowercase hexadecimal. U is the URL as a source or a manifest writes it, without its fragment, whatever proxy the request was sent through. |
| `git-<O>` | A regular file: the content of the git blob whose object id is `<O>` [GIT-OBJECTS], without the object header. `<O>` is in lowercase hexadecimal. |
| `.staging` | A directory of files being written. |

Any other entry is not defined by this document, and a process MUST NOT read meaning into it.

**Writing.** A process creates an entry only by writing the whole file below `.staging`, under a name no other process uses, and then renaming it to the name of the entry. A process MUST NOT create an entry `git-<O>` whose content does not have the object id `<O>`. A process MUST NOT change an entry in place; when an entry already exists, the rename either fails or replaces it with a file of the same meaning.

**Reading.** An entry that exists is complete. The body behind a URL can change, so a reader that knows what the content of an `http-<H>` entry must be (for example, from the SHA-256 recorded in a workspace file [WORKSPACE]) MUST check it, and MAY delete an entry that does not match.

**Deleting.** Any entry MAY be deleted; deleting an entry while another process reads it can make that process fail. A file below `.staging` MAY be deleted when no process is writing it. `lip cache clean` deletes the cache directory.

## 5. Security Considerations

The `github_proxy` prefix receives every request `lip` sends to GitHub, so it can observe and alter what `lip` downloads from GitHub.

Any process that can write to the cache directory can alter what `lip` installs from a URL whose content the workspace file does not record yet. `git-<O>` entries are named by their content and can be checked.

## 6. IANA Considerations

This document has no IANA actions.

## 7. References

### 7.1. Normative References

- [GIT-OBJECTS] Chacon, S. and B. Straub, "Pro Git, Section 10.2: Git Objects", <https://git-scm.com/book/en/v2/Git-Internals-Git-Objects>.
- [RFC2119] Bradner, S., "Key words for use in RFCs to Indicate Requirement Levels", BCP 14, RFC 2119, DOI 10.17487/RFC2119, March 1997, <https://www.rfc-editor.org/info/rfc2119>.
- [RFC8174] Leiba, B., "Ambiguity of Uppercase vs Lowercase in RFC 2119 Key Words", BCP 14, RFC 8174, DOI 10.17487/RFC8174, May 2017, <https://www.rfc-editor.org/info/rfc8174>.
- [WORKSPACE] "lip Workspace Protocol", [`../workspace/README.md`](../workspace/README.md).
- [XDG] freedesktop.org, "XDG Base Directory Specification", <https://specifications.freedesktop.org/basedir-spec/latest/>.
- [YAML] "YAML Ain't Markup Language (YAML) Version 1.2", Revision 1.2.2, October 2021, <https://yaml.org/spec/1.2.2/>.
