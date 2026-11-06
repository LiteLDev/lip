namespace Golang.Org.X.Mod;

/// golang.org/x/mod/module: CheckPath and what it needs.
public static class Module {
  /// Go's Error() is "malformed module path %q: %v"; the message says the same in words, and
  /// the inner exception is the reason alone.
  public class InvalidPathError(string kind, string path, Exception innerException)
      : Exception($"The {kind} path \"{path}\" is not valid: {innerException.Message}.",
          innerException) {
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
      return MakeError(new Exception("it starts with '/'"));
    }
    if (!path[..i].Contains('.')) {
      return MakeError(new Exception($"its first path element \"{path[..i]}\" has no dot, but the"
          + " first element must be a host name such as github.com"));
    }
    if (path[0] == '-') {
      return MakeError(new Exception("its first path element starts with '-'"));
    }
    for (int j = 0; j < i; j++) {
      if (!ModuleInternal.FirstPathOK(path[j])) {
        return new InvalidPathError("module", path, new Exception(
            $"its first path element \"{path[..i]}\" contains {ModuleInternal.Show(path[j])},"
            + " but a host name has only lowercase ASCII letters, digits, '-' and '.'"));
      }
    }
    (string, string, bool) split = SplitPathVersion(path);
    if (!split.Item3) {
      return new InvalidPathError("module", path, new Exception(
          "the major version suffix at its end is invalid (it must be /v2 or higher without a"
          + " leading zero or a dot, or .v0 or higher for gopkg.in)"));
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
      return new Exception("one of its path elements is empty");
    }

    if (elem.Count(c => c == '.') == elem.Length) {
      return new Exception($"the path element \"{elem}\" consists only of dots");
    }

    if (elem[0] == '.') {
      return new Exception($"the path element \"{elem}\" starts with a dot");
    }

    if (elem[^1] == '.') {
      return new Exception($"the path element \"{elem}\" ends with a dot");
    }

    foreach (char r in elem) {
      if (!ModPathOK(r)) {
        return new Exception($"the path element \"{elem}\" contains {Show(r)}, but only ASCII"
            + " letters, digits, '-', '.', '_' and '~' are allowed");
      }
    }

    string shortElem = elem;
    int dotIndex = shortElem.IndexOf('.');
    if (dotIndex >= 0) {
      shortElem = shortElem[..dotIndex];
    }

    foreach (string bad in _badWindowsNames) {
      if (string.Equals(bad, shortElem, StringComparison.OrdinalIgnoreCase)) {
        return new Exception($"the path element \"{elem}\" uses \"{shortElem}\", a name that"
            + " Windows reserves");
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
        return new Exception($"the path element \"{elem}\" ends with '~' followed by digits");
      }
    }
    return null;
  }

  /// checkPath of a module path. A string that is not valid UTF-8 in Go (a lone surrogate here)
  /// has a non-ASCII char, which ModPathOK rejects.
  public static Exception? CheckPath(string path) {
    if (path == "") {
      return new Exception("it is empty");
    }
    if (path[0] == '-') {
      return new Exception("it starts with '-'");
    }
    if (path.Contains("//")) {
      return new Exception("it contains '//'");
    }
    if (path[^1] == '/') {
      return new Exception("it ends with '/'");
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

  /// A character for a message: quoted when it is printable ASCII, otherwise its code point.
  public static string Show(char r) =>
      r is >= ' ' and < '\x7f' ? $"'{r}'" : $"the character U+{(int)r:X4}";

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
