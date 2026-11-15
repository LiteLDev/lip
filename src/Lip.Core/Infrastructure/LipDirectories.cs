namespace Lip.Core.Infrastructure;

/// lip's per-user directories, following the platform conventions: the known folders on Windows,
/// the XDG Base Directory specification elsewhere.
public static class LipDirectories {
  private const string AppName = "lip";

  /// User-editable configuration: %APPDATA%\lip on Windows, $XDG_CONFIG_HOME/lip (default
  /// ~/.config/lip) elsewhere. .NET maps SpecialFolder.ApplicationData to both.
  public static string Config => Path.Join(GetFolder(Environment.SpecialFolder.ApplicationData), AppName);

  /// Data that can be deleted without losing anything: %LOCALAPPDATA%\lip\Cache on Windows, which
  /// has no cache folder of its own, and $XDG_CACHE_HOME/lip (default ~/.cache/lip) elsewhere,
  /// for which .NET has no special folder.
  public static string Cache => OperatingSystem.IsWindows()
      ? Path.Join(GetFolder(Environment.SpecialFolder.LocalApplicationData), AppName, "Cache")
      : Path.Join(GetXdgCacheHome(), AppName);

  /// What lip installs to run programs, the Wine of lip among it (protocol/lip-userspace.md,
  /// "Data directory"): %LOCALAPPDATA%\lip\Data on Windows, $XDG_DATA_HOME/lip (default
  /// ~/.local/share/lip) elsewhere, macOS included.
  public static string Data => OperatingSystem.IsWindows()
      ? Path.Join(GetFolder(Environment.SpecialFolder.LocalApplicationData), AppName, "Data")
      : Path.Join(GetXdgHome("XDG_DATA_HOME", Path.Join(".local", "share")), AppName);

  private static string GetFolder(Environment.SpecialFolder folder) =>
      Environment.GetFolderPath(folder, Environment.SpecialFolderOption.DoNotVerify);

  private static string GetXdgCacheHome() => GetXdgHome("XDG_CACHE_HOME", ".cache");

  // The specification says to ignore an empty or relative value.
  private static string GetXdgHome(string variable, string defaultBelowHome) =>
      Environment.GetEnvironmentVariable(variable) is { Length: > 0 } value
          && Path.IsPathRooted(value)
          ? value
          : Path.Join(GetFolder(Environment.SpecialFolder.UserProfile), defaultBelowHome);
}
