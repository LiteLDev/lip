namespace Lip.Core.Infrastructure;

/// lip's per-user directories, following the platform conventions: the known folders on Windows,
/// the XDG Base Directory specification elsewhere.
public static class LipDirectories {
  private const string AppName = "lip";

  /// User-editable configuration: %APPDATA%\lip on Windows, $XDG_CONFIG_HOME/lip (default
  /// ~/.config/lip) elsewhere. .NET maps SpecialFolder.ApplicationData to both.
  public static string Config => Path.Join(GetFolder(Environment.SpecialFolder.ApplicationData), AppName);

  /// Data that can be deleted without losing anything: %LOCALAPPDATA%\lip\cache on Windows, which
  /// has no cache folder of its own, and $XDG_CACHE_HOME/lip (default ~/.cache/lip) elsewhere,
  /// for which .NET has no special folder.
  public static string Cache => OperatingSystem.IsWindows()
      ? Path.Join(GetFolder(Environment.SpecialFolder.LocalApplicationData), AppName, "cache")
      : Path.Join(GetXdgCacheHome(), AppName);

  /// Temporary files: %TEMP%\lip on Windows, $TMPDIR/lip (default /tmp/lip) elsewhere.
  public static string Temp => Path.Join(Path.GetTempPath(), AppName);

  private static string GetFolder(Environment.SpecialFolder folder) =>
      Environment.GetFolderPath(folder, Environment.SpecialFolderOption.DoNotVerify);

  // The specification says to ignore an empty or relative value.
  private static string GetXdgCacheHome() =>
      Environment.GetEnvironmentVariable("XDG_CACHE_HOME") is { Length: > 0 } value
          && Path.IsPathRooted(value)
          ? value
          : Path.Join(GetFolder(Environment.SpecialFolder.UserProfile), ".cache");
}
