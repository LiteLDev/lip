using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Lip.Core.Mcmod;

/// The forms of a source (protocol/package.md §2).
public enum RefKind {
  /// "https://..." naming a ZIP archive (a URL).
  Url,

  /// A Go module path naming a git repository (a remote repository); its version tags are the
  /// versions, unless a ref pins it.
  Spec,

  /// "file:///..." naming a local ZIP file or a local directory (a local path). Requests have
  /// local paths as the file: URI of their absolute path (protocol/cli.md §3).
  File,
}

public enum GitRefKind {
  /// A SemVer 2.0.0 version V, selecting the tag "v" + V.
  Version,
  Branch,
  Tag,
  Rev,
}

/// The ref of a remote repository (protocol/package.md §2.1): a version, "branch:" name,
/// "tag:" name or "rev:" commit id.
public sealed partial record GitRef {
  private GitRef(GitRefKind kind, string name) {
    Kind = kind;
    Name = name;
  }

  public GitRefKind Kind { get; }

  /// The version, the branch or tag name, or the full lowercase commit id of a rev.
  public string Name { get; }

  public override string ToString() => $"{Prefix(Kind)}{Name}";

  public static bool TryParse(
      string s,
      [NotNullWhen(true)] out GitRef? result,
      [NotNullWhen(false)] out string? error) {
    result = null;
    foreach (GitRefKind kind in (GitRefKind[])[GitRefKind.Branch, GitRefKind.Tag, GitRefKind.Rev]) {
      string prefix = Prefix(kind);
      if (!s.StartsWith(prefix, StringComparison.Ordinal)) {
        continue;
      }

      string name = s[prefix.Length..];
      if (kind == GitRefKind.Rev) {
        if (!CommitIdRegex().IsMatch(name)) {
          error = $"'{name}' is not a full commit id: write all 40 hexadecimal digits, not an"
              + " abbreviation";
          return false;
        }

        name = name.ToLowerInvariant();
      } else if (!IsValidRefName(name)) {
        string what = kind.ToString().ToLowerInvariant();
        error = name == ""
            ? $"the {what} name after \"{prefix}\" is missing"
            : $"'{name}' is not a valid git {what} name (see 'git check-ref-format')";
        return false;
      }

      result = new GitRef(kind, name);
      error = null;
      return true;
    }

    if (Semver.SemVersion.TryParse(s, Semver.SemVersionStyles.Strict, out _)) {
      result = new GitRef(GitRefKind.Version, s);
      error = null;
      return true;
    }

    error = s.Length > 1 && s[0] is 'v' or 'V'
        && Semver.SemVersion.TryParse(s[1..], Semver.SemVersionStyles.Strict, out _)
            ? $"write the version without the leading 'v', as '{s[1..]}'"
            : "a ref is a SemVer version such as 1.2.3 (without a leading 'v'), branch:<name>,"
                + " tag:<name> or rev:<full commit id>";
    return false;
  }

  public static GitRef Parse(string s) => TryParse(s, out GitRef? r, out string? error)
      ? r
      : throw new FormatException($"The ref '{s}' of a remote repository is not valid: {error}.");

  public static GitRef Rev(string commit) => Parse($"rev:{commit}");

  /// Whether a branch or tag name is valid as git check-ref-format defines it for one ref name
  /// below refs/heads/ or refs/tags/.
  public static bool IsValidRefName(string name) {
    if (name is "" or "@" || name.StartsWith('/') || name.EndsWith('/') || name.EndsWith('.')
        || name.Contains("//") || name.Contains("..") || name.Contains("@{")) {
      return false;
    }

    if (name.Any(c => c < 0x20 || c == 0x7f || c is ' ' or '~' or '^' or ':' or '?' or '*'
        or '[' or '\\')) {
      return false;
    }

    return name.Split('/').All(c => !c.StartsWith('.')
        && !c.EndsWith(".lock", StringComparison.Ordinal));
  }

  private static string Prefix(GitRefKind kind) => kind switch {
    GitRefKind.Version => "",
    GitRefKind.Branch => "branch:",
    GitRefKind.Tag => "tag:",
    _ => "rev:",
  };

  [GeneratedRegex(@"^[0-9a-fA-F]{40}\z")]
  private static partial Regex CommitIdRegex();
}

/// A source (protocol/package.md §2): target [ "#" variant ], where a remote repository may be
/// followed by [ "@" ref ] after the variant.
public partial record Ref {
  private const string _fileScheme = "file:";
  private static readonly UTF8Encoding _strictUtf8 = new(false, true);

  private Ref(RefKind kind, string target, string variant, GitRef? gitRef = null) {
    Kind = kind;
    Target = target;
    Variant = variant;
    GitRef = gitRef;
  }

  public RefKind Kind { get; }

  /// The location without the variant and the git ref: the URL exactly as written, the module
  /// path of a spec ref, or the absolute file URI of a file ref.
  public string Target { get; }

  /// The part after the "#", or "" when there is none.
  public string Variant { get; }

  /// The git ref pinning a spec ref, or null when the spec ref selects by tag version.
  public GitRef? GitRef { get; }

  /// For file refs, the absolute local path.
  public string LocalPath => Kind == RefKind.File
      ? FileUriToPath(Target)!
      : throw new InvalidOperationException(
          $"The source '{this}' is not a local path, so it has no local file path.");

  public Ref WithVariant(string variant) => variant == "" || IsValidVariant(variant)
      ? new Ref(Kind, Target, variant, GitRef)
      : throw new FormatException(
          $"The variant '{variant}' of '{Target}' is not valid: {VariantRule}.");

  /// What a variant looks like, for messages.
  public const string VariantRule = "a variant is a name, or two names separated by '/', of"
      + " lowercase letters, digits and '_' (e.g. client or my_variant/sub)";

  public override string ToString() => Target
      + (Variant == "" ? "" : $"#{Variant}")
      + (GitRef is null ? "" : $"@{GitRef}");

  public static bool IsValidVariant(string variant) => VariantRegex().IsMatch(variant);

  /// A file ref for an absolute local path.
  public static Ref FromFile(string absolutePath, string variant = "") =>
      PathToFileUri(absolutePath) is string uri
          ? new Ref(RefKind.File, uri, "").WithVariant(variant)
          : throw new ArgumentException($"'{absolutePath}' is not an absolute local path.");

  /// The file: URI [RFC8089] of an absolute local path, with every octet a path segment does not
  /// allow as it is percent-encoded, or null when the path is not absolute. On Windows, it is
  /// "file:///" drive "/" ... for a path on a drive and "file://" host "/" share "/" ... for a
  /// UNC path.
  private static string? PathToFileUri(string path) {
    if (path.Contains('\0')) {
      return null;
    }

    if (!OperatingSystem.IsWindows()) {
      return path.StartsWith('/')
          ? "file://" + string.Join('/', path.Split('/').Select(Escape))
          : null;
    }

    string[] segments = path.Split('\\', '/');
    if (path.Length >= 2 && path[0] is '\\' or '/' && path[1] is '\\' or '/') {
      return segments.Length >= 4 && segments[2] != "" && segments[3] != ""
          ? $"file://{Escape(segments[2])}/{string.Join('/', segments[3..].Select(Escape))}"
          : null;
    }

    return segments.Length >= 2 && IsDrive(segments[0])
        ? $"file:///{segments[0]}/{string.Join('/', segments[1..].Select(Escape))}"
        : null;
  }

  /// The absolute local path a file: URI [RFC8089] names, the reverse of PathToFileUri, or null
  /// when it names none: when it has a query or fragment, is no valid URI, or names a remote
  /// host other than on Windows, or no drive on Windows.
  private static string? FileUriToPath(string uri) {
    if (!uri.StartsWith(_fileScheme, StringComparison.OrdinalIgnoreCase)) {
      return null;
    }

    string path = uri[_fileScheme.Length..];
    string host = "";
    if (path.StartsWith("//", StringComparison.Ordinal)) {
      int slash = path.IndexOf('/', 2);
      if (slash < 0) {
        return null;
      }

      host = path[2..slash];
      path = path[slash..];
    }

    if (!path.StartsWith('/') || !UriTextRegex().IsMatch(host + path)
        || (host + path).IndexOfAny(['?', '#', '[', ']']) >= 0) {
      return null;
    }

    char separator = OperatingSystem.IsWindows() ? '\\' : '/';
    List<string> names = [];
    foreach (string segment in path[1..].Split('/')) {
      if (Unescape(segment) is not string name || name.IndexOfAny(['/', separator, '\0']) >= 0) {
        return null;
      }

      names.Add(name);
    }

    string? hostName = Unescape(host);
    if (hostName is null || hostName.IndexOfAny(['/', '\\', '\0']) >= 0) {
      return null;
    }

    bool local = hostName == "" || hostName.Equals("localhost", StringComparison.OrdinalIgnoreCase);
    if (!OperatingSystem.IsWindows()) {
      return local ? "/" + string.Join('/', names) : null;
    }

    if (!local) {
      return names[0] == "" ? null : $@"\\{hostName}\{string.Join('\\', names)}";
    }

    return IsDrive(names[0])
        ? string.Join('\\', names.Count == 1 ? [names[0], ""] : names)
        : null;
  }

  /// Whether a Windows path segment is a drive, such as "C:".
  private static bool IsDrive(string segment) =>
      segment.Length == 2 && char.IsAsciiLetter(segment[0]) && segment[1] == ':';

  /// Percent-encodes the UTF-8 octets of a path segment but those RFC 3986 allows as they are
  /// (pchar).
  private static string Escape(string segment) {
    StringBuilder builder = new();
    foreach (byte b in Encoding.UTF8.GetBytes(segment)) {
      char c = (char)b;
      if (char.IsAsciiLetterOrDigit(c) || "-._~!$&'()*+,;=:@".Contains(c)) {
        builder.Append(c);
      } else {
        builder.Append(CultureInfo.InvariantCulture, $"%{b:X2}");
      }
    }

    return builder.ToString();
  }

  /// Decodes the percent-encoded octets of an ASCII URI component with every '%' starting one,
  /// as UTF-8, or null when they are no valid UTF-8.
  private static string? Unescape(string component) {
    List<byte> bytes = [];
    for (int i = 0; i < component.Length; i++) {
      if (component[i] == '%') {
        bytes.Add(byte.Parse(component.AsSpan(i + 1, 2), NumberStyles.HexNumber,
            CultureInfo.InvariantCulture));
        i += 2;
      } else {
        bytes.Add((byte)component[i]);
      }
    }

    try {
      return _strictUtf8.GetString([.. bytes]);
    }
    catch (DecoderFallbackException) {
      return null;
    }
  }

  /// Parses a source; allowFile also accepts local paths as file: URIs.
  public static Ref Parse(string s, bool allowFile) =>
      TryParse(s, allowFile, out Ref? r, out string? error)
          ? r
          : throw new FormatException($"The source '{s}' is not valid: {error}.");

  public static bool TryParse(
      string s,
      [NotNullWhen(true)] out Ref? result,
      [NotNullWhen(false)] out string? error) => TryParse(s, false, out result, out error);

  public static bool TryParse(
      string s,
      bool allowFile,
      [NotNullWhen(true)] out Ref? result,
      [NotNullWhen(false)] out string? error) {
    result = null;

    bool isUrl = s.StartsWith("https://", StringComparison.Ordinal);
    bool isFile = s.StartsWith(_fileScheme, StringComparison.OrdinalIgnoreCase);

    // Module paths, variants and URLs before the fragment contain no '@' that starts a git ref;
    // everything after the first '@' of a spec ref is its git ref, which may contain '#'.
    string head = s;
    GitRef? gitRef = null;
    if (!isUrl && !isFile) {
      int at = s.IndexOf('@');
      if (at >= 0) {
        head = s[..at];
        if (!GitRef.TryParse(s[(at + 1)..], out gitRef, out string? gitError)) {
          error = $"the ref '{s[(at + 1)..]}' after '@' is not valid: {gitError}";
          return false;
        }
      }
    }

    int hash = head.IndexOf('#');
    string target = hash < 0 ? head : head[..hash];
    string variant = hash < 0 ? "" : head[(hash + 1)..];

    if (hash >= 0 && !IsValidVariant(variant)) {
      error = variant == ""
          ? $"the variant after '#' is missing; {VariantRule}"
          : $"the variant '{variant}' is not valid; {VariantRule}";
      return false;
    }

    if (isUrl) {
      if (!IsUriText(target)) {
        error = $"'{target}' is not a valid absolute https:// URL (characters other than those"
            + " a URL allows must be percent-encoded)";
        return false;
      }
      if (!Uri.TryCreate(target, UriKind.Absolute, out Uri? uri) || uri.Scheme != "https") {
        error = $"'{target}' is not a valid absolute https:// URL";
        return false;
      }
      if (uri.Host == "") {
        error = $"'{target}' is not a valid absolute https:// URL: it has no host name";
        return false;
      }

      result = new Ref(RefKind.Url, target, variant);
    } else if (isFile) {
      if (!allowFile) {
        error = "local paths are only allowed in package arguments on the command line, not as"
            + " dependencies";
        return false;
      }

      if (FileUriToPath(target) is not string path || PathToFileUri(path) is not string uri) {
        error = $"'{target}' is not an absolute file: URI";
        return false;
      }

      result = new Ref(RefKind.File, uri, variant);
    } else {
      if (Golang.Org.X.Mod.Module.CheckPath(target) is Exception ex) {
        // The reason alone; the outer message names the path again.
        string reason = ex.InnerException?.Message ?? ex.Message;
        error = $"'{target}' is not a valid repository path: {reason}; a remote repository is"
            + " written like github.com/owner/repo";
        return false;
      }

      result = new Ref(RefKind.Spec, target, variant, gitRef);
    }

    error = null;
    return true;
  }

  [GeneratedRegex(@"^[a-z0-9_]+(/[a-z0-9_]+)?\z")]
  private static partial Regex VariantRegex();

  /// Whether s has only the characters RFC 3986 allows in a URI, with every '%' starting a
  /// percent-encoded octet, and '[' and ']' only in the host; Uri.TryCreate alone also accepts
  /// and escapes others.
  private static bool IsUriText(string s) {
    int authority = s.IndexOf("://", StringComparison.Ordinal) + 3;
    int pathStart = s.IndexOfAny(['/', '?', '#'], authority);
    return UriTextRegex().IsMatch(s)
        && (pathStart < 0 || s.IndexOfAny(['[', ']'], pathStart) < 0);
  }

  [GeneratedRegex(@"^(?:[A-Za-z0-9\-._~:/?#\[\]@!$&'()*+,;=]|%[0-9A-Fa-f]{2})*\z")]
  private static partial Regex UriTextRegex();
}
