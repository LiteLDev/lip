using System.Diagnostics;

namespace Lip.Cli.Tests;

/// Invocation and usage errors (protocol/cli.md §1, §2), checked by running lip in an empty
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
  [InlineData("--bogus", "view", "--bogus", "x")]
  [InlineData("--bogus", "versions", "--bogus", "x")]
  [InlineData("--bogus", "pack", "-o", "out.zip", "--bogus")]
  [InlineData("--bogus", "config", "list", "--bogus")]
  [InlineData("--bogus", "config", "get", "--bogus", "x")]
  [InlineData("--bogus", "config", "delete", "--bogus", "x")]
  [InlineData("-x", "install", "-x", "foo")]
  public async Task UnknownOption_IsUsageError(string option, params string[] args) {
    (int exitCode, _, string stderr) = await Run(args);

    Assert.NotEqual(0, exitCode);
    Assert.Contains($"Unknown option '{option}'", stderr);
    Assert.DoesNotContain("missing", stderr, StringComparison.OrdinalIgnoreCase);
    Assert.False(Directory.Exists(Path.Join(_workspace, ".lip")));
    Assert.False(File.Exists(Path.Join(_workspace, "out.zip")));
  }

  [Theory]
  [InlineData("pack", "-o")]
  [InlineData("pack", "--output")]
  [InlineData("pack", "-o", "--help")]
  [InlineData("update", "--rid")]
  [InlineData("update", "--rid", "-n")]
  [InlineData("install", "x", "--rid")]
  [InlineData("uninstall", "x", "--rid")]
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
  [InlineData("pack", "-o", "out.zip", "--", "dir")]
  [InlineData("config", "list", "--", "extra")]
  [InlineData("list", "--", "--bogus")]
  [InlineData("install", "--", "foo")]
  [InlineData("uninstall", "--", "foo")]
  [InlineData("view", "--", "foo")]
  [InlineData("config", "get", "--", "key")]
  [InlineData("list", "--")]
  [InlineData("update", "-n", "--")]
  public async Task Separator_IsUsageError(params string[] args) {
    (int exitCode, _, string stderr) = await Run(args);

    Assert.NotEqual(0, exitCode);
    Assert.Contains("'--'", stderr);
    Assert.DoesNotContain("missing", stderr, StringComparison.OrdinalIgnoreCase);
    Assert.False(Directory.Exists(Path.Join(_workspace, ".lip")));
    Assert.Empty(Directory.EnumerateFileSystemEntries(_workspace));
  }

  [Theory]
  [InlineData("config")]
  [InlineData("cache")]
  public async Task BranchWithoutSubcommand_IsUsageError(string branch) {
    (int exitCode, _, string stderr) = await Run(branch);

    Assert.NotEqual(0, exitCode);
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
  public async Task HelpAndVersion_ExitZero(params string[] args) {
    (int exitCode, string stdout, _) = await Run(args);

    Assert.Equal(0, exitCode);
    Assert.NotEqual("", stdout.Trim());
    Assert.False(Directory.Exists(Path.Join(_workspace, ".lip")));
  }

  private async Task<(int ExitCode, string Stdout, string Stderr)> Run(params string[] args) {
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
    process.StandardInput.Close();
    Task<string> stdout = process.StandardOutput.ReadToEndAsync();
    Task<string> stderr = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync(TestContext.Current.CancellationToken);
    return (process.ExitCode, await stdout, await stderr);
  }
}
