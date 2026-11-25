using System.Diagnostics;
using System.Text.Json;

namespace Lip.Cli.Tests;

/// Invocation and usage errors (protocol/lip.md, "Terms"), checked by running lip in an empty
/// directory: a usage error writes a message and exits with a non-zero code, without a failure
/// report below .lip/logs. The commands are carried out by a daemon (protocol/lip-daemon.md).
public sealed class UsageErrorTests(LipProcess lip) : IDisposable, IClassFixture<LipProcess> {
  private readonly string _workspace = Directory.CreateTempSubdirectory("lip-cli-tests-").FullName;
  private readonly LipProcess _lip = lip;

  public void Dispose() {
    Directory.Delete(_workspace, recursive: true);
  }

  [Theory]
  [InlineData("--bogus", "list", "--bogus")]
  [InlineData("--dryrun", "uninstall", "foo", "--dryrun")]
  [InlineData("--dryrun", "uninstall", "--dryrun", "foo")]
  [InlineData("--bogus", "install", "--bogus", "x")]
  [InlineData("--bogus", "pkg", "init", "--bogus")]
  [InlineData("--bogus", "pkg", "migrate", "--bogus")]
  [InlineData("--bogus", "config", "list", "--bogus")]
  [InlineData("--bogus", "run", "--bogus")]
  // lip has no --platform: the platform is always win-x64 (protocol/lip.md, "Environment").
  [InlineData("--platform", "run", "--platform", "win-x64")]
  [InlineData("--platform", "install", "--platform", "win-x64", "x")]
  [InlineData("--platform", "install", "x", "--platform", "linux-x64")]
  [InlineData("--platform", "uninstall", "x", "--platform", "win-x64")]
  [InlineData("--platform", "update", "--platform", "win-x64")]
  [InlineData("--platform", "update", "--platform")]
  [InlineData("--platform", "install", "x", "--platform", "", "-n")]
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
  }

  [Theory]
  [InlineData("setup", "--game")]
  [InlineData("setup", "--game", "1.21.0", "--loader")]
  [InlineData("setup", "--game", "--help")]
  // An empty value is no value (protocol/lip.md, "Arguments"), so -h does not make it help.
  [InlineData("setup", "--game", "")]
  [InlineData("setup", "--game", "1.21.0", "--build", "", "--help")]
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
  [InlineData("setup", "--game", "1.21.0", "--", "dir")]
  [InlineData("config", "list", "--", "extra")]
  [InlineData("list", "--", "--bogus")]
  [InlineData("install", "--", "foo")]
  [InlineData("uninstall", "--", "foo")]
  [InlineData("pkg", "init", "--", "foo")]
  [InlineData("pkg", "--")]
  [InlineData("pkg", "--", "init")]
  [InlineData("config", "get", "--", "key")]
  [InlineData("list", "--")]
  [InlineData("update", "-n", "--")]
  [InlineData("config", "--")]
  [InlineData("config", "--", "get")]
  [InlineData("config", "--", "x")]
  [InlineData("cache", "--")]
  [InlineData("cache", "--", "clean")]
  [InlineData("setup", "--game", "--")]
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

  /// The values of the options of lip setup (protocol/lip-setup.md, OPTIONS).
  [Theory]
  [InlineData("--game", "setup")]
  [InlineData("--game", "setup", "-n")]
  [InlineData("not a version", "setup", "--game", "v1.21.0")]
  [InlineData("not a build", "setup", "--game", "1.21.0", "--build", "bsd")]
  [InlineData("not a loader", "setup", "--game", "1.21.0", "--loader", "fabric")]
  [InlineData("not a loader", "setup", "--game", "1.21.0", "--loader", "levilamina@latest")]
  [InlineData("cannot be given together", "setup", "--game", "1.21.0", "--loader", "levilamina",
      "--no-loader")]
  public async Task SetupOptionValue_IsUsageError(string expected, params string[] args) {
    (int exitCode, string stdout, string stderr) = await Run(args);

    Assert.NotEqual(0, exitCode);
    Assert.Equal("", stdout);
    Assert.Contains(expected, stderr);
    Assert.Empty(Directory.EnumerateFileSystemEntries(_workspace));
  }

  [Fact]
  public async Task RunDetachWithoutManaged_IsUsageError() {
    // protocol/lip-run.md, OPTIONS
    (int exitCode, string stdout, string stderr) = await Run("run", "--detach");

    Assert.NotEqual(0, exitCode);
    Assert.Equal("", stdout);
    Assert.Contains("--detach", stderr);
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
  [InlineData("setup", "--help")]
  [InlineData("config", "--help")]
  [InlineData("pkg", "--help")]
  [InlineData("pkg", "-h")]
  [InlineData("pkg", "init", "--help")]
  [InlineData("run", "--help")]
  public async Task HelpAndVersion_ExitZero(params string[] args) {
    (int exitCode, string stdout, _) = await Run(args);

    Assert.Equal(0, exitCode);
    Assert.NotEqual("", stdout.Trim());
    Assert.False(Directory.Exists(Path.Join(_workspace, ".lip")));
  }

  /// lip --uri with a link that is not valid fails before it does anything, and so does one
  /// with a valid link whose standard streams are not terminals (protocol/lip-uri.md).
  [Theory]
  [InlineData("lip://install/https://evil.example/x.zip", "cannot open the link")]
  [InlineData("lip://install/github.com/a/b?version=branch:main", "cannot open the link")]
  [InlineData("lip://uninstall/github.com/a/b", "cannot open the link")]
  [InlineData("lip://install/github.com/a/b?version=1.0.0", "terminals")]
  public async Task Link_ThatCannotBeOpened_Fails(string link, string expected) {
    (int exitCode, string stdout, string stderr) = await Run("--uri", link);

    Assert.NotEqual(0, exitCode);
    Assert.Equal("", stdout);
    Assert.Contains(expected, stderr);
    Assert.Empty(Directory.EnumerateFileSystemEntries(_workspace));
  }

  /// The package development commands are only in the command group pkg (protocol/lip.md,
  /// "Arguments"), which has no pack, view or versions (protocol/lip-pkg.md).
  [Theory]
  [InlineData("init")]
  [InlineData("pack", "-o", "out.zip")]
  [InlineData("view", "x")]
  [InlineData("versions", "x")]
  [InlineData("migrate")]
  [InlineData("pkg", "versions", "x")]
  [InlineData("pkg", "pack", "-o", "out.zip")]
  [InlineData("pkg", "view", "x")]
  [InlineData("pkg", "bogus")]
  public async Task UnknownCommand_IsUsageError(params string[] args) {
    (int exitCode, string stdout, string stderr) = await Run(args);

    Assert.NotEqual(0, exitCode);
    Assert.Equal("", stdout);
    Assert.NotEqual("", stderr.Trim());
    Assert.Empty(Directory.EnumerateFileSystemEntries(_workspace));
  }

  /// protocol/lip-run.md, step 4.
  [Fact]
  public async Task Run_WithoutServer_Fails() {
    // Act
    (int exitCode, string stdout, string stderr) = await Run("run");

    // Assert
    Assert.NotEqual(0, exitCode);
    Assert.Equal("", stdout);
    Assert.Contains("lip setup", stderr);
  }

  /// protocol/lip-run.md, "Standard streams" and EXIT STATUS: the server reads the standard input
  /// of lip, and lip exits with the exit code of the server, without a failure report.
  [Fact]
  public async Task Run_Server_ReadsInputAndGivesItsExitCode() {
    Assert.SkipUnless(
        OperatingSystem.IsWindows() || OperatingSystem.IsLinux()
            && System.Runtime.InteropServices.RuntimeInformation.OSArchitecture
                == System.Runtime.InteropServices.Architecture.X64,
        "lip runs a server of the test without Wine on Windows and on Linux on x64 only.");
    // Arrange: a server that exits with the code it reads; on Windows, LeviLamina's server of
    // the Windows game, elsewhere the Linux game (protocol/lip-run.md).
    bool windows = OperatingSystem.IsWindows();
    string server = Path.Join(_workspace, windows ? "bedrock_server_mod.exe" : "bedrock_server");
    Directory.CreateDirectory(Path.Join(_workspace, ".lip"));
    File.WriteAllText(Path.Join(_workspace, ".lip", "state.json"), $$"""
        { "format": 4, "side": "server", "platform": "win-x64", "packages": [
          {{Installed("github.com/LiteLDev/bds", windows ? "" : "linux")}}
          {{(windows ? ", " + Installed("github.com/LiteLDev/LeviLamina", "") : "")}} ] }
        """);
    if (windows) {
      File.Copy(Path.Join(Environment.SystemDirectory, "cmd.exe"), server);
    } else {
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

  /// A result written as JSON reads as its text does: what JSON does not require to be
  /// escaped, such as the "+" of a version, is written as it is
  /// (protocol/lip.md, "Output": the result as JSON).
  [Fact]
  public async Task Json_OfAResult_EscapesOnlyWhatJsonRequires() {
    // Arrange
    Directory.CreateDirectory(Path.Join(_workspace, ".lip"));
    File.WriteAllText(Path.Join(_workspace, ".lip", "state.json"), """
        { "format": 4, "side": "server", "platform": "win-x64", "packages": [
          { "manifest": { "format": 4, "id": "example.com/ab", "version": "1.0.0+build.5",
              "variants": { "x": [ { "side": "server", "platform": "win-x64" } ] } },
            "variant": "x", "explicit": true, "ref": "example.com/ab#x", "files": [] } ] }
        """);

    // Act
    (int exitCode, string stdout, string stderr) = await Run("list", "--json");

    // Assert
    Assert.True(exitCode == 0, stderr);
    Assert.Contains("\"example.com/ab#x@1.0.0+build.5\"", stdout);
    Assert.DoesNotContain("\\u", stdout);
    using JsonDocument list = JsonDocument.Parse(stdout);
    Assert.Equal(
        "example.com/ab#x@1.0.0+build.5",
        list.RootElement.EnumerateObject().First().Value[0].GetString());
  }

  /// An entry of the state file for a package of an id and variant, without files.
  private static string Installed(string id, string variant) => $$"""
      { "manifest": { "format": 4, "id": "{{id}}", "version": "1.0.0",
          "variants": { "{{variant}}": [ { "side": "server", "platform": "win-x64" } ] } },
        "variant": "{{variant}}", "explicit": true, "ref": "{{id}}", "files": [] }
      """;

  private Task<(int ExitCode, string Stdout, string Stderr)> Run(params string[] args) =>
      Run(args, "");

  /// protocol/lip-config.md: an empty value is an operand, not a missing one; an empty
  /// github_proxy is written and read back as "", and a deleted one has no value, null in JSON.
  [Fact]
  public async Task Config_EmptyGithubProxy_IsSetAndToldApartFromNoValue() {
    // On Windows the config directory is the known folder RoamingAppData, which no environment
    // variable redirects: the test would write the configuration of the user.
    Assert.SkipWhen(
        OperatingSystem.IsWindows(), "The config directory cannot be redirected on Windows.");

    // Arrange
    string configHome = Path.Join(_workspace, "config-home");
    string configPath = Path.Join(configHome, "lip", "config.toml");

    // Act & Assert
    Assert.Equal(0, (await Run(["config", "set", "github_proxy", ""], "", configHome)).ExitCode);
    Assert.Equal("format = 4\ngithub_proxy = \"\"\n", File.ReadAllText(configPath));
    Assert.Equal(
        "\"\"",
        (await Run(["config", "get", "github_proxy", "--json"], "", configHome)).Stdout.Trim());

    Assert.Equal(0, (await Run(["config", "delete", "github_proxy"], "", configHome)).ExitCode);
    Assert.Equal("format = 4\n", File.ReadAllText(configPath));
    (int exitCode, string stdout, _) = await Run(["config", "list", "--json"], "", configHome);
    Assert.Equal(0, exitCode);
    using JsonDocument list = JsonDocument.Parse(stdout);
    Assert.Equal(JsonValueKind.Null, list.RootElement.GetProperty("github_proxy").ValueKind);
  }

  /// protocol/lip-config.md: with --workspace, the keys are those of .lip/config.toml.
  [Fact]
  public async Task Config_Workspace_WritesLipConfigToml() {
    // Arrange
    string configHome = Path.Join(_workspace, "config-home");

    // Act & Assert
    Assert.Equal(
        0,
        (await Run(["config", "set", "restart", "on-failure", "--workspace"], "", configHome))
            .ExitCode);
    Assert.Equal(
        "format = 4\nrestart = \"on-failure\"\n",
        File.ReadAllText(Path.Join(_workspace, ".lip", "config.toml")));
    Assert.Equal(
        "\"on-failure\"",
        (await Run(["config", "get", "--workspace", "restart", "--json"], "", configHome))
            .Stdout.Trim());

    (int invalid, _, string invalidError) =
        await Run(["config", "set", "restart", "always", "--workspace"], "", configHome);
    (int otherScope, _, string otherScopeError) = await Run(
        ["config", "set", "github_proxy", "https://x.example/", "--workspace"], "", configHome);
    Assert.NotEqual(0, invalid);
    Assert.Contains("on-failure", invalidError);
    Assert.NotEqual(0, otherScope);
    Assert.Contains("without --workspace", otherScopeError);
    Assert.False(Directory.Exists(Path.Join(configHome, "lip")));
  }

  // The interface needs terminals (protocol/lip-tui.md); the standard streams here are pipes.
  [Fact]
  public async Task NoArgument_NotOnATerminal_WritesHelp() {
    (int exitCode, string stdout, string stderr) = await Run([]);

    Assert.Equal(0, exitCode);
    Assert.Contains("install", stdout);
    Assert.Contains("run", stdout);
    Assert.Equal("", stderr);
  }

  private async Task<(int ExitCode, string Stdout, string Stderr)> Run(
      string[] args, string input, string? configHome = null) =>
      // The config directory is $XDG_CONFIG_HOME/lip, except on Windows.
      await _lip.Run(
          args,
          _workspace,
          input,
          configHome is null
              ? null
              : new Dictionary<string, string> { ["XDG_CONFIG_HOME"] = configHome });
}
