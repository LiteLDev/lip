using System.Diagnostics;
using System.Text.Json;

namespace Lip.Cli.Tests;

/// Invocation and usage errors (protocol/lip.md, "Terms"), checked by running lip in an empty
/// directory: a usage error writes a message and exits with a non-zero code, without a failure
/// report below .lip/logs.
public sealed class UsageErrorTests : IDisposable {
  private readonly string _workspace = Directory.CreateTempSubdirectory("lip-cli-tests-").FullName;

  public void Dispose() {
    Directory.Delete(_workspace, recursive: true);
  }

  [Theory]
  [InlineData("--bogus", "list", "--bogus")]
  [InlineData("--dryrun", "uninstall", "foo", "--dryrun")]
  [InlineData("--dryrun", "uninstall", "--dryrun", "foo")]
  [InlineData("--bogus", "install", "--bogus", "x")]
  [InlineData("--bogus", "pkg", "view", "--bogus", "x")]
  [InlineData("--bogus", "pkg", "pack", "-o", "out.zip", "--bogus")]
  [InlineData("--bogus", "pkg", "init", "--bogus")]
  [InlineData("--bogus", "pkg", "migrate", "--bogus")]
  // lip pkg view always writes JSON (protocol/lip-pkg.md, "lip pkg view").
  [InlineData("--json", "pkg", "view", "x", "--json")]
  [InlineData("--bogus", "config", "list", "--bogus")]
  [InlineData("--bogus", "run", "--bogus")]
  // lip run has no --platform (protocol/lip-run.md).
  [InlineData("--platform", "run", "--platform", "win-x64")]
  [InlineData("--bogus", "config", "get", "--bogus", "x")]
  [InlineData("--bogus", "config", "delete", "--bogus", "x")]
  [InlineData("-x", "install", "-x", "foo")]
  // Renamed to --platform in lip v0.40.
  [InlineData("--rid", "install", "x", "--rid")]
  // No longer options of install, uninstall and update.
  [InlineData("--ignore-scripts", "install", "x", "--ignore-scripts")]
  [InlineData("--no-dependencies", "install", "x", "--no-dependencies")]
  [InlineData("--ignore-scripts", "uninstall", "x", "--ignore-scripts")]
  [InlineData("--no-dependencies", "uninstall", "x", "--no-dependencies")]
  [InlineData("--ignore-scripts", "update", "--ignore-scripts")]
  public async Task UnknownOption_IsUsageError(string option, params string[] args) {
    (int exitCode, _, string stderr) = await Run(args);

    Assert.NotEqual(0, exitCode);
    Assert.Contains($"Unknown option '{option}'", stderr);
    Assert.DoesNotContain("missing", stderr, StringComparison.OrdinalIgnoreCase);
    Assert.False(Directory.Exists(Path.Join(_workspace, ".lip")));
    Assert.False(File.Exists(Path.Join(_workspace, "out.zip")));
  }

  [Theory]
  [InlineData("pkg", "pack", "-o")]
  [InlineData("pkg", "pack", "--output")]
  [InlineData("pkg", "pack", "-o", "--help")]
  [InlineData("update", "--platform")]
  [InlineData("update", "--platform", "-n")]
  [InlineData("install", "x", "--platform")]
  [InlineData("uninstall", "x", "--platform")]
  // An empty value is no value (protocol/lip.md, "Arguments"), so -h does not make it help.
  [InlineData("update", "-n", "--platform", "")]
  [InlineData("update", "--platform", "", "-h")]
  [InlineData("install", "x", "--platform", "", "-n")]
  [InlineData("uninstall", "x", "--platform", "")]
  [InlineData("pkg", "pack", "-o", "")]
  [InlineData("pkg", "pack", "--output", "", "--help")]
  public async Task OptionWithoutValue_IsUsageError(params string[] args) {
    (int exitCode, _, string stderr) = await Run(args);

    Assert.NotEqual(0, exitCode);
    Assert.Contains("no value", stderr);
    Assert.False(Directory.Exists(Path.Join(_workspace, ".lip")));
    Assert.Empty(Directory.EnumerateFileSystemEntries(_workspace));
  }

  [Theory]
  [InlineData("list", "--", "extra")]
  [InlineData("update", "-n", "--", "github.com/a/b")]
  [InlineData("install", "-n", "foo", "--", "bar")]
  [InlineData("pkg", "pack", "-o", "out.zip", "--", "dir")]
  [InlineData("config", "list", "--", "extra")]
  [InlineData("list", "--", "--bogus")]
  [InlineData("install", "--", "foo")]
  [InlineData("uninstall", "--", "foo")]
  [InlineData("pkg", "view", "--", "foo")]
  [InlineData("pkg", "--")]
  [InlineData("pkg", "--", "view")]
  [InlineData("config", "get", "--", "key")]
  [InlineData("list", "--")]
  [InlineData("update", "-n", "--")]
  [InlineData("config", "--")]
  [InlineData("config", "--", "get")]
  [InlineData("config", "--", "x")]
  [InlineData("cache", "--")]
  [InlineData("cache", "--", "clean")]
  [InlineData("pkg", "pack", "-o", "--")]
  [InlineData("--")]
  [InlineData("--", "list")]
  [InlineData("list", "--help", "--")]
  [InlineData("config", "--help", "--")]
  public async Task Separator_IsUsageError(params string[] args) {
    (int exitCode, string stdout, string stderr) = await Run(args);

    Assert.NotEqual(0, exitCode);
    Assert.Equal("", stdout);
    Assert.Contains("'--'", stderr);
    Assert.DoesNotContain("missing", stderr, StringComparison.OrdinalIgnoreCase);
    Assert.False(Directory.Exists(Path.Join(_workspace, ".lip")));
    Assert.Empty(Directory.EnumerateFileSystemEntries(_workspace));
  }

  [Theory]
  [InlineData("config")]
  [InlineData("cache")]
  [InlineData("pkg")]
  public async Task BranchWithoutSubcommand_IsUsageError(string branch) {
    (int exitCode, string stdout, string stderr) = await Run(branch);

    Assert.NotEqual(0, exitCode);
    Assert.Equal("", stdout);
    Assert.Contains($"'lip {branch}' needs a subcommand", stderr);
    Assert.False(Directory.Exists(Path.Join(_workspace, ".lip")));
  }

  [Theory]
  [InlineData]
  [InlineData("--help")]
  [InlineData("-h")]
  [InlineData("--version")]
  [InlineData("-v")]
  [InlineData("install", "--help")]
  [InlineData("config", "--help")]
  [InlineData("pkg", "--help")]
  [InlineData("pkg", "-h")]
  [InlineData("pkg", "view", "--help")]
  [InlineData("run", "--help")]
  public async Task HelpAndVersion_ExitZero(params string[] args) {
    (int exitCode, string stdout, _) = await Run(args);

    Assert.Equal(0, exitCode);
    Assert.NotEqual("", stdout.Trim());
    Assert.False(Directory.Exists(Path.Join(_workspace, ".lip")));
  }

  /// The package development commands are only in the command group pkg (protocol/lip.md,
  /// "Arguments"), and "versions" is part of lip pkg view.
  [Theory]
  [InlineData("init")]
  [InlineData("pack", "-o", "out.zip")]
  [InlineData("view", "x")]
  [InlineData("versions", "x")]
  [InlineData("migrate")]
  [InlineData("pkg", "versions", "x")]
  [InlineData("pkg", "bogus")]
  public async Task UnknownCommand_IsUsageError(params string[] args) {
    (int exitCode, string stdout, string stderr) = await Run(args);

    Assert.NotEqual(0, exitCode);
    Assert.Equal("", stdout);
    Assert.NotEqual("", stderr.Trim());
    Assert.Empty(Directory.EnumerateFileSystemEntries(_workspace));
  }

  [Fact]
  public async Task PkgView_LocalDirectory_WritesManifestAndNoVersionsAsJson() {
    // Arrange
    string package = Path.Join(_workspace, "pkg");
    Directory.CreateDirectory(package);
    await File.WriteAllTextAsync(
        Path.Join(package, "lip.toml"),
        """
        format = 4
        id = "example.com/pkg"
        version = "1.2.3"
        variants = { "" = [ { side = "server", platform = "win-x64" } ] }
        """,
        TestContext.Current.CancellationToken);

    // Act
    (int exitCode, string stdout, _) = await Run("pkg", "view", "./pkg");

    // Assert: one JSON object with exactly "manifest" and "versions", followed by a line break
    // (protocol/lip-pkg.md, "lip pkg view").
    Assert.Equal(0, exitCode);
    Assert.EndsWith("\n", stdout);
    using JsonDocument result = JsonDocument.Parse(stdout);
    Assert.Equal(
        ["manifest", "versions"], result.RootElement.EnumerateObject().Select(m => m.Name));
    Assert.Equal(
        "example.com/pkg",
        result.RootElement.GetProperty("manifest").GetProperty("id").GetString());
    Assert.Equal(0, result.RootElement.GetProperty("versions").GetArrayLength());
    Assert.False(Directory.Exists(Path.Join(_workspace, ".lip")));
  }

  [Fact]
  public async Task PkgView_DirectoryWithoutPackageFile_Fails() {
    // Arrange
    Directory.CreateDirectory(Path.Join(_workspace, "pkg"));

    // Act
    (int exitCode, string stdout, _) = await Run("pkg", "view", "./pkg");

    // Assert
    Assert.NotEqual(0, exitCode);
    Assert.Equal("", stdout);
  }

  /// protocol/lip-run.md, step 4.
  [Fact]
  public async Task Run_WithoutServer_Fails() {
    // Act
    (int exitCode, string stdout, string stderr) = await Run("run");

    // Assert
    Assert.NotEqual(0, exitCode);
    Assert.Equal("", stdout);
    Assert.Contains("bedrock_server_mod.exe", stderr);
  }

  /// protocol/lip-run.md, "Standard streams" and EXIT STATUS: the server reads the standard input
  /// of lip, and lip exits with the exit code of the server, without a failure report.
  [Fact]
  public async Task Run_Server_ReadsInputAndGivesItsExitCode() {
    // Arrange: a server that exits with the code it reads.
    string server = Path.Join(_workspace, "bedrock_server_mod.exe");
    if (OperatingSystem.IsWindows()) {
      File.Copy(Path.Join(Environment.SystemDirectory, "cmd.exe"), server);
    } else {
      Directory.CreateDirectory(Path.Join(_workspace, ".lip"));
      File.WriteAllText(
          Path.Join(_workspace, ".lip", "state.json"),
          """{ "format": 4, "side": "server", "platform": "linux-x64", "packages": [] }""");
      File.WriteAllText(server, "#!/bin/sh\nread l\nexit ${l#exit }\n");
      File.SetUnixFileMode(
          server, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    // Act
    (int exitCode, _, string stderr) = await Run(["run"], "exit 5\n");

    // Assert
    Assert.Equal(5, exitCode);
    Assert.DoesNotContain("error", stderr);
    Assert.False(Directory.Exists(Path.Join(_workspace, ".lip", "logs")));
  }

  private Task<(int ExitCode, string Stdout, string Stderr)> Run(params string[] args) =>
      Run(args, "");

  // The interface needs terminals (protocol/lip-tui.md); the standard streams here are pipes.
  [Fact]
  public async Task NoArgument_NotOnATerminal_WritesHelp() {
    (int exitCode, string stdout, string stderr) = await Run([]);

    Assert.Equal(0, exitCode);
    Assert.Contains("install", stdout);
    Assert.Contains("run", stdout);
    Assert.False(stdout.Contains((char)27));
    Assert.Equal("", stderr);
  }

  private async Task<(int ExitCode, string Stdout, string Stderr)> Run(string[] args, string input) {
    ProcessStartInfo startInfo = new(
        Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet") {
      WorkingDirectory = _workspace,
      RedirectStandardInput = true,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
    };
    startInfo.ArgumentList.Add(Path.Join(AppContext.BaseDirectory, "lip.dll"));
    foreach (string arg in args) {
      startInfo.ArgumentList.Add(arg);
    }

    using Process process = Process.Start(startInfo)!;
    await process.StandardInput.WriteAsync(input);
    process.StandardInput.Close();
    Task<string> stdout = process.StandardOutput.ReadToEndAsync();
    Task<string> stderr = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync(TestContext.Current.CancellationToken);
    return (process.ExitCode, await stdout, await stderr);
  }
}
