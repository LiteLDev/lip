using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Semver;

namespace Lip.Core.Mcmod;

/// A lip: URI (protocol/lip-uri.md): a link, such as one of a web page, that asks the
/// interface to install a remote repository. Parsing one installs nothing: it only says which
/// package the link names, which the user is then asked to confirm.
public sealed partial record LipUri {
  /// The scheme of the links, without the ':'.
  public const string Scheme = "lip";

  /// The only action: install a package.
  public const string InstallAction = "install";

  /// The longest link that is read; a longer one is invalid.
  public const int MaxLength = 2048;

  private static readonly UTF8Encoding _strictUtf8 = new(false, true);

  private LipUri(string action, string modulePath, string? version, string variant) {
    Action = action;
    ModulePath = modulePath;
    Version = version;
    Variant = variant;
  }

  /// The action, in lowercase: "install".
  public string Action { get; }

  /// The module path of the remote repository, decoded, its case as the link writes it.
  public string ModulePath { get; }

  /// The SemVer version to install, or null for the latest one.
  public string? Version { get; }

  /// The variant, or "" for the default variant.
  public string Variant { get; }

  /// The package argument that lip install is given for the link (protocol/lip.md, "Package
  /// arguments"): the module path, "#" and the variant unless it is the default, and "@" and
  /// the version when the link names one.
  public string PackageArgument =>
      ModulePath + (Variant == "" ? "" : "#" + Variant) + (Version is null ? "" : "@" + Version);

  public static LipUri Parse(string s) =>
      TryParse(s, out LipUri? uri, out string? error)
          ? uri
          : throw new FormatException($"The link '{s}' is not valid: {error}.");

  /// Parses a link; false with why, in English, when it is not a valid lip: URI.
  public static bool TryParse(
      string s,
      [NotNullWhen(true)] out LipUri? result,
      [NotNullWhen(false)] out string? error) {
    result = null;
    if (s.Length > MaxLength) {
      error = $"it is longer than {MaxLength} characters";
      return false;
    }

    if (!UriTextRegex().IsMatch(s)) {
      error = "it has characters that a URI does not allow (they must be percent-encoded)";
      return false;
    }

    string prefix = Scheme + "://";
    if (!s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
      error = $"it does not start with '{prefix}'";
      return false;
    }

    // The fragment is not read.
    string rest = s[prefix.Length..];
    int hash = rest.IndexOf('#');
    if (hash >= 0) {
      rest = rest[..hash];
    }

    string query = "";
    int question = rest.IndexOf('?');
    if (question >= 0) {
      query = rest[(question + 1)..];
      rest = rest[..question];
    }

    int slash = rest.IndexOf('/');
    string authority = slash < 0 ? rest : rest[..slash];
    string path = slash < 0 ? "" : rest[(slash + 1)..];
    string action = authority.ToLowerInvariant();
    if (action != InstallAction) {
      error = authority == ""
          ? "it names no action, such as lip://install/github.com/owner/repo"
          : $"'{authority}' is not an action of lip; the only one is '{InstallAction}'";
      return false;
    }

    // One '/' at the end is allowed, as a browser may add one.
    if (path.EndsWith('/')) {
      path = path[..^1];
    }

    if (path == "") {
      error = "it names no package, as in lip://install/github.com/owner/repo";
      return false;
    }

    if (Decode(path) is not string modulePath) {
      error = "its path is not percent-encoded UTF-8";
      return false;
    }

    if (Golang.Org.X.Mod.Module.CheckPath(modulePath) is Exception ex) {
      string reason = ex.InnerException?.Message ?? ex.Message;
      error = $"'{modulePath}' is not the path of a remote repository: {reason}; a remote"
          + " repository is written like github.com/owner/repo";
      return false;
    }

    string? version = null;
    string variant = "";
    bool versionSeen = false;
    bool variantSeen = false;
    foreach (string parameter in query.Split('&')) {
      if (parameter == "") {
        continue;
      }

      int equals = parameter.IndexOf('=');
      string? name = Decode(equals < 0 ? parameter : parameter[..equals]);
      string? value = Decode(equals < 0 ? "" : parameter[(equals + 1)..]);
      if (name is null || value is null) {
        error = "its query is not percent-encoded UTF-8";
        return false;
      }

      switch (name) {
        case "version":
          if (versionSeen) {
            error = "it gives the version more than once";
            return false;
          }

          versionSeen = true;
          if (value == "") {
            break;
          }

          if (!SemVersion.TryParse(value, SemVersionStyles.Strict, out _)) {
            error = value.Length > 1 && value[0] is 'v' or 'V'
                && SemVersion.TryParse(value[1..], SemVersionStyles.Strict, out _)
                    ? $"write the version without the leading 'v', as '{value[1..]}'"
                    : $"the version '{value}' is not a SemVer version such as 1.2.3; a link"
                        + " names no branch, tag or commit";
            return false;
          }

          version = value;
          break;
        case "variant":
          if (variantSeen) {
            error = "it gives the variant more than once";
            return false;
          }

          variantSeen = true;
          if (value != "" && !Ref.IsValidVariant(value)) {
            error = $"the variant '{value}' is not valid; {Ref.VariantRule}";
            return false;
          }

          variant = value;
          break;
      }
    }

    result = new LipUri(action, modulePath, version, variant);
    error = null;
    return true;
  }

  /// Decodes the percent-encoded octets of a URI component as UTF-8, or null when they are no
  /// valid UTF-8. '+' is itself, not a space: a version may have one.
  private static string? Decode(string component) {
    List<byte> bytes = [];
    for (int i = 0; i < component.Length; i++) {
      if (component[i] == '%') {
        bytes.Add(byte.Parse(
            component.AsSpan(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
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

  /// The characters of a URI (RFC 3986), with every '%' starting a percent-encoded octet.
  [GeneratedRegex(@"^(?:[A-Za-z0-9\-._~:/?#\[\]@!$&'()*+,;=]|%[0-9A-Fa-f]{2})*\z")]
  private static partial Regex UriTextRegex();
}
