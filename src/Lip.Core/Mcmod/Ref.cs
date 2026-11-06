using System.Diagnostics.CodeAnalysis;
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
          error = $"'{name}' is not a full 40-digit hexadecimal commit id";
          return false;
        }

        name = name.ToLowerInvariant();
      } else if (!IsValidRefName(name)) {
        error = $"'{name}' is not a valid git {kind.ToString().ToLowerInvariant()} name";
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

    error = $"'{s}' is not <version>, branch:<name>, tag:<name> or rev:<commit>";
    return false;
  }

  public static GitRef Parse(string s) => TryParse(s, out GitRef? r, out string? error)
      ? r
      : throw new FormatException($"Invalid git ref '{s}': {error}");

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
      ? new Uri(Target).LocalPath
      : throw new InvalidOperationException($"Not a file ref: {this}");

  public Ref WithVariant(string variant) => variant == "" || IsValidVariant(variant)
      ? new Ref(Kind, Target, variant, GitRef)
      : throw new FormatException($"Invalid variant: {variant}");

  public override string ToString() => Target
      + (Variant == "" ? "" : $"#{Variant}")
      + (GitRef is null ? "" : $"@{GitRef}");

  public static bool IsValidVariant(string variant) => VariantRegex().IsMatch(variant);

  /// A file ref for an absolute local path.
  public static Ref FromFile(string absolutePath, string variant = "") {
    Uri uri = new(absolutePath, UriKind.Absolute);
    return uri.IsFile
        ? new Ref(RefKind.File, uri.AbsoluteUri, "").WithVariant(variant)
        : throw new ArgumentException($"Not a local path: {absolutePath}", nameof(absolutePath));
  }

  /// Parses a source; allowFile also accepts local paths as file: URIs.
  public static Ref Parse(string s, bool allowFile) =>
      TryParse(s, allowFile, out Ref? r, out string? error)
          ? r
          : throw new FormatException($"Invalid ref '{s}': {error}");

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
          error = $"{gitError}; '@' in a spec ref must be followed by <version>, branch:<name>, "
              + "tag:<name> or rev:<commit>";
          return false;
        }
      }
    }

    int hash = head.IndexOf('#');
    string target = hash < 0 ? head : head[..hash];
    string variant = hash < 0 ? "" : head[(hash + 1)..];

    if (hash >= 0 && !IsValidVariant(variant)) {
      error = $"invalid variant '{variant}'";
      return false;
    }

    if (isUrl) {
      if (!IsUriText(target) || !Uri.TryCreate(target, UriKind.Absolute, out Uri? uri)
          || uri.Scheme != "https" || uri.Host == "") {
        error = "not an absolute https URL";
        return false;
      }

      result = new Ref(RefKind.Url, target, variant);
    } else if (isFile) {
      if (!allowFile) {
        error = "local paths are only allowed on the command line";
        return false;
      }

      if (!Uri.TryCreate(target, UriKind.Absolute, out Uri? uri) || !uri.IsFile) {
        error = "not an absolute file URI";
        return false;
      }

      result = new Ref(RefKind.File, uri.AbsoluteUri, variant);
    } else {
      if (Golang.Org.X.Mod.Module.CheckPath(target) is Exception ex) {
        error = $"not a valid module path ({ex.Message})";
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
