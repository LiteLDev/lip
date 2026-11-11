using Lip.Core.Infrastructure;
using Lip.Core.Mcmod.Lock;

namespace Lip.Core.Mcmod;

/// How the paths of a workspace are compared, and which names in them are valid, for the
/// environment of an operation (protocol/lip-workspace.md, "Environment and layout" and
/// "Installing and uninstalling"):
///
/// - On Windows, paths are compared ignoring case, as the file system does.
/// - When the platform of the environment is a Windows platform and lip does not run on Windows,
///   paths are case-insensitive (IgnoresCase): A-Z equal a-z, as they are to the programs that
///   Wine runs. A path then names the existing files and directories its names equal so.
/// - Otherwise paths are compared ordinally.
///
/// The Windows rules for names (WindowsNames) apply when lip runs on Windows or the platform is a
/// Windows platform.
public sealed class WorkspacePaths {
  private WorkspacePaths(bool onWindows, bool windowsPlatform) {
    OnWindows = onWindows;
    IgnoresCase = windowsPlatform && !onWindows;
    WindowsNames = onWindows || windowsPlatform;
    Comparer = onWindows
        ? StringComparer.OrdinalIgnoreCase
        : IgnoresCase ? AsciiIgnoreCaseComparer.Instance : StringComparer.Ordinal;
  }

  /// The rules of a workspace whose environment is not known, as when an interrupted operation
  /// is recovered: those of the host lip runs on, for paths as the file system spells them.
  public static WorkspacePaths Host { get; } = new(OperatingSystem.IsWindows(), false);

  /// The rules of a workspace for the environment of an operation on it.
  public static WorkspacePaths For(LipEnvironment environment) =>
      new(OperatingSystem.IsWindows(), WorkspaceProgram.IsWindowsPlatform(environment.Platform));

  /// Whether lip runs on Windows.
  public bool OnWindows { get; }

  /// Whether the paths of the workspace are case-insensitive (protocol/lip-workspace.md,
  /// "Environment and layout"): the platform is a Windows platform and lip does not run on
  /// Windows.
  public bool IgnoresCase { get; }

  /// Whether a name containing ':' or ending in '.' or a space is invalid (protocol/lip-workspace.md,
  /// "Installing and uninstalling").
  public bool WindowsNames { get; }

  /// How workspace paths are compared.
  public StringComparer Comparer { get; }

  public bool AreEqual(string a, string b) => Comparer.Equals(a, b);

  /// Whether path starts with prefix, compared as paths are.
  public bool StartsWith(string path, string prefix) =>
      path.Length >= prefix.Length && Comparer.Equals(path[..prefix.Length], prefix);

  /// Whether a workspace-relative path is mods itself, which lip never deletes.
  public bool IsModsDirectory(string path) =>
      AreEqual(path.TrimEnd('/'), WorkspaceLayout.ModsDirectory);

  /// Whether a workspace-relative path is .lip or below it, where lip keeps its own files (the
  /// lock): packages never write there, and purge never matches it.
  public bool IsLipPath(string path) =>
      AreEqual(path.TrimEnd('/'), LockStore.LipDirectory)
      || StartsWith(path, LockStore.LipDirectory + "/");

  /// Whether a segment of a path other than "." is invalid by the Windows rules for names, when
  /// they apply (protocol/lip-workspace.md, "Installing and uninstalling"): it contains ':' or
  /// ends in '.' or a space.
  public bool IsInvalidName(string segment) =>
      WindowsNames && segment != "."
      && (segment.EndsWith('.') || segment.EndsWith(' ') || segment.Contains(':'));
}

/// Compares strings ordinally, with the ASCII letters A-Z equal to a-z and no other characters
/// folded (protocol/lip-workspace.md, "Environment and layout").
public sealed class AsciiIgnoreCaseComparer : StringComparer {
  public static AsciiIgnoreCaseComparer Instance { get; } = new();

  private AsciiIgnoreCaseComparer() {
  }

  public override int Compare(string? x, string? y) {
    if (ReferenceEquals(x, y)) {
      return 0;
    }
    if (x is null) {
      return -1;
    }
    if (y is null) {
      return 1;
    }

    for (int i = 0; i < Math.Min(x.Length, y.Length); i++) {
      int difference = Fold(x[i]) - Fold(y[i]);
      if (difference != 0) {
        return difference;
      }
    }

    return x.Length - y.Length;
  }

  public override bool Equals(string? x, string? y) =>
      ReferenceEquals(x, y)
      || (x is not null && y is not null && x.Length == y.Length && Compare(x, y) == 0);

  public override int GetHashCode(string obj) {
    HashCode hash = new();
    foreach (char c in obj) {
      hash.Add(Fold(c));
    }

    return hash.ToHashCode();
  }

  /// The character with A-Z replaced by a-z.
  public static char Fold(char c) => c is >= 'A' and <= 'Z' ? (char)(c + ('a' - 'A')) : c;
}
