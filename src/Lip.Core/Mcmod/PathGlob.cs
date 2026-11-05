using System.Text;
using System.Text.RegularExpressions;

namespace Lip.Core.Mcmod;

/// A path pattern (tooth.schema.json "globs"), matched segment by segment with the POSIX pattern matching
/// notation as fnmatch applies it with FNM_PATHNAME and without FNM_PERIOD: '*' matches any run of
/// characters other than '/' (a leading '.' included), '?' one character other than '/', and a
/// bracket expression one character of its set ('[!...]' of the complement), with ranges,
/// character classes, collating symbols and equivalence classes of the POSIX locale. There is no
/// escape character. A segment that is exactly "**" matches zero or more segments, and a pattern
/// ending in '/' matches directories together with every path below a matched directory.
///
/// Paths are '/'-separated and relative; a directory path is written with a trailing '/'.
public class PathGlob {
  private static readonly Dictionary<string, string> _classes = new(StringComparer.Ordinal) {
    ["alnum"] = "A-Za-z0-9",
    ["alpha"] = "A-Za-z",
    ["blank"] = @" \t",
    ["cntrl"] = @"\x00-\x1F\x7F",
    ["digit"] = "0-9",
    ["graph"] = @"\x21-\x7E",
    ["lower"] = "a-z",
    ["print"] = @"\x20-\x7E",
    ["punct"] = @"!-/:-@\[-`{-~",
    ["space"] = @" \t\n\v\f\r",
    ["upper"] = "A-Z",
    ["xdigit"] = "0-9A-Fa-f",
  };

  private readonly string[] _segments;
  private readonly Regex?[] _segmentRegexes;

  public PathGlob(string pattern) {
    if (pattern == "" || pattern.StartsWith('/') || pattern.Contains('\\')) {
      throw new FormatException($"Invalid glob: '{pattern}'");
    }

    Pattern = pattern;
    MatchesDirectories = pattern.EndsWith('/');
    _segments = (MatchesDirectories ? pattern[..^1] : pattern).Split('/');
    if (_segments.Any(seg => seg == "")) {
      throw new FormatException($"Invalid glob: '{pattern}'");
    }

    _segmentRegexes = [.. _segments.Select(seg => seg == "**" || !HasMetacharacters(seg)
        ? null
        : new Regex(
            "^" + Translate(seg, pattern) + @"\z",
            RegexOptions.CultureInvariant))];
  }

  public string Pattern { get; }

  public bool MatchesDirectories { get; }

  public override string ToString() => Pattern;

  /// Whether a path contains '*', '?' or '[', which make it a pattern (tooth.schema.json "placements").
  public static bool HasMetacharacters(string path) => path.AsSpan().IndexOfAny("*?[") >= 0;

  /// The leading segments of a pattern that contain no metacharacter, each followed by '/'
  /// (protocol/workspace.md §4 step 2): "" when the first segment is a pattern.
  public static string FixedPrefix(string pattern) {
    string[] segments = pattern.Split('/');
    StringBuilder prefix = new();
    for (int i = 0; i < segments.Length - 1 && !HasMetacharacters(segments[i]); i++) {
      prefix.Append(segments[i]).Append('/');
    }

    return prefix.ToString();
  }

  /// Whether the path is matched. A directory path must end with '/'.
  public bool IsMatch(string path) {
    bool isDirectory = path.EndsWith('/');
    string[] segments = (isDirectory ? path[..^1] : path).Split('/');

    if (!MatchesDirectories) {
      return !isDirectory && MatchSegments(0, segments, 0, segments.Length);
    }

    // A directory pattern matches a directory and everything below it. The last segment of a
    // file path is the file itself, never a directory. The empty prefix is the base directory,
    // which only a pattern of "**" segments matches (e.g. "**/" matches every path).
    int maxPrefix = isDirectory ? segments.Length : segments.Length - 1;
    for (int length = 0; length <= maxPrefix; length++) {
      if (MatchSegments(0, segments, 0, length)) {
        return true;
      }
    }
    return false;
  }

  private bool MatchSegments(int p, string[] path, int i, int end) {
    if (p == _segments.Length) {
      return i == end;
    }

    if (_segments[p] == "**") {
      for (int k = i; k <= end; k++) {
        if (MatchSegments(p + 1, path, k, end)) {
          return true;
        }
      }
      return false;
    }

    if (i == end) {
      return false;
    }

    bool segmentMatches = _segmentRegexes[p] is Regex regex
        ? regex.IsMatch(path[i])
        : _segments[p] == path[i];

    return segmentMatches && MatchSegments(p + 1, path, i + 1, end);
  }

  /// The .NET regular expression of one pattern segment (which contains no '/').
  private static string Translate(string segment, string pattern) {
    StringBuilder regex = new();
    for (int i = 0; i < segment.Length; i++) {
      char c = segment[i];
      switch (c) {
        case '*':
          regex.Append("[^/]*");
          break;
        case '?':
          regex.Append("[^/]");
          break;
        case '[':
          i = TranslateBracket(segment, i, regex, pattern);
          break;
        default:
          regex.Append(Regex.Escape(c.ToString()));
          break;
      }
    }

    return regex.ToString();
  }

  /// Appends the character class of the bracket expression starting at segment[start] and
  /// returns the index of its closing ']'. Throws when it is incomplete or starts with '^'.
  private static int TranslateBracket(
      string segment, int start, StringBuilder regex, string pattern) {
    int i = start + 1;
    bool negated = false;
    if (i < segment.Length && segment[i] == '!') {
      negated = true;
      i++;
    } else if (i < segment.Length && segment[i] == '^') {
      throw new FormatException($"Invalid glob: '{pattern}' (a bracket expression starts with '^')");
    }

    StringBuilder set = new();
    bool first = true;
    for (; i < segment.Length; i++) {
      char c = segment[i];
      if (c == ']' && !first) {
        regex.Append(negated ? "[^/" : "[").Append(set).Append(']');
        return i;
      }

      first = false;
      if (c == '[' && i + 1 < segment.Length && segment[i + 1] is ':' or '.' or '=') {
        char kind = segment[i + 1];
        int close = segment.IndexOf($"{kind}]", i + 2, StringComparison.Ordinal);
        if (close < 0) {
          break;
        }

        string name = segment[(i + 2)..close];
        if (kind == ':') {
          set.Append(_classes.TryGetValue(name, out string? members)
              ? members
              : throw new FormatException(
                  $"Invalid glob: '{pattern}' (unknown character class '{name}')"));
        } else if (name.Length == 1) {
          // In the POSIX locale a collating symbol or an equivalence class is the character.
          set.Append(EscapeInClass(name[0]));
        } else {
          throw new FormatException(
              $"Invalid glob: '{pattern}' (unknown collating element '{name}')");
        }

        i = close + 1;
        continue;
      }

      if (i + 2 < segment.Length && segment[i + 1] == '-' && segment[i + 2] != ']') {
        char end = segment[i + 2];
        if (end < c) {
          throw new FormatException($"Invalid glob: '{pattern}' (invalid range '{c}-{end}')");
        }

        set.Append(EscapeInClass(c)).Append('-').Append(EscapeInClass(end));
        i += 2;
        continue;
      }

      set.Append(EscapeInClass(c));
    }

    throw new FormatException($"Invalid glob: '{pattern}' (incomplete bracket expression)");
  }

  private static string EscapeInClass(char c) =>
      c is '\\' or ']' or '[' or '^' or '-' ? $"\\{c}" : c.ToString();
}
