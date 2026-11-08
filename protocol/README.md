# lip Protocol

The lip protocol is normative and consists of:

| Document | Defines |
|---|---|
| [PACKAGE] | Packages, sources and resolution; the package file by [`lip.schema.toml`](lip.schema.toml). |
| [WORKSPACE] | Workspaces and operations on them; the state file by [`state.schema.json`](state.schema.json). |
| [USERSPACE] | Per-user configuration and cache; the configuration file by [`config.schema.toml`](config.schema.toml). |
| [CLI] | The command line interface of `lip`. |
| [COMPATIBILITY] | Reading and migrating the files of formats 1 to 3, and the packages that `lip` MUST install. |

## Conventions

The key words "MUST", "MUST NOT", "REQUIRED", "SHALL", "SHALL NOT", "SHOULD", "SHOULD NOT", "RECOMMENDED", "NOT RECOMMENDED", "MAY", and "OPTIONAL" are to be interpreted as described in BCP 14 [RFC2119] [RFC8174] when, and only when, they appear in all capitals, as shown here.

A term is defined where it appears in bold, and has that meaning in every document that does not say otherwise.

A **TOML file** is a [TOML] document in UTF-8, read as the JSON [RFC8259] value it represents: a table is an object, an array is an array, a string is a string, an integer or a float is a number, and a boolean is a boolean. A TOML file holding a date-time, a date or a time, or the float `inf` or `nan`, is invalid. The TOML schemas (`*.schema.toml`) are JSON Schemas (draft-07) written as TOML files, and constrain that value.

A **JSON file** is a JSON [RFC8259] text in UTF-8, with no two members of the same name in an object. The JSON schemas (`*.schema.json`) are JSON Schemas (draft-07) written as JSON files, and constrain its value.

A file is **written atomically** by writing its whole content to another file in the same directory, whose name ends in `.tmp`, then renaming that file to its name.

"Section N" refers to the same document; "Section N of [X]" to document X. "Not defined here" means not defined by the document that says it.

Grammars use ABNF [RFC5234]; a rule written as `<...>` is defined by the prose inside the brackets.

## References

- [APPNOTE] PKWARE Inc., "APPNOTE.TXT - .ZIP File Format Specification", <https://pkware.cachefly.net/webdocs/casestudies/APPNOTE.TXT>.
- [CLI] [`cli.md`](cli.md).
- [COMPATIBILITY] [`compatibility.md`](compatibility.md).
- [CREATEFILE] Microsoft, "CreateFileW function (fileapi.h)", <https://learn.microsoft.com/windows/win32/api/fileapi/nf-fileapi-createfilew>.
- [FLOCK] "flock(2) - Linux manual page", <https://man7.org/linux/man-pages/man2/flock.2.html>.
- [GIT-OBJECTS] Chacon, S. and B. Straub, "Pro Git, Section 10.2: Git Objects", <https://git-scm.com/book/en/v2/Git-Internals-Git-Objects>.
- [GIT-REF] "git-check-ref-format", <https://git-scm.com/docs/git-check-ref-format>.
- [GO-MODULE] "golang.org/x/mod/module: CheckPath", <https://pkg.go.dev/golang.org/x/mod/module#CheckPath>.
- [PACKAGE] [`package.md`](package.md).
- [RFC2119] Bradner, S., "Key words for use in RFCs to Indicate Requirement Levels", BCP 14, RFC 2119, DOI 10.17487/RFC2119, March 1997, <https://www.rfc-editor.org/info/rfc2119>.
- [RFC3339] Klyne, G. and C. Newman, "Date and Time on the Internet: Timestamps", RFC 3339, DOI 10.17487/RFC3339, July 2002, <https://www.rfc-editor.org/info/rfc3339>.
- [RFC3986] Berners-Lee, T., Fielding, R., and L. Masinter, "Uniform Resource Identifier (URI): Generic Syntax", STD 66, RFC 3986, DOI 10.17487/RFC3986, January 2005, <https://www.rfc-editor.org/info/rfc3986>.
- [RFC5234] Crocker, D., Ed. and P. Overell, "Augmented BNF for Syntax Specifications: ABNF", STD 68, RFC 5234, DOI 10.17487/RFC5234, January 2008, <https://www.rfc-editor.org/info/rfc5234>.
- [RFC8089] Kerwin, M., "The "file" URI Scheme", RFC 8089, DOI 10.17487/RFC8089, February 2017, <https://www.rfc-editor.org/info/rfc8089>.
- [RFC8174] Leiba, B., "Ambiguity of Uppercase vs Lowercase in RFC 2119 Key Words", BCP 14, RFC 8174, DOI 10.17487/RFC8174, May 2017, <https://www.rfc-editor.org/info/rfc8174>.
- [RFC8259] Bray, T., Ed., "The JavaScript Object Notation (JSON) Data Interchange Format", STD 90, RFC 8259, DOI 10.17487/RFC8259, December 2017, <https://www.rfc-editor.org/info/rfc8259>.
- [RID] Microsoft, ".NET RID catalog", <https://learn.microsoft.com/dotnet/core/rid-catalog>.
- [SCRIBAN] Mutel, A., "Scriban language", Scriban version 7.5.0, <https://github.com/scriban/scriban/blob/7.5.0/site/docs/language.md>.
- [SEMVER-NET] Walker, J., "Semver for .NET", version 3.0.0, `SemVersionRange.Parse`, <https://github.com/WalkerCodeRanger/semver/tree/v3.0.0>.
- [SEMVER] Preston-Werner, T., "Semantic Versioning 2.0.0", <https://semver.org/spec/v2.0.0.html>.
- [TOML] Preston-Werner, T., "TOML: Tom's Obvious Minimal Language", version 1.0.0, <https://toml.io/en/v1.0.0>.
- [TOOTH-V3] LiteLDev, "Package Manifest", lip v0.37.0, <https://github.com/LiteLDev/lip/blob/v0.37.0/docs/concepts/package_manifest.md>, and its JSON Schema, <https://github.com/LiteLDev/lip/blob/v0.37.0/schemas/manifest.schema.json>.
- [USERSPACE] [`userspace.md`](userspace.md).
- [WORKSPACE] [`workspace.md`](workspace.md).
- [XDG] freedesktop.org, "XDG Base Directory Specification", <https://specifications.freedesktop.org/basedir-spec/latest/>.
