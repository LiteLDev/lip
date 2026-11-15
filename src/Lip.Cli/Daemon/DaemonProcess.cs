using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Lip.Core.Infrastructure;
using Semver;

namespace Lip.Cli.Daemon;

/// The executable of this lip, and starting it as the daemon (protocol/lip-daemon.md, "Carrying
/// out a command", step 1).
public static class DaemonProcess {
  /// The argument that makes lip the daemon (protocol/lip.md, OPTIONS).
  public const string Argument = "--daemon";

  // Windows process creation flags.
  private const uint DetachedProcess = 0x00000008;
  private const uint CreateNewProcessGroup = 0x00000200;
  private const uint CreateUnicodeEnvironment = 0x00000400;

  /// The version that lip -v writes.
  public static string Version { get; } = SemVersion.Parse(typeof(DaemonProcess).Assembly
      .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
      .InformationalVersion).ToString();

  /// The program and arguments that run this lip: its executable, or the .NET host with the
  /// assembly of lip when lip runs as lip.dll.
  public static (string Program, string[] Arguments) Command {
    get {
      string process = Environment.ProcessPath
          ?? throw new InvalidOperationException("The executable of lip is not known.");
      // Run by the .NET host, the first argument of lip is its assembly.
      string assembly = Environment.GetCommandLineArgs()[0];
      return Path.GetFileNameWithoutExtension(process) == "dotnet"
          && assembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
          ? (process, [assembly])
          : (process, []);
    }
  }

  /// The executable file that the daemon of this lip runs, which identifies it.
  public static string Executable {
    get {
      (string program, string[] arguments) = Command;
      return arguments.Length > 0 ? arguments[0] : program;
    }
  }

  /// Starts this lip as the daemon, with the environment variables of this process, in a session
  /// of its own, without a terminal or the standard streams of this process. Returns why it could
  /// not, or null.
  public static string? Start() {
    try {
      (string program, string[] arguments) = Command;
      // A directory that the daemon keeps as its working directory cannot be deleted on Windows,
      // so it is one that is never deleted, not that of the client.
      if (OperatingSystem.IsWindows()) {
        StartOnWindows(program, [.. arguments, Argument], Environment.SystemDirectory);
      } else {
        StartElsewhere(program, [.. arguments, Argument], "/");
      }

      return null;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
        or Win32Exception or InvalidOperationException) {
      return $"lip could not start its daemon: {ex.Message}";
    }
  }

  /// Ends the session of the daemon with the terminal it was started from, elsewhere than on
  /// Windows, where it starts without a console.
  public static void Detach() {
    if (!OperatingSystem.IsWindows()) {
      _ = setsid();
    }
  }

  // sh starts the daemon in the background with its standard streams on /dev/null and exits, so
  // that the daemon is not a child of the client; the daemon then calls setsid.
  private static void StartElsewhere(string program, string[] arguments, string directory) {
    ProcessStartInfo info = new("/bin/sh") {
      UseShellExecute = false,
      WorkingDirectory = directory,
    };
    info.ArgumentList.Add("-c");
    info.ArgumentList.Add("\"$0\" \"$@\" </dev/null >/dev/null 2>&1 &");
    info.ArgumentList.Add(program);
    foreach (string argument in arguments) {
      info.ArgumentList.Add(argument);
    }

    using Process sh = Process.Start(info)
        ?? throw new InvalidOperationException("/bin/sh did not start.");
    sh.WaitForExit();
  }

  // CreateProcess without inheriting any handle, so that the daemon keeps no pipe of the client
  // open, and without a console.
  private static void StartOnWindows(string program, string[] arguments, string directory) {
    StringBuilder commandLine = new(Quote(program));
    foreach (string argument in arguments) {
      commandLine.Append(' ').Append(Quote(argument));
    }

    StartupInfo startup = new() { cb = Marshal.SizeOf<StartupInfo>() };
    if (!CreateProcessW(
        program, commandLine, IntPtr.Zero, IntPtr.Zero, bInheritHandles: false,
        DetachedProcess | CreateNewProcessGroup | CreateUnicodeEnvironment, IntPtr.Zero,
        directory, ref startup, out ProcessInformation information)) {
      throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    _ = CloseHandle(information.hProcess);
    _ = CloseHandle(information.hThread);
  }

  /// An argument as the C runtime of Windows reads it back.
  private static string Quote(string argument) {
    if (argument.Length > 0 && !argument.Any(c => c is ' ' or '\t' or '"')) {
      return argument;
    }

    StringBuilder quoted = new("\"");
    int backslashes = 0;
    foreach (char c in argument) {
      if (c == '\\') {
        backslashes++;
        continue;
      }

      quoted.Append('\\', c == '"' ? (backslashes * 2) + 1 : backslashes).Append(c);
      backslashes = 0;
    }

    return quoted.Append('\\', backslashes * 2).Append('"').ToString();
  }

  [DllImport("libc", SetLastError = true)]
  private static extern int setsid();

  [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool CreateProcessW(
      string applicationName,
      StringBuilder commandLine,
      IntPtr processAttributes,
      IntPtr threadAttributes,
      [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
      uint creationFlags,
      IntPtr environment,
      string currentDirectory,
      ref StartupInfo startupInfo,
      out ProcessInformation processInformation);

  [DllImport("kernel32.dll", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool CloseHandle(IntPtr handle);

  [StructLayout(LayoutKind.Sequential)]
  private struct StartupInfo {
    public int cb;
    public IntPtr lpReserved;
    public IntPtr lpDesktop;
    public IntPtr lpTitle;
    public int dwX;
    public int dwY;
    public int dwXSize;
    public int dwYSize;
    public int dwXCountChars;
    public int dwYCountChars;
    public int dwFillAttribute;
    public int dwFlags;
    public short wShowWindow;
    public short cbReserved2;
    public IntPtr lpReserved2;
    public IntPtr hStdInput;
    public IntPtr hStdOutput;
    public IntPtr hStdError;
  }

  [StructLayout(LayoutKind.Sequential)]
  private struct ProcessInformation {
    public IntPtr hProcess;
    public IntPtr hThread;
    public int dwProcessId;
    public int dwThreadId;
  }
}
