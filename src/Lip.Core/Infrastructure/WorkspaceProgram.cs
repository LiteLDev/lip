namespace Lip.Core.Infrastructure;

/// How to start a program: the file to run, its arguments, and the environment variables to set
/// on top of those of lip. With VerbatimArguments, the arguments are joined with spaces as they
/// are, for a program that parses its command line itself (Windows only); otherwise each argument
/// reaches the program as one argument.
public sealed record ProgramStartInfo(
    string FileName,
    IReadOnlyList<string> Arguments,
    bool VerbatimArguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string> EnvironmentOverrides) {
  /// The arguments as one Windows command line, for VerbatimArguments.
  public string JoinedArguments => string.Join(' ', Arguments);
}

/// Where a program runs (protocol/lip-workspace.md, "Running programs"): whether lip runs on
/// Windows, the platform of the environment, the absolute workspace root, and the environment
/// variables of lip. When Wine runs the program, WINEPREFIX defaults to .lip/wine below the
/// workspace root (protocol/lip-workspace.md, "The .lip directory"). IsExecutable tells whether a
/// path is a file lip can run; SystemDirectory is the Windows system directory, which holds
/// cmd.exe.
public sealed record ProgramHost(
    bool IsWindows,
    string Platform,
    string WorkspaceRoot,
    Func<string, string?> GetEnvironmentVariable) {
  public Func<string, bool> IsExecutable { get; init; } = IsExecutableFile;

  public string SystemDirectory { get; init; } = Environment.SystemDirectory;

  /// The host lip runs on.
  public static ProgramHost Current(string platform, string workspaceRoot) =>
      new(OperatingSystem.IsWindows(), platform, workspaceRoot, Environment.GetEnvironmentVariable);

  /// A file that is, outside Windows, executable by someone.
  private static bool IsExecutableFile(string path) {
    if (!File.Exists(path)) {
      return false;
    }
    if (OperatingSystem.IsWindows()) {
      return true;
    }

    const UnixFileMode execute =
        UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
    try {
      return (File.GetUnixFileMode(path) & execute) != 0;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
      return false;
    }
  }
}

/// Running programs in a workspace (protocol/lip-workspace.md, "Running programs"): hook commands
/// and the server (protocol/lip-run.md).
public static class WorkspaceProgram {
  /// The server, at the workspace root (protocol/lip-run.md).
  public const string ServerFileName = "bedrock_server_mod.exe";

  /// A platform that starts with "win" (protocol/lip-workspace.md, "Terms").
  public static bool IsWindowsPlatform(string platform) =>
      platform.StartsWith("win", StringComparison.Ordinal);

  /// Whether Wine runs the programs of the host: the platform is a Windows platform and lip does
  /// not run on Windows (protocol/lip-workspace.md, "Running programs").
  public static bool RunsThroughWine(ProgramHost host) =>
      IsWindowsPlatform(host.Platform) && !host.IsWindows;

  /// A hook command: cmd with the arguments /s, /c and the string in double quotes for a Windows
  /// platform (through Wine: wine with cmd, /s, /c and the string as one argument), sh with -c
  /// and the string for any other (protocol/lip-workspace.md, "Terms" and "Running programs"). sh is found through PATH, and
  /// cmd.exe in the Windows system directory, not in the directory of lip or the workspace.
  /// Throws InvalidOperationException when sh or wine is not found through PATH.
  public static ProgramStartInfo HookCommand(string command, ProgramHost host) {
    if (!IsWindowsPlatform(host.Platform)) {
      return Start(host, FindInPath("sh", host), ["-c", command]);
    }

    if (host.IsWindows) {
      // cmd.exe does not parse its command line with the MSVCRT rules arguments are quoted with,
      // so the string goes verbatim; /s makes cmd strip only the outer quotes added here.
      return new ProgramStartInfo(
          Path.Join(host.SystemDirectory, "cmd.exe"), ["/s", "/c", $"\"{command}\""],
          VerbatimArguments: true, host.WorkspaceRoot, new Dictionary<string, string>());
    }

    // Wine makes its Windows command line from its arguments, quoting an argument with a space
    // or a tab in double quotes; /s makes cmd strip exactly those quotes again, so cmd reads the
    // string as written. Wine escapes a double quote in an argument with a backslash, which cmd
    // does not read as an escape, so a string with double quotes does not reach cmd as written.
    return Start(host, "cmd", ["/s", "/c", command]);
  }

  /// The server, bedrock_server_mod.exe at the workspace root, without arguments
  /// (protocol/lip-run.md). Throws InvalidOperationException when wine is needed and not found
  /// through PATH.
  public static ProgramStartInfo Server(ProgramHost host) =>
      Start(host, Path.Join(host.WorkspaceRoot, ServerFileName), []);

  /// The program, run by Wine when the platform is a Windows platform and lip does not run on
  /// Windows, otherwise by lip itself.
  private static ProgramStartInfo Start(
      ProgramHost host, string fileName, IReadOnlyList<string> arguments) {
    if (!RunsThroughWine(host)) {
      return new ProgramStartInfo(
          fileName, arguments, VerbatimArguments: false, host.WorkspaceRoot,
          new Dictionary<string, string>());
    }

    Dictionary<string, string> environment = new(StringComparer.Ordinal);
    if (string.IsNullOrEmpty(host.GetEnvironmentVariable("WINEPREFIX"))) {
      environment["WINEPREFIX"] = Path.Join(host.WorkspaceRoot, ".lip", "wine");
    }

    if (host.GetEnvironmentVariable("WINEDEBUG") is null) {
      environment["WINEDEBUG"] = "-all";
    }

    return new ProgramStartInfo(
        FindInPath("wine", host), [fileName, .. arguments], VerbatimArguments: false,
        host.WorkspaceRoot, environment);
  }

  /// The absolute path of the first executable file of the name in the directories of PATH, with
  /// ".exe" on Windows. Directories that are not absolute are skipped, so that the program is
  /// never looked up relative to the working directory, the workspace. Throws
  /// InvalidOperationException when there is none.
  private static string FindInPath(string name, ProgramHost host) {
    string fileName = host.IsWindows ? name + ".exe" : name;
    char separator = host.IsWindows ? ';' : ':';
    foreach (string directory in (host.GetEnvironmentVariable("PATH") ?? "").Split(separator)) {
      string trimmed = host.IsWindows ? directory.Trim().Trim('"') : directory;
      if (!IsAbsolute(trimmed, host.IsWindows)) {
        continue;
      }

      string candidate = Path.Join(trimmed, fileName);
      if (host.IsExecutable(candidate)) {
        return candidate;
      }
    }

    throw new InvalidOperationException($"'{name}' is not found through PATH.");
  }

  private static bool IsAbsolute(string path, bool windows) =>
      windows
          ? path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':'
              && path[2] is '\\' or '/'
              || path.StartsWith(@"\\", StringComparison.Ordinal)
          : path.StartsWith('/');
}
