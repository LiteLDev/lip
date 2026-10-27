using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Lip.Core.Mcmod;

public enum RefKind {
  /// "./path" inside the archive of the referring document.
  Relative,

  /// "https://..." pointing to a ZIP archive.
  Url,

  /// A Go module path; its "v" + SemVer tags are the versions.
  Spec,
}

/// A reference to a package location (spec §3): target [ "#" variant ].
public partial record Ref {
  private Ref(RefKind kind, string target, string variant) {
    Kind = kind;
    Target = target;
    Variant = variant;
  }

  public RefKind Kind { get; }

  /// The part before the first '#', exactly as written.
  public string Target { get; }

  /// The part after the first '#', or "" when there is none.
  public string Variant { get; }

  /// For relative refs, the path without the leading "./" and without a trailing '/'.
  public string RelativePath => Kind == RefKind.Relative
      ? Target[2..].TrimEnd('/')
      : throw new InvalidOperationException($"Not a relative ref: {this}");

  /// For relative refs, whether the ref points to a directory (written with a trailing '/').
  public bool IsDirectory => Kind == RefKind.Relative && Target.EndsWith('/');

  public Ref WithVariant(string variant) => variant == "" || IsValidVariant(variant)
      ? new Ref(Kind, Target, variant)
      : throw new FormatException($"Invalid variant: {variant}");

  public override string ToString() => Variant == "" ? Target : $"{Target}#{Variant}";

  public static bool IsValidVariant(string variant) => VariantRegex().IsMatch(variant);

  public static Ref Parse(string s) => TryParse(s, out Ref? r, out string? error)
      ? r
      : throw new FormatException($"Invalid ref '{s}': {error}");

  public static bool TryParse(
      string s,
      [NotNullWhen(true)] out Ref? result,
      [NotNullWhen(false)] out string? error) {
    result = null;

    int hash = s.IndexOf('#');
    string target = hash < 0 ? s : s[..hash];
    string variant = hash < 0 ? "" : s[(hash + 1)..];

    if (hash >= 0 && !IsValidVariant(variant)) {
      error = $"invalid variant '{variant}'";
      return false;
    }

    if (target.StartsWith("./", StringComparison.Ordinal)) {
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
    } else if (target.StartsWith("https://", StringComparison.Ordinal)) {
      if (!Uri.TryCreate(target, UriKind.Absolute, out Uri? uri) || uri.Scheme != "https"
          || uri.Host == "") {
        error = "not an absolute https URL";
        return false;
      }

      result = new Ref(RefKind.Url, target, variant);
    } else {
      if (Golang.Org.X.Mod.Module.CheckPath(target) is Exception ex) {
        error = $"not a valid module path ({ex.Message})";
        return false;
      }

      result = new Ref(RefKind.Spec, target, variant);
    }

    error = null;
    return true;
  }

  [GeneratedRegex(@"^[a-z0-9_]+(/[a-z0-9_]+)?$")]
  private static partial Regex VariantRegex();
}
