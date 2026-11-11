using Lip.Core.Infrastructure;

namespace Lip.Core.Tests.Infrastructure;

/// protocol/lip-workspace.md, "Terms" (hook command) and "Running programs", for every
/// combination of the system lip runs on and the platform of the environment.
public class WorkspaceProgramTests {
  private const string Root = "/srv/ws";
  private const string Hook =
      "IF EXIST bedrock_server.exe (DEL bedrock_server.exe.bak) ELSE (REN bedrock_server.exe.bak bedrock_server.exe)";

  // The PATH of the host, the programs in it, and programs at the workspace root, in the working
  // directory and in a relative directory of PATH, which are never run in their place.
  private const string UnixPath = ":.:relative/bin:/opt/none:/usr/bin";
  private const string WindowsPath = @".;C:\none;""C:\Program Files\Git\bin""";
  private const string SystemDirectory = @"C:\Windows\system32";
  private static readonly string _sh = Path.Join("/usr/bin", "sh");
  private static readonly string _wine = Path.Join("/usr/bin", "wine");
  private static readonly string _windowsSh = Path.Join(@"C:\Program Files\Git\bin", "sh.exe");

  private static readonly HashSet<string> _executables = [
    _sh, _wine, _windowsSh,
    Path.Join(Root, "sh"), Path.Join(Root, "wine"), Path.Join("", "sh"), Path.Join(".", "sh"),
    Path.Join(".", "wine"), Path.Join("relative/bin", "sh"), Path.Join(".", "sh.exe"),
  ];

  private static ProgramHost Host(
      bool isWindows, string platform, Dictionary<string, string>? variables = null) {
    Dictionary<string, string> all = new() { ["PATH"] = isWindows ? WindowsPath : UnixPath };
    foreach ((string name, string value) in variables ?? []) {
      all[name] = value;
    }

    return new(isWindows, platform, Root, all.GetValueOrDefault) {
      IsExecutable = _executables.Contains,
      SystemDirectory = SystemDirectory,
    };
  }

  private static string WinePrefix => Path.Join(Root, ".lip", "wine");

  [Theory]
  [InlineData("win-x64", true)]
  [InlineData("win-arm64", true)]
  [InlineData("win", true)]
  [InlineData("linux-x64", false)]
  [InlineData("osx-arm64", false)]
  [InlineData("Win-x64", false)]
  public void IsWindowsPlatform_StartsWithWin(string platform, bool expected) {
    Assert.Equal(expected, WorkspaceProgram.IsWindowsPlatform(platform));
  }

  [Fact]
  public void HookCommand_WindowsPlatformOnWindows_RunsCmdWithTheStringVerbatim() {
    // Act
    ProgramStartInfo start = WorkspaceProgram.HookCommand(Hook, Host(true, "win-x64"));

    // Assert
    Assert.Equal(Path.Join(SystemDirectory, "cmd.exe"), start.FileName);
    Assert.True(start.VerbatimArguments);
    Assert.Equal($"/s /c \"{Hook}\"", start.JoinedArguments);
    Assert.Equal(Root, start.WorkingDirectory);
    Assert.Empty(start.EnvironmentOverrides);
  }

  [Fact]
  public void HookCommand_WindowsPlatformElsewhere_RunsCmdThroughWine() {
    // Act
    ProgramStartInfo start = WorkspaceProgram.HookCommand(Hook, Host(false, "win-x64"));

    // Assert
    Assert.Equal(_wine, start.FileName);
    Assert.False(start.VerbatimArguments);
    Assert.Equal(["cmd", "/s", "/c", Hook], start.Arguments);
    Assert.Equal(Root, start.WorkingDirectory);
    Assert.Equal(
        new Dictionary<string, string> { ["WINEPREFIX"] = WinePrefix, ["WINEDEBUG"] = "-all" },
        start.EnvironmentOverrides);
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public void HookCommand_OtherPlatform_RunsSh(bool isWindows) {
    // Act
    ProgramStartInfo start = WorkspaceProgram.HookCommand(
        "./PeEditor -mb", Host(isWindows, "linux-x64"));

    // Assert
    Assert.Equal(isWindows ? _windowsSh : _sh, start.FileName);
    Assert.False(start.VerbatimArguments);
    Assert.Equal(["-c", "./PeEditor -mb"], start.Arguments);
    Assert.Equal(Root, start.WorkingDirectory);
    Assert.Empty(start.EnvironmentOverrides);
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public void HookCommand_ShOnlyOutsideAbsoluteDirectoriesOfPath_Throws(bool isWindows) {
    // Act & Assert: the workspace root, the working directory, has sh, and so do the relative
    // directories of PATH.
    string path = isWindows ? ".;;" : ":.:relative/bin";
    InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
        () => WorkspaceProgram.HookCommand(
            "true", Host(isWindows, "linux-x64", new() { ["PATH"] = path })));
    Assert.Contains("'sh'", ex.Message);
  }

  [Fact]
  public void Server_WineNotInPath_Throws() {
    // Act & Assert
    InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
        () => WorkspaceProgram.Server(Host(false, "win-x64", new() { ["PATH"] = "." })));
    Assert.Contains("'wine'", ex.Message);
  }

  [Fact]
  public void Server_WindowsPlatformOnWindows_RunsTheServerItself() {
    // Act
    ProgramStartInfo start = WorkspaceProgram.Server(Host(true, "win-x64"));

    // Assert
    Assert.Equal(Path.Join(Root, "bedrock_server_mod.exe"), start.FileName);
    Assert.Empty(start.Arguments);
    Assert.Equal(Root, start.WorkingDirectory);
    Assert.Empty(start.EnvironmentOverrides);
  }

  [Fact]
  public void Server_WindowsPlatformElsewhere_RunsTheServerThroughWine() {
    // Act
    ProgramStartInfo start = WorkspaceProgram.Server(Host(false, "win-x64"));

    // Assert
    Assert.Equal(_wine, start.FileName);
    Assert.False(start.VerbatimArguments);
    Assert.Equal([Path.Join(Root, "bedrock_server_mod.exe")], start.Arguments);
    Assert.Equal(Root, start.WorkingDirectory);
    Assert.Equal(
        new Dictionary<string, string> { ["WINEPREFIX"] = WinePrefix, ["WINEDEBUG"] = "-all" },
        start.EnvironmentOverrides);
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public void Server_OtherPlatform_RunsTheServerItself(bool isWindows) {
    // Act
    ProgramStartInfo start = WorkspaceProgram.Server(Host(isWindows, "linux-x64"));

    // Assert
    Assert.Equal(Path.Join(Root, "bedrock_server_mod.exe"), start.FileName);
    Assert.Empty(start.Arguments);
    Assert.Empty(start.EnvironmentOverrides);
  }

  [Fact]
  public void Server_WineVariablesSet_KeepsThem() {
    // Act
    ProgramStartInfo start = WorkspaceProgram.Server(Host(false, "win-x64", new() {
      ["WINEPREFIX"] = "/home/u/.wine",
      ["WINEDEBUG"] = "",
    }));

    // Assert: an empty WINEDEBUG is set, so it is kept.
    Assert.Empty(start.EnvironmentOverrides);
  }

  [Fact]
  public void Server_WinePrefixEmpty_SetsIt() {
    // Act
    ProgramStartInfo start = WorkspaceProgram.Server(Host(false, "win-x64", new() {
      ["WINEPREFIX"] = "",
      ["WINEDEBUG"] = "warn+all",
    }));

    // Assert
    Assert.Equal(
        new Dictionary<string, string> { ["WINEPREFIX"] = WinePrefix },
        start.EnvironmentOverrides);
  }
}
