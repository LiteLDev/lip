using System.Text.RegularExpressions;
using Semver;

namespace Lip.Core.Mcmod;

/// A version range of a manifest.json (spec §4.2 "range"): the syntax and matching semantics
/// of LeviLamina's ll::data::VersionRequirement, except that a bare full version without an
/// operator (e.g. "1.2.3") is rejected.
///
/// This is a port of src/ll/api/data/VersionRequirement.h (and the parts of
/// src/ll/api/data/Version.h it depends on) from LiteLDev/LeviLamina at commit
/// 5811462b56614841386fabd853a0c08af7eeb9e8 (VersionRequirement.h last changed in
/// 4c25b54e76f1b9c22abf6cc25587e2dbb2498133). Its grammar, as implemented there:
///
/// - The input is split at every "||" into alternatives (OR). An input that is empty or only
///   whitespace is one empty alternative, which, like "*", accepts every non-prerelease version.
///   An empty alternative between or around "||" is invalid.
/// - Each alternative is split at whitespace (' ', '\t', '\r', '\n' only) into tokens whose
///   comparators all must hold (AND).
/// - "A - B" (three tokens, the middle one exactly "-") is an inclusive hyphen range.
/// - An operator token alone (">", ">=", "<", "<=", "=", "^", "~") is joined with the next token.
/// - A comparator is an optional operator followed by a partial version: "1", "1.2", "1.2.3",
///   with "*", "x" or "X" allowed in place of trailing components. A partial version containing
///   '-' or '+' must be a full SemVer version. Numbers are 0–65535 without leading zeros.
/// - Only a bare or "=" comparator may consist solely of wildcards ("*", "=x.x"); it adds no
///   constraint.
/// - Build metadata in a range is ignored, and comparisons use SemVer precedence.
/// - A prerelease candidate matches an alternative only when one of that alternative's
///   comparators has a prerelease on the same major.minor.patch.
///
/// Quirks kept on purpose so that lip and LeviLamina agree on every range: a lone operator is
/// joined with whatever token follows ("> =1.0.0" is ">=1.0.0"); an upper bound that would
/// exceed 65535 is dropped ("^65535.1.2" has no upper bound), except for '>' and '&lt;=' on a
/// partial version, which then fail to parse ("&lt;=65535"); an inverted hyphen range parses and
/// matches nothing.
///
/// The one deviation required by the spec: LeviLamina reads a bare full version ("1.2.3",
/// "1.2.3-beta") as the legacy "&gt;=1.2.3 &lt;2.0.0"; here it is a FormatException. Bare partial
/// versions ("1", "1.2", "1.x", "*") are not full versions and keep their meaning, as do the
/// full versions on either side of a hyphen range, which are not comparators.
public partial class VersionRange : IVersionConstraint {
  private const int MaxNumber = ushort.MaxValue;

  private static readonly string[] _operatorTokens = [">", ">=", "<", "<=", "=", "^", "~"];

  private readonly List<List<Comparator>> _alternatives;

  private VersionRange(string text, List<List<Comparator>> alternatives) {
    Text = text;
    _alternatives = alternatives;
  }

  private enum Operator {
    Equal,
    Greater,
    GreaterEqual,
    Less,
    LessEqual,
  }

  private enum Prefix {
    Bare,
    Equal,
    Greater,
    GreaterEqual,
    Less,
    LessEqual,
    Caret,
    Tilde,
  }

  /// The range exactly as written.
  public string Text { get; }

  public override string ToString() => Text;

  public static VersionRange Parse(string s) {
    ArgumentNullException.ThrowIfNull(s);
    List<List<Comparator>> alternatives = [];

    if (Trim(s) == "") {
      alternatives.Add([]);
      return new VersionRange(s, alternatives);
    }

    int begin = 0;
    while (true) {
      int end = s.IndexOf("||", begin, StringComparison.Ordinal);
      if (end < 0) {
        end = s.Length;
      }

      string group = Trim(s[begin..end]);
      if (group == "") {
        throw Invalid(s, "empty alternative");
      }

      List<string> tokens = Tokenize(group);
      List<Comparator> set = [];
      for (int i = 0; i < tokens.Count;) {
        if (i + 2 < tokens.Count && tokens[i + 1] == "-") {
          ExpandHyphen(s, set, tokens[i], tokens[i + 2]);
          i += 3;
          continue;
        }

        string token = tokens[i];
        if (_operatorTokens.Contains(token) && i + 1 < tokens.Count) {
          token += tokens[++i];
        }

        ExpandComparator(s, set, token);
        ++i;
      }

      alternatives.Add(set);
      if (end == s.Length) {
        break;
      }

      begin = end + 2;
    }

    return new VersionRange(s, alternatives);
  }

  public bool IsSatisfiedBy(SemVersion version) => _alternatives.Any(
      set => AllowsPrerelease(set, version) && set.All(c => c.IsSatisfiedBy(version)));

  private static bool AllowsPrerelease(List<Comparator> set, SemVersion candidate) =>
      !candidate.IsPrerelease || set.Any(c => c.Version.IsPrerelease
          && c.Version.Major == candidate.Major
          && c.Version.Minor == candidate.Minor
          && c.Version.Patch == candidate.Patch);

  private static void ExpandComparator(string s, List<Comparator> set, string token) {
    string body = token;
    Prefix prefix = Prefix.Bare;
    if (body.StartsWith(">=", StringComparison.Ordinal)) {
      (prefix, body) = (Prefix.GreaterEqual, body[2..]);
    } else if (body.StartsWith("<=", StringComparison.Ordinal)) {
      (prefix, body) = (Prefix.LessEqual, body[2..]);
    } else if (body.StartsWith('>')) {
      (prefix, body) = (Prefix.Greater, body[1..]);
    } else if (body.StartsWith('<')) {
      (prefix, body) = (Prefix.Less, body[1..]);
    } else if (body.StartsWith('=')) {
      (prefix, body) = (Prefix.Equal, body[1..]);
    } else if (body.StartsWith('^')) {
      (prefix, body) = (Prefix.Caret, body[1..]);
    } else if (body.StartsWith('~')) {
      (prefix, body) = (Prefix.Tilde, body[1..]);
    }

    if (body == "" || ParsePartial(body) is not Partial partial) {
      throw Invalid(s, $"invalid comparator '{token}'");
    }

    if (partial.Major is null) {
      if (prefix is Prefix.Bare or Prefix.Equal) {
        return;
      }

      throw Invalid(s, $"operator without a version number in '{token}'");
    }

    // The spec's single deviation from VersionRequirement, which reads a bare full version as
    // ">=X <nextMajor(X)".
    if (prefix == Prefix.Bare && partial.Exact is not null) {
      string legacy = NextMajor(partial.Exact) is SemVersion next
          ? $">={partial.Exact} <{next}"
          : $">={partial.Exact}";
      throw Invalid(s, $"bare full version '{token}' is not allowed; write '={token}' for an "
          + $"exact match or '{legacy}' for LeviLamina's legacy meaning");
    }

    SemVersion lower = partial.Floor;
    switch (prefix) {
      case Prefix.Bare:
      case Prefix.Equal:
        ExpandPartial(set, partial);
        return;

      case Prefix.GreaterEqual:
        Add(set, Operator.GreaterEqual, partial.Exact ?? lower);
        return;

      case Prefix.Greater:
        if (partial.Exact is not null) {
          Add(set, Operator.Greater, partial.Exact);
        } else {
          SemVersion upper = (partial.Minor is null ? NextMajor(lower) : NextMinor(lower))
              ?? throw Invalid(s, $"no version is greater than '{token}'");
          Add(set, Operator.GreaterEqual, upper);
        }

        return;

      case Prefix.Less:
        Add(set, Operator.Less, partial.Exact ?? lower);
        return;

      case Prefix.LessEqual:
        if (partial.Exact is not null) {
          Add(set, Operator.LessEqual, partial.Exact);
        } else {
          SemVersion upper = (partial.Minor is null ? NextMajor(lower) : NextMinor(lower))
              ?? throw Invalid(s, $"no upper bound for '{token}'");
          Add(set, Operator.Less, upper);
        }

        return;

      case Prefix.Caret:
        Add(set, Operator.GreaterEqual, partial.Exact ?? lower);
        if (partial.Minor is null || lower.Major != 0) {
          AddUpperBound(set, NextMajor(lower));
        } else if (partial.Patch is null || lower.Minor != 0) {
          AddUpperBound(set, NextMinor(lower));
        } else {
          AddUpperBound(set, NextPatch(lower));
        }

        return;

      case Prefix.Tilde:
        Add(set, Operator.GreaterEqual, partial.Exact ?? lower);
        AddUpperBound(set, partial.Minor is null ? NextMajor(lower) : NextMinor(lower));
        return;
    }
  }

  private static void ExpandHyphen(
      string s, List<Comparator> set, string lowerText, string upperText) {
    if (ParsePartial(lowerText) is not { Major: not null } lowerPartial
        || ParsePartial(upperText) is not { Major: not null } upperPartial) {
      throw Invalid(s, $"invalid hyphen range '{lowerText} - {upperText}'");
    }

    Add(set, Operator.GreaterEqual, lowerPartial.Exact ?? lowerPartial.Floor);
    SemVersion upper = upperPartial.Floor;
    if (upperPartial.Exact is not null) {
      Add(set, Operator.LessEqual, upperPartial.Exact);
    } else if (upperPartial.Minor is null) {
      AddUpperBound(set, NextMajor(upper));
    } else {
      AddUpperBound(set, NextMinor(upper));
    }
  }

  private static void ExpandPartial(List<Comparator> set, Partial partial) {
    if (partial.Exact is not null) {
      Add(set, Operator.Equal, partial.Exact);
      return;
    }

    SemVersion lower = partial.Floor;
    Add(set, Operator.GreaterEqual, lower);
    AddUpperBound(set, partial.Minor is null ? NextMajor(lower) : NextMinor(lower));
  }

  /// Returns null when the text is not a partial version.
  private static Partial? ParsePartial(string value) {
    value = Trim(value);
    if (value == "") {
      return null;
    }

    if (value.AsSpan().IndexOfAny('-', '+') >= 0) {
      return ParseFullVersion(value) is SemVersion full
          ? new Partial((int)full.Major, (int)full.Minor, (int)full.Patch, full)
          : null;
    }

    string[] parts = value.Split('.');
    if (parts.Length > 3) {
      return null;
    }

    int?[] numbers = new int?[3];
    bool wildcardFound = false;
    for (int i = 0; i < parts.Length; ++i) {
      if (parts[i] is "*" or "x" or "X") {
        wildcardFound = true;
        continue;
      }

      if (wildcardFound || ParseNumber(parts[i]) is not int number) {
        return null;
      }

      numbers[i] = number;
    }

    SemVersion? exact = numbers[2] is int patch
        ? new SemVersion(numbers[0]!.Value, numbers[1]!.Value, patch)
        : null;
    return new Partial(numbers[0], numbers[1], numbers[2], exact);
  }

  /// LeviLamina's ll::data::Version grammar: strict SemVer 2.0.0 whose major, minor, patch and
  /// numeric prerelease identifiers fit in uint16. Build metadata is dropped.
  private static SemVersion? ParseFullVersion(string value) {
    Match m = FullVersionRegex().Match(value);
    if (!m.Success
        || ParseNumber(m.Groups["major"].Value) is not int major
        || ParseNumber(m.Groups["minor"].Value) is not int minor
        || ParseNumber(m.Groups["patch"].Value) is not int patch) {
      return null;
    }

    string[] prerelease = m.Groups["pre"].Success ? m.Groups["pre"].Value.Split('.') : [];
    if (prerelease.Any(id => id.All(char.IsAsciiDigit) && ParseNumber(id) is null)) {
      return null;
    }

    return new SemVersion(major, minor, patch, prerelease, []);
  }

  /// A decimal number without leading zeros that fits in uint16, or null.
  private static int? ParseNumber(string value) {
    if (value == "" || value.Length > 5 || (value.Length > 1 && value[0] == '0')
        || !value.All(char.IsAsciiDigit)) {
      return null;
    }

    int number = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
    return number <= MaxNumber ? number : null;
  }

  private static SemVersion? NextMajor(SemVersion v) =>
      v.Major == MaxNumber ? null : new SemVersion(v.Major + 1, 0, 0);

  private static SemVersion? NextMinor(SemVersion v) =>
      v.Minor == MaxNumber ? NextMajor(v) : new SemVersion(v.Major, v.Minor + 1, 0);

  private static SemVersion? NextPatch(SemVersion v) =>
      v.Patch == MaxNumber ? NextMinor(v) : new SemVersion(v.Major, v.Minor, v.Patch + 1);

  private static void Add(List<Comparator> set, Operator op, SemVersion version) =>
      set.Add(new Comparator(op, version.WithoutMetadata()));

  private static void AddUpperBound(List<Comparator> set, SemVersion? upper) {
    if (upper is not null) {
      Add(set, Operator.Less, upper);
    }
  }

  private static bool IsWhitespace(char c) => c is ' ' or '\t' or '\r' or '\n';

  private static string Trim(string value) {
    int start = 0;
    int end = value.Length;
    while (start < end && IsWhitespace(value[start])) {
      ++start;
    }

    while (end > start && IsWhitespace(value[end - 1])) {
      --end;
    }

    return value[start..end];
  }

  private static List<string> Tokenize(string value) {
    List<string> tokens = [];
    int current = 0;
    while (current < value.Length) {
      while (current < value.Length && IsWhitespace(value[current])) {
        ++current;
      }

      int begin = current;
      while (current < value.Length && !IsWhitespace(value[current])) {
        ++current;
      }

      if (current > begin) {
        tokens.Add(value[begin..current]);
      }
    }

    return tokens;
  }

  private static FormatException Invalid(string s, string reason) =>
      new($"Invalid version range '{s}': {reason}");

  [GeneratedRegex(
      @"^(?<major>[0-9]+)\.(?<minor>[0-9]+)\.(?<patch>[0-9]+)"
          + @"(?:-(?<pre>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?"
          + @"(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?\z",
      RegexOptions.CultureInvariant)]
  private static partial Regex FullVersionRegex();

  /// A partially written version; Exact is set when all three numbers are written.
  private sealed record Partial(int? Major, int? Minor, int? Patch, SemVersion? Exact) {
    public SemVersion Floor => new(Major ?? 0, Minor ?? 0, Patch ?? 0);
  }

  private readonly record struct Comparator(Operator Op, SemVersion Version) {
    public bool IsSatisfiedBy(SemVersion candidate) {
      int c = SemVersion.ComparePrecedence(candidate, Version);
      return Op switch {
        Operator.Equal => c == 0,
        Operator.Greater => c > 0,
        Operator.GreaterEqual => c >= 0,
        Operator.Less => c < 0,
        Operator.LessEqual => c <= 0,
        _ => false,
      };
    }
  }
}

/// Adapts a tooth.json v3 range (Semver's SemVersionRange) for §9.3.
public class SemVersionRangeConstraint(SemVersionRange range) : IVersionConstraint {
  public SemVersionRange Range { get; } = range;

  public bool IsSatisfiedBy(SemVersion version) => Range.Contains(version);

  public override string ToString() => Range.ToString();
}

/// Accepts exactly one version, compared by SemVer precedence (build metadata is ignored). Used
/// for "path@version" arguments and to look up one version of a ref.
public sealed record ExactVersionConstraint(SemVersion Version) : IVersionConstraint {
  public bool IsSatisfiedBy(SemVersion version) =>
      SemVersion.PrecedenceComparer.Compare(Version, version) == 0;

  public override string ToString() => $"={Version}";
}
