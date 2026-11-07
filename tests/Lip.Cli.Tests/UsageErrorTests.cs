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
  [InlineData("list", "--bogus")]
  [InlineData("uninstall", "foo", "--dryrun")]
  [InlineData("pack", "-o", "out.zip", "--bogus")]
  [InlineData("config", "list", "--bogus")]
  public async Task UnknownOption_IsUsageError(params string[] args) {
    (int exitCode, _, string stderr) = await Run(args);

    Assert.NotEqual(0, exitCode);
    Assert.Contains("--", stderr);
    Assert.False(Directory.Exists(Path.Join(_workspace, ".lip")));
    Assert.False(File.Exists(Path.Join(_workspace, "out.zip")));
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
