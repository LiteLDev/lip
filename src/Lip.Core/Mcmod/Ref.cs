using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Lip.Core.Mcmod;

public enum RefKind {
  /// "./path" inside the archive of the referring document.
  Relative,

  /// "https://..." pointing to a ZIP archive.
  Url,

  /// A Go module path; its "v" + SemVer tags are the versions, unless a git ref pins it.
  Spec,

  /// "file:///..." naming a local ZIP file. Only user requests (and the lock entries they
  /// produce) have file refs; documents must not contain them (spec §3).
  File,
}

public enum GitRefKind {
  Branch,
  Tag,
  Rev,
}

/// A git ref pinning a spec ref (spec §3): "branch:" name, "tag:" name or "rev:" commit id.
public sealed partial record GitRef {
  private GitRef(GitRefKind kind, string name) {
    Kind = kind;
    Name = name;
  }

  public GitRefKind Kind { get; }

  /// The branch or tag name, or the full lowercase commit id of a rev.
  public string Name { get; }

  public override string ToString() => $"{Prefix(Kind)}{Name}";

  public static bool TryParse(
      string s,
      [NotNullWhen(true)] out GitRef? result,
      [NotNullWhen(false)] out string? error) {
    result = null;
    foreach (GitRefKind kind in Enum.GetValues<GitRefKind>()) {
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

    error = $"'{s}' is not branch:<name>, tag:<name> or rev:<commit>";
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
    GitRefKind.Branch => "branch:",
    GitRefKind.Tag => "tag:",
    _ => "rev:",
  };

  [GeneratedRegex("^[0-9a-fA-F]{40}$")]
  private static partial Regex CommitIdRegex();
}

/// A reference to a package location (spec §3): target [ "#" variant ], where a spec target may
/// be followed by [ "@" git-ref ] after the variant.
public partial record Ref {
  private const string _fileScheme = "file:";

  private Ref(RefKind kind, string target, string variant, GitRef? gitRef = null) {
    Kind = kind;
    Target = target;
    Variant = variant;
    GitRef = gitRef;
  }

  public RefKind Kind { get; }

  /// The location without the variant and the git ref: the relative path or URL exactly as
  /// written, the module path of a spec ref, or the absolute file URI of a file ref.
  public string Target { get; }

  /// The part after the "#", or "" when there is none.
  public string Variant { get; }

  /// The git ref pinning a spec ref, or null when the spec ref selects by tag version.
  public GitRef? GitRef { get; }

  /// For relative refs, the path without the leading "./" and without a trailing '/'.
  public string RelativePath => Kind == RefKind.Relative
      ? Target[2..].TrimEnd('/')
      : throw new InvalidOperationException($"Not a relative ref: {this}");

  /// For relative refs, whether the ref points to a directory (written with a trailing '/').
  public bool IsDirectory => Kind == RefKind.Relative && Target.EndsWith('/');

  /// For file refs, the absolute local path.
  public string LocalPath => Kind == RefKind.File
      ? new Uri(Target).LocalPath
      : throw new InvalidOperationException($"Not a file ref: {this}");

  public Ref WithVariant(string variant) => variant == "" || IsValidVariant(variant)
      ? new Ref(Kind, Target, variant, GitRef)
      : throw new FormatException($"Invalid variant: {variant}");

  public Ref WithGitRef(GitRef? gitRef) => Kind == RefKind.Spec || gitRef is null
      ? new Ref(Kind, Target, Variant, gitRef)
      : throw new InvalidOperationException($"Only spec refs take a git ref: {this}");

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

  /// Parses a ref of a document. File refs are rejected.
  public static Ref Parse(string s) => Parse(s, allowFile: false);

  /// Parses a ref; allowFile also accepts file refs, which only user requests and the lock have.
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

    bool isRelative = s.StartsWith("./", StringComparison.Ordinal);
    bool isUrl = s.StartsWith("https://", StringComparison.Ordinal);
    bool isFile = s.StartsWith(_fileScheme, StringComparison.OrdinalIgnoreCase);

    // Module paths, variants and URLs before the fragment contain no '@' that starts a git ref;
    // everything after the first '@' of a spec ref is its git ref, which may contain '#'.
    string head = s;
    GitRef? gitRef = null;
    if (!isRelative && !isUrl && !isFile) {
      int at = s.IndexOf('@');
      if (at >= 0) {
        head = s[..at];
        if (!GitRef.TryParse(s[(at + 1)..], out gitRef, out string? gitError)) {
          error = $"{gitError}; '@' in a spec ref must be followed by branch:<name>, tag:<name> "
              + "or rev:<commit> (a version range belongs in the version key)";
          return false;
        }
      }
    } else if (GitRefSuffixRegex().IsMatch(s)) {
      error = "only spec refs may have a git ref (@branch:, @tag: or @rev:)";
      return false;
    }

    int hash = head.IndexOf('#');
    string target = hash < 0 ? head : head[..hash];
    string variant = hash < 0 ? "" : head[(hash + 1)..];

    if (hash >= 0 && !IsValidVariant(variant)) {
      error = $"invalid variant '{variant}'";
      return false;
    }

    if (isRelative) {
      string path = target[2..];
      if (path.EndsWith('/')) {
        path = path[..^1];
      }

      if (path == "" || path.Contains('\\')
          || path.Split('/').Any(seg => seg is "" or "." or "..")) {
        error = "relative path must be non-empty and must not contain empty, '.' or '..' "
            + "segments or '\\'";
        return false;
      }

      result = new Ref(RefKind.Relative, target, variant);
    } else if (isUrl) {
      if (!Uri.TryCreate(target, UriKind.Absolute, out Uri? uri) || uri.Scheme != "https"
          || uri.Host == "") {
        error = "not an absolute https URL";
        return false;
      }

      result = new Ref(RefKind.Url, target, variant);
    } else if (isFile) {
      if (!allowFile) {
        error = "file refs are only allowed on the command line";
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

  [GeneratedRegex(@"^[a-z0-9_]+(/[a-z0-9_]+)?$")]
  private static partial Regex VariantRegex();

  [GeneratedRegex("@(branch|tag|rev):")]
  private static partial Regex GitRefSuffixRegex();
}
