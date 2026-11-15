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
/// workspace root (protocol/lip-workspace.md, "The .lip directory"). Wine is the wine program of
/// the Wine of lip (protocol/lip-workspace.md, "System dependencies"), null when the system has
/// none. IsExecutable tells whether a path is a file lip can run; SystemDirectory is the Windows
/// system directory, which holds cmd.exe.
public sealed record ProgramHost(
    bool IsWindows,
    string Platform,
    string WorkspaceRoot,
    Func<string, string?> GetEnvironmentVariable) {
  public Func<string, bool> IsExecutable { get; init; } = IsExecutableFile;

  public string SystemDirectory { get; init; } = Environment.SystemDirectory;

  public string? Wine { get; init; }

  /// The host lip runs on, with the Wine of lip in the data directory.
  public static ProgramHost Current(string platform, string workspaceRoot) =>
      new(
          OperatingSystem.IsWindows(), platform, workspaceRoot,
          Environment.GetEnvironmentVariable) {
        Wine = ManagedWine.Executable(LipDirectories.Data, SystemInfo.Current),
      };

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
  /// The server of a workspace with a loader, at the workspace root (protocol/lip-run.md).
  public const string ModServerFileName = "bedrock_server_mod.exe";

  /// The Windows server of a workspace without a loader (protocol/lip-run.md).
  public const string WindowsServerFileName = "bedrock_server.exe";

  /// The Linux server (protocol/lip-run.md).
  public const string LinuxServerFileName = "bedrock_server";

  /// The file of the server that the game and the loader choose (protocol/lip-run.md):
  /// bedrock_server_mod.exe with a loader, otherwise that of the build.
  public static string ServerFile(bool loader, Mcmod.GameBuild build) =>
      loader ? ModServerFileName
      : build == Mcmod.GameBuild.Linux ? LinuxServerFileName
      : WindowsServerFileName;

  /// Whether lip runs the Linux server here: on Linux on x64 only (protocol/lip-run.md).
  public static bool RunsLinuxServer =>
      OperatingSystem.IsLinux()
      && System.Runtime.InteropServices.RuntimeInformation.OSArchitecture
          == System.Runtime.InteropServices.Architecture.X64;

  /// A platform that starts with "win" (protocol/lip-workspace.md, "Terms").
  public static bool IsWindowsPlatform(string platform) =>
      platform.StartsWith("win", StringComparison.Ordinal);

  /// Whether Wine runs the programs of the host: the platform is a Windows platform and lip does
  /// not run on Windows (protocol/lip-workspace.md, "Running programs").
  public static bool RunsThroughWine(ProgramHost host) =>
      IsWindowsPlatform(host.Platform) && !host.IsWindows;

  /// A hook command: cmd with the arguments /s, /c and the string in double quotes for a Windows
  /// platform (through Wine: wine with cmd, /s, /c and the string as one argument), sh with -c
  /// and the string for any other (protocol/lip-workspace.md, "Terms" and "Running programs").
  /// sh is found through PATH, and cmd.exe in the Windows system directory, not in the directory
  /// of lip or the workspace. Throws InvalidOperationException when sh is not found through PATH,
  /// or when Wine is needed and the system has no Wine of lip.
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

  /// A Windows server, the file of the name at the workspace root, without arguments
  /// (protocol/lip-run.md). Throws InvalidOperationException when Wine is needed and the system
  /// has no Wine of lip.
  public static ProgramStartInfo Server(ProgramHost host, string fileName) =>
      Start(host, Path.Join(host.WorkspaceRoot, fileName), []);

  /// The Linux server, bedrock_server at the workspace root, run by lip itself without
  /// arguments, with LD_LIBRARY_PATH "." when it is unset or empty (protocol/lip-run.md).
  public static ProgramStartInfo LinuxServer(ProgramHost host) {
    Dictionary<string, string> environment = new(StringComparer.Ordinal);
    if (string.IsNullOrEmpty(host.GetEnvironmentVariable("LD_LIBRARY_PATH"))) {
      environment["LD_LIBRARY_PATH"] = ".";
    }

    return new ProgramStartInfo(
        Path.Join(host.WorkspaceRoot, LinuxServerFileName), [], VerbatimArguments: false,
        host.WorkspaceRoot, environment);
  }

  /// The Windows program fileName with the arguments, run by Wine when the platform is a Windows
  /// platform and lip does not run on Windows, otherwise by lip itself, with the workspace as
  /// working directory. Throws InvalidOperationException when Wine is needed and the system has
  /// no Wine of lip.
  public static ProgramStartInfo Start(
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

    string wine = host.Wine ?? throw new InvalidOperationException(
        $"lip has no Wine for this system, so it cannot run programs for the platform"
        + $" {host.Platform} here: lip runs them on Windows, on Linux on x64 and on macOS.");
    return new ProgramStartInfo(
        wine, [fileName, .. arguments], VerbatimArguments: false, host.WorkspaceRoot, environment);
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
