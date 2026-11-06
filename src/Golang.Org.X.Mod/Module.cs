namespace Golang.Org.X.Mod;

/// golang.org/x/mod/module: CheckPath and what it needs.
public static class Module {
  public class InvalidPathError(string kind, string path, Exception innerException)
      : Exception($"malformed {kind} path {path}", innerException) {
    public string Kind { get; private init; } = kind;
    public string Path { get; private init; } = path;
  }

  public static Exception? CheckPath(string path) {
    InvalidPathError MakeError(Exception exception) {
      return new InvalidPathError("module", path, exception);
    }

    Exception? err = ModuleInternal.CheckPath(path);
    if (err is not null) {
      return MakeError(err);
    }
    int i = path.IndexOf('/');
    if (i < 0) {
      i = path.Length;
    }
    if (i == 0) {
      return MakeError(new Exception("leading slash"));
    }
    if (!path[..i].Contains('.')) {
      return MakeError(new Exception("missing dot in first path element"));
    }
    if (path[0] == '-') {
      return MakeError(new Exception("leading dash in first path element"));
    }
    for (int j = 0; j < i; j++) {
      if (!ModuleInternal.FirstPathOK(path[j])) {
        return new InvalidPathError("module", path, new Exception($"invalid char '{path[j]}' in first path element"));
      }
    }
    (string, string, bool) split = SplitPathVersion(path);
    if (!split.Item3) {
      return new InvalidPathError("module", path, new Exception("invalid version"));
    }
    return null;
  }

  public static (string, string, bool) SplitPathVersion(string path) {
    if (path.StartsWith("gopkg.in/")) {
      return ModuleInternal.SplitGopkgIn(path);
    }

    int i = path.Length;
    bool dot = false;
    while (i > 0 && ((path[i - 1] >= '0' && path[i - 1] <= '9') || path[i - 1] == '.')) {
      if (path[i - 1] == '.') {
        dot = true;
      }
      i--;
    }
    if (i <= 1 || i == path.Length || path[i - 1] != 'v' || path[i - 2] != '/') {
      return (path, "", true);
    }
    string prefix = path[..(i - 2)];
    string pathMajor = path[(i - 2)..];
    if (dot || pathMajor.Length <= 2 || pathMajor[2] == '0' || pathMajor == "/v1") {
      return (path, "", false);
    }
    return (prefix, pathMajor, true);
  }
}

file static class ModuleInternal {
  private static readonly string[] _badWindowsNames = [
      "CON",
        "PRN",
        "AUX",
        "NUL",
        "COM1",
        "COM2",
        "COM3",
        "COM4",
        "COM5",
        "COM6",
        "COM7",
        "COM8",
        "COM9",
        "LPT1",
        "LPT2",
        "LPT3",
        "LPT4",
        "LPT5",
        "LPT6",
        "LPT7",
        "LPT8",
        "LPT9",
    ];

  /// checkElem of a module path element.
  public static Exception? CheckElem(string elem) {
    if (string.IsNullOrEmpty(elem)) {
      return new Exception("empty path element");
    }

    if (elem.Count(c => c == '.') == elem.Length) {
      return new Exception($"invalid path element \"{elem}\"");
    }

    if (elem[0] == '.') {
      return new Exception("leading dot in path element");
    }

    if (elem[^1] == '.') {
      return new Exception("trailing dot in path element");
    }

    foreach (char r in elem) {
      if (!ModPathOK(r)) {
        return new Exception($"invalid char \"{r}\"");
      }
    }

    string shortElem = elem;
    int dotIndex = shortElem.IndexOf('.');
    if (dotIndex >= 0) {
      shortElem = shortElem[..dotIndex];
    }

    foreach (string bad in _badWindowsNames) {
      if (string.Equals(bad, shortElem, StringComparison.OrdinalIgnoreCase)) {
        return new Exception($"\"{shortElem}\" disallowed as path element component on Windows");
      }
    }

    int tilde = shortElem.LastIndexOf('~');
    if (tilde >= 0 && tilde < shortElem.Length - 1) {
      string suffix = shortElem[(tilde + 1)..];
      bool suffixIsDigits = true;
      foreach (char c in suffix) {
        if (c < '0' || c > '9') {
          suffixIsDigits = false;
          break;
        }
      }
      if (suffixIsDigits) {
        return new Exception("trailing tilde and digits in path element");
      }
    }
    return null;
  }

  /// checkPath of a module path. A string that is not valid UTF-8 in Go (a lone surrogate here)
  /// has a non-ASCII char, which ModPathOK rejects.
  public static Exception? CheckPath(string path) {
    if (path == "") {
      return new Exception("empty string");
    }
    if (path[0] == '-') {
      return new Exception("leading dash");
    }
    if (path.Contains("//")) {
      return new Exception("double slash");
    }
    if (path[^1] == '/') {
      return new Exception("trailing slash");
    }
    int elemStart = 0;
    for (int i = 0; i < path.Length; i++) {
      if (path[i] == '/') {
        Exception? err = CheckElem(path[elemStart..i]);
        if (err is not null) {
          return err;
        }
        elemStart = i + 1;
      }
    }
    return CheckElem(path[elemStart..]);
  }

  public static bool FirstPathOK(char r) {
    return r == '-'
           || r == '.'
           || '0' <= r
           && r <= '9'
           || 'a' <= r
           && r <= 'z';
  }

  public static bool ModPathOK(char r) {
    if (r < 0x80) {
      return r == '-'
             || r == '.'
             || r == '_'
             || r == '~'
             || '0' <= r
             && r <= '9'
             || 'A' <= r
             && r <= 'Z'
             || 'a' <= r
             && r <= 'z';
    }
    return false;
  }

  public static (string, string, bool) SplitGopkgIn(string path) {
    if (!path.StartsWith("gopkg.in/")) {
      return (path, "", false);
    }
    int i = path.Length;
    string unstable = "-unstable";
    if (path.EndsWith(unstable)) {
      i -= unstable.Length;
    }
    while (i > 0 && path[i - 1] >= '0' && path[i - 1] <= '9') {
      i--;
    }
    if (i <= 1 || path[i - 1] != 'v' || path[i - 2] != '.') {
      return (path, "", false);
    }
    string prefix = path[..(i - 2)];
    string pathMajor = path[(i - 2)..];
    if (pathMajor.Length <= 2 || (pathMajor[2] == '0' && pathMajor != ".v0")) {
      return (path, "", false);
    }
    return (prefix, pathMajor, true);
  }
}
