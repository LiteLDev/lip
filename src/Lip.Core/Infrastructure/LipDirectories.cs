namespace Lip.Core.Infrastructure;

/// lip's per-user directories (protocol/lip-userspace.md, "Directories"), following the platform
/// conventions: the known folders on Windows, the XDG Base Directory specification elsewhere, with
/// the environment variables of the current invocation (Invocation).
public static class LipDirectories {
  private const string AppName = "lip";

  /// User-editable configuration: %APPDATA%\lip on Windows, $XDG_CONFIG_HOME/lip (default
  /// ~/.config/lip) elsewhere.
  public static string Config => OperatingSystem.IsWindows()
      ? Path.Join(GetFolder(Environment.SpecialFolder.ApplicationData), AppName)
      : Path.Join(GetXdgHome("XDG_CONFIG_HOME", ".config"), AppName);

  /// Data that can be deleted without losing anything: %LOCALAPPDATA%\lip\Cache on Windows, which
  /// has no cache folder of its own, and $XDG_CACHE_HOME/lip (default ~/.cache/lip) elsewhere.
  public static string Cache => OperatingSystem.IsWindows()
      ? Path.Join(GetFolder(Environment.SpecialFolder.LocalApplicationData), AppName, "Cache")
      : Path.Join(GetXdgHome("XDG_CACHE_HOME", ".cache"), AppName);

  /// What lip installs to run programs, the Wine of lip among it (protocol/lip-userspace.md,
  /// "Data directory"): %LOCALAPPDATA%\lip\Data on Windows, $XDG_DATA_HOME/lip (default
  /// ~/.local/share/lip) elsewhere, macOS included.
  public static string Data => OperatingSystem.IsWindows()
      ? Path.Join(GetFolder(Environment.SpecialFolder.LocalApplicationData), AppName, "Data")
      : Path.Join(GetXdgHome("XDG_DATA_HOME", Path.Join(".local", "share")), AppName);

  /// The files of the daemon (protocol/lip-userspace.md): %LOCALAPPDATA%\lip\State on Windows,
  /// $XDG_STATE_HOME/lip (default ~/.local/state/lip) elsewhere.
  public static string State => OperatingSystem.IsWindows()
      ? Path.Join(GetFolder(Environment.SpecialFolder.LocalApplicationData), AppName, "State")
      : Path.Join(GetXdgHome("XDG_STATE_HOME", Path.Join(".local", "state")), AppName);

  /// The daemon directory, elsewhere than on Windows (protocol/lip-daemon.md, "Terms"):
  /// $XDG_RUNTIME_DIR/lip when XDG_RUNTIME_DIR is an absolute path, otherwise the state directory.
  public static string Daemon =>
      Invocation.Current.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } runtime
          && Path.IsPathRooted(runtime)
          ? Path.Join(runtime, AppName)
          : State;

  private static string GetFolder(Environment.SpecialFolder folder) =>
      Environment.GetFolderPath(folder, Environment.SpecialFolderOption.DoNotVerify);

  // The specification says to ignore an empty or relative value. The home directory is $HOME, as
  // .NET reads it for the process.
  private static string GetXdgHome(string variable, string defaultBelowHome) {
    Invocation invocation = Invocation.Current;
    if (invocation.GetEnvironmentVariable(variable) is { Length: > 0 } value
        && Path.IsPathRooted(value)) {
      return value;
    }

    string home = invocation.EnvironmentVariables is not null
        && invocation.GetEnvironmentVariable("HOME") is { Length: > 0 } clientHome
        ? clientHome
        : GetFolder(Environment.SpecialFolder.UserProfile);
    return Path.Join(home, defaultBelowHome);
  }
}
