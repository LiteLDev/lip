using System.Text;
using System.Text.RegularExpressions;

namespace Lip.Core.Mcmod;

/// A path pattern (lip.schema.toml "globs"), matched segment by segment with the POSIX pattern
/// matching notation as fnmatch applies it with FNM_PATHNAME and without FNM_PERIOD: '*' matches
/// any run of characters other than '/' (a leading '.' included), '?' one character other than
/// '/', and a bracket expression one character of its set ('[!...]' of the complement), with
/// ranges, character classes, collating symbols and equivalence classes of the POSIX locale; a
/// '[' that starts no complete bracket expression matches itself. There is no escape character.
/// A segment that is exactly "**" matches zero or more segments, and a pattern ending in '/'
/// matches directories together with every path below a matched directory.
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

  /// Throws FormatException when the pattern is not a glob.
  public PathGlob(string pattern) {
    string? reason = pattern switch {
      "" => "it is empty",
      _ when pattern.StartsWith('/') => "it starts with '/', but a glob is a relative path",
      _ when pattern.Contains('\\') =>
          "it contains '\\', but path segments are separated by '/' and there is no escape"
          + " character",
      _ => null,
    };
    if (reason is not null) {
      throw Invalid(pattern, reason);
    }

    Pattern = pattern;
    MatchesDirectories = pattern.EndsWith('/');
    _segments = (MatchesDirectories ? pattern[..^1] : pattern).Split('/');
    if (_segments.Any(seg => seg == "")) {
      throw Invalid(pattern, "it contains '//', an empty path segment");
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

  private static FormatException Invalid(string pattern, string reason) =>
      new($"The glob '{pattern}' is not valid: {reason}.");

  /// Whether a path contains '*', '?' or '[', which make it a pattern (lip.schema.toml "place").
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

  /// Whether the path is matched. A directory path must end with '/'. A directory pattern
  /// matches a path below a directory only through a directory of at least minSegments segments
  /// (e.g. 2 for a path below mods, which a pattern matching mods alone does not reach).
  public bool IsMatch(string path, int minSegments = 0) {
    bool isDirectory = path.EndsWith('/');
    string[] segments = (isDirectory ? path[..^1] : path).Split('/');

    if (!MatchesDirectories) {
      return !isDirectory && MatchSegments(0, segments, 0, segments.Length);
    }

    // A directory pattern matches a directory and everything below it. The last segment of a
    // file path is the file itself, never a directory. The empty prefix is the base directory,
    // which only a pattern of "**" segments matches (e.g. "**/" matches every path).
    int maxPrefix = isDirectory ? segments.Length : segments.Length - 1;
    for (int length = minSegments; length <= maxPrefix; length++) {
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
          int close = TranslateBracket(segment, i, regex, pattern);
          if (close < 0) {
            regex.Append(@"\[");
          } else {
            i = close;
          }

          break;
        default:
          regex.Append(Regex.Escape(c.ToString()));
          break;
      }
    }

    return regex.ToString();
  }

  /// Appends the character class of the bracket expression starting at segment[start] and
  /// returns the index of its closing ']', or returns -1 when there is none. Throws when a
  /// complete bracket expression is invalid, e.g. starts with '^'.
  private static int TranslateBracket(
      string segment, int start, StringBuilder regex, string pattern) {
    int i = start + 1;
    bool negated = false;
    string? error = null;
    if (i < segment.Length && segment[i] == '!') {
      negated = true;
      i++;
    } else if (i < segment.Length && segment[i] == '^') {
      error = "a bracket expression starts with '^'; write '[!...]' to match any character not"
          + " in a set";
    }

    StringBuilder set = new();
    bool first = true;
    for (; i < segment.Length; i++) {
      char c = segment[i];
      if (c == ']' && !first) {
        if (error is not null) {
          throw Invalid(pattern, error);
        }

        regex.Append(negated ? "[^/" : "[").Append(set).Append(']');
        return i;
      }

      first = false;
      if (c == '[' && i + 1 < segment.Length && segment[i + 1] is ':' or '.' or '=') {
        char kind = segment[i + 1];
        int close = segment.IndexOf($"{kind}]", i + 2, StringComparison.Ordinal);
        if (close < 0) {
          return -1;
        }

        string name = segment[(i + 2)..close];
        if (kind == ':') {
          if (_classes.TryGetValue(name, out string? members)) {
            set.Append(members);
          } else {
            error ??= $"'[:{name}:]' is not a character class (the classes are"
                + $" {string.Join(", ", _classes.Keys.Select(k => $"[:{k}:]"))})";
          }
        } else if (name.Length == 1) {
          // In the POSIX locale a collating symbol or an equivalence class is the character.
          set.Append(EscapeInClass(name[0]));
        } else {
          error ??= $"'[{kind}{name}{kind}]' names no character (only single characters are"
              + " collating elements)";
        }

        i = close + 1;
        continue;
      }

      if (i + 2 < segment.Length && segment[i + 1] == '-' && segment[i + 2] != ']') {
        char end = segment[i + 2];
        if (end < c) {
          error ??= $"the range '{c}-{end}' ends before it starts";
        }

        set.Append(EscapeInClass(c)).Append('-').Append(EscapeInClass(end));
        i += 2;
        continue;
      }

      set.Append(EscapeInClass(c));
    }

    return -1;
  }

  private static string EscapeInClass(char c) =>
      c is '\\' or ']' or '[' or '^' or '-' ? $"\\{c}" : c.ToString();
}
