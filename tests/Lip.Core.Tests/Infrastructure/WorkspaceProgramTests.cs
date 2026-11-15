using System.Runtime.InteropServices;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod;

namespace Lip.Core.Tests.Infrastructure;

/// protocol/lip-workspace.md, "Terms" (hook command) and "Running programs", for every
/// combination of the system lip runs on and the platform of the environment.
public class WorkspaceProgramTests {
  private const string Root = "/srv/ws";
  private const string ModServer = "bedrock_server_mod.exe";
  private const string Hook =
      "IF EXIST bedrock_server.exe (DEL bedrock_server.exe.bak) ELSE (REN bedrock_server.exe.bak bedrock_server.exe)";

  // The PATH of the host, the programs in it, and programs at the workspace root, in the working
  // directory and in a relative directory of PATH, which are never run in their place.
  private const string UnixPath = ":.:relative/bin:/opt/none:/usr/bin";
  private const string WindowsPath = @".;C:\none;""C:\Program Files\Git\bin""";
  private const string SystemDirectory = @"C:\Windows\system32";
  private static readonly string _sh = Path.Join("/usr/bin", "sh");
  // A wine in PATH, which lip never runs, and the wine program of the Wine of lip.
  private static readonly string _wine = Path.Join("/usr/bin", "wine");
  private static readonly string _managedWine = Path.Join("/data/lip/wine/build/bin", "wine");
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
      Wine = isWindows ? null : _managedWine,
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
    Assert.Equal(_managedWine, start.FileName);
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
  public void Server_NoWineOfLip_ThrowsEvenWithWineInPath() {
    // Arrange: the system has no Wine of lip; PATH has a wine.
    ProgramHost host = Host(false, "win-x64") with { Wine = null };

    // Act & Assert
    InvalidOperationException ex =
        Assert.Throws<InvalidOperationException>(() => WorkspaceProgram.Server(host, ModServer));
    Assert.Contains("no Wine", ex.Message);
  }

  [Fact]
  public void Start_WindowsPlatformElsewhere_RunsTheProgramThroughWine() {
    // Act
    ProgramStartInfo start = WorkspaceProgram.Start(
        Host(false, "win-x64"), "/data/lip/vc_redist.x64.exe", ["/install", "/quiet"]);

    // Assert
    Assert.Equal(_managedWine, start.FileName);
    Assert.Equal(["/data/lip/vc_redist.x64.exe", "/install", "/quiet"], start.Arguments);
    Assert.Equal(WinePrefix, start.EnvironmentOverrides["WINEPREFIX"]);
  }

  [Theory]
  [InlineData(HostOs.Linux, Architecture.X64, "wine-11.21-amd64-wow64/bin/wine")]
  [InlineData(HostOs.MacOS, Architecture.X64, "Wine Devel.app/Contents/Resources/wine/bin/wine")]
  [InlineData(HostOs.MacOS, Architecture.Arm64, "Wine Devel.app/Contents/Resources/wine/bin/wine")]
  [InlineData(HostOs.Linux, Architecture.Arm64, null)]
  [InlineData(HostOs.Windows, Architecture.X64, null)]
  [InlineData(HostOs.Other, Architecture.X64, null)]
  public void ManagedWine_Executable_IsBelowWineInTheDataDirectory(
      HostOs os, Architecture architecture, string? executable) {
    // Act
    string? path = ManagedWine.Executable("/data/lip", new SystemInfo(os, architecture));

    // Assert
    if (executable is null) {
      Assert.Null(path);
    } else {
      Assert.NotNull(path);
      Assert.StartsWith(Path.Join("/data/lip", "wine") + Path.DirectorySeparatorChar, path);
      Assert.EndsWith(executable, path.Replace('\\', '/'));
    }
  }

  [Fact]
  public void Server_WindowsPlatformOnWindows_RunsTheServerItself() {
    // Act
    ProgramStartInfo start = WorkspaceProgram.Server(Host(true, "win-x64"), ModServer);

    // Assert
    Assert.Equal(Path.Join(Root, "bedrock_server_mod.exe"), start.FileName);
    Assert.Empty(start.Arguments);
    Assert.Equal(Root, start.WorkingDirectory);
    Assert.Empty(start.EnvironmentOverrides);
  }

  [Fact]
  public void Server_WindowsPlatformElsewhere_RunsTheServerThroughWine() {
    // Act
    ProgramStartInfo start = WorkspaceProgram.Server(Host(false, "win-x64"), ModServer);

    // Assert
    Assert.Equal(_managedWine, start.FileName);
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
    ProgramStartInfo start = WorkspaceProgram.Server(Host(isWindows, "linux-x64"), ModServer);

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
    }), ModServer);

    // Assert: an empty WINEDEBUG is set, so it is kept.
    Assert.Empty(start.EnvironmentOverrides);
  }

  [Fact]
  public void Server_WinePrefixEmpty_SetsIt() {
    // Act
    ProgramStartInfo start = WorkspaceProgram.Server(Host(false, "win-x64", new() {
      ["WINEPREFIX"] = "",
      ["WINEDEBUG"] = "warn+all",
    }), ModServer);

    // Assert
    Assert.Equal(
        new Dictionary<string, string> { ["WINEPREFIX"] = WinePrefix },
        start.EnvironmentOverrides);
  }

  [Theory]
  [InlineData(true, GameBuild.Windows, "bedrock_server_mod.exe")]
  [InlineData(false, GameBuild.Windows, "bedrock_server.exe")]
  [InlineData(false, GameBuild.Linux, "bedrock_server")]
  public void ServerFile_IsChosenByTheLoaderAndTheBuild(
      bool loader, GameBuild build, string expected) {
    // protocol/lip-run.md, DESCRIPTION.
    Assert.Equal(expected, WorkspaceProgram.ServerFile(loader, build));
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public void LinuxServer_RunsItItselfWithLdLibraryPath(bool isWindows) {
    // Act: whatever Wine the host has (protocol/lip-run.md).
    ProgramStartInfo start = WorkspaceProgram.LinuxServer(Host(isWindows, "win-x64"));

    // Assert
    Assert.Equal(Path.Join(Root, "bedrock_server"), start.FileName);
    Assert.Empty(start.Arguments);
    Assert.Equal(Root, start.WorkingDirectory);
    Assert.Equal(
        new Dictionary<string, string> { ["LD_LIBRARY_PATH"] = "." }, start.EnvironmentOverrides);
  }

  [Fact]
  public void LinuxServer_LdLibraryPathSet_KeepsIt() {
    // Act
    ProgramStartInfo start = WorkspaceProgram.LinuxServer(
        Host(false, "win-x64", new() { ["LD_LIBRARY_PATH"] = "/opt/lib" }));

    // Assert
    Assert.Empty(start.EnvironmentOverrides);
  }
}
