using System.Text.RegularExpressions;

namespace Lip.Core.Mcmod;

/// A path pattern (spec §4.2): '*' matches any run of characters other than '/', a segment
/// that is exactly "**" matches zero or more segments, and a pattern ending in '/' matches
/// directories together with every path below a matched directory. There are no other
/// metacharacters.
///
/// Paths are '/'-separated and relative; a directory path is written with a trailing '/'.
public class PathGlob {
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

    _segmentRegexes = [.. _segments.Select(seg => seg == "**" || !seg.Contains('*')
        ? null
        : new Regex(
            "^" + Regex.Escape(seg).Replace(@"\*", "[^/]*") + "$",
            RegexOptions.CultureInvariant))];
  }

  public string Pattern { get; }

  public bool MatchesDirectories { get; }

  public override string ToString() => Pattern;

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
}
