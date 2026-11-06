# lip Protocol

The lip protocol is normative and consists of:

| Document | Defines |
|---|---|
| [PACKAGE] | Packages, sources and resolution; `tooth.json` by [`tooth.schema.json`](tooth.schema.json). |
| [WORKSPACE] | Workspaces and operations on them; the workspace file by [`workspace.schema.json`](workspace.schema.json). |
| [USERSPACE] | Per-user configuration and cache; the configuration file by [`config.schema.json`](config.schema.json). |
| [CLI] | The command line interface of `lip`. |
| [COMPATIBILITY] | The `tooth.json` formats 1 to 3, and the packages that `lip` MUST install. |

## Conventions

The key words "MUST", "MUST NOT", "REQUIRED", "SHALL", "SHALL NOT", "SHOULD", "SHOULD NOT", "RECOMMENDED", "NOT RECOMMENDED", "MAY", and "OPTIONAL" are to be interpreted as described in BCP 14 [RFC2119] [RFC8174] when, and only when, they appear in all capitals, as shown here.

A term is defined where it appears in bold, and has that meaning in every document that does not say otherwise.

A file is **written atomically** by writing its whole content to another file in the same directory, then renaming that file to its name.

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
- [SEMVER-NET] Walker, J., "Semver for .NET", version 3.0.0, `SemVersionRange.Parse`, <https://github.com/WalkerCodeRanger/semver/tree/v3.0.0>.
- [SEMVER] Preston-Werner, T., "Semantic Versioning 2.0.0", <https://semver.org/spec/v2.0.0.html>.
- [TOOTH-V3] LiteLDev, "Package Manifest", lip v0.37.0, <https://github.com/LiteLDev/lip/blob/v0.37.0/docs/concepts/package_manifest.md>, and its JSON Schema, <https://github.com/LiteLDev/lip/blob/v0.37.0/schemas/manifest.schema.json>.
- [USERSPACE] [`userspace.md`](userspace.md).
- [WORKSPACE] [`workspace.md`](workspace.md).
- [XDG] freedesktop.org, "XDG Base Directory Specification", <https://specifications.freedesktop.org/basedir-spec/latest/>.
- [YAML] "YAML Ain't Markup Language (YAML) Version 1.2", Revision 1.2.2, October 2021, <https://yaml.org/spec/1.2.2/>.
