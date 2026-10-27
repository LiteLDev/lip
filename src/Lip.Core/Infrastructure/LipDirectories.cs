namespace Lip.Core.Infrastructure;

/// lip's per-user directories, following the platform conventions: the known folders on Windows,
/// the XDG Base Directory specification elsewhere.
public static class LipDirectories {
  private const string AppName = "lip";

  /// User-editable configuration: %APPDATA%\lip on Windows, $XDG_CONFIG_HOME/lip (default
  /// ~/.config/lip) elsewhere. .NET maps SpecialFolder.ApplicationData to both.
  public static string Config => Path.Join(GetFolder(Environment.SpecialFolder.ApplicationData), AppName);

  /// State lip cannot recreate, such as the workspace locks: %LOCALAPPDATA%\lip on Windows,
  /// $XDG_DATA_HOME/lip (default ~/.local/share/lip) elsewhere. .NET maps
  /// SpecialFolder.LocalApplicationData to both.
  public static string Data => Path.Join(GetFolder(Environment.SpecialFolder.LocalApplicationData), AppName);

  /// Data that can be deleted without losing anything: %LOCALAPPDATA%\lip\Cache on Windows, which
  /// has no cache folder of its own, and $XDG_CACHE_HOME/lip (default ~/.cache/lip) elsewhere,
  /// for which .NET has no special folder.
  public static string Cache => OperatingSystem.IsWindows()
      ? Path.Join(GetFolder(Environment.SpecialFolder.LocalApplicationData), AppName, "Cache")
      : Path.Join(GetXdgCacheHome(), AppName);

  /// Files being written, inside the cache directory so that moving a finished file into the
  /// cache is a rename on the same volume. No cache key escapes to a name starting with ".".
  public static string Staging => Path.Join(Cache, ".staging");

  private static string GetFolder(Environment.SpecialFolder folder) =>
      Environment.GetFolderPath(folder, Environment.SpecialFolderOption.DoNotVerify);

  // The specification says to ignore an empty or relative value.
  private static string GetXdgCacheHome() =>
      Environment.GetEnvironmentVariable("XDG_CACHE_HOME") is { Length: > 0 } value
          && Path.IsPathRooted(value)
          ? value
          : Path.Join(GetFolder(Environment.SpecialFolder.UserProfile), ".cache");
}
