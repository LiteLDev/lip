using System.Diagnostics;

namespace Lip.Cli.Tests;

/// Runs the lip that was built, as lip.dll, with an endpoint of its own for the daemon
/// (protocol/lip-daemon.md), so that tests neither use nor leave the daemon of the user; the
/// daemon that the tests start is stopped when the fixture is disposed.
public sealed class LipProcess : IDisposable {
  private readonly string? _socketDirectory;

  public LipProcess() {
    if (OperatingSystem.IsWindows()) {
      Endpoint = $"lip-test-{Guid.NewGuid():N}";
    } else {
      // Short enough for the path of a Unix domain socket.
      _socketDirectory = Path.Join(Path.GetTempPath(), $"lip-{Guid.NewGuid():N}"[..12]);
      Endpoint = Path.Join(_socketDirectory, "daemon.sock");
    }
  }

  /// The name of the pipe, or the path of the socket.
  public string Endpoint { get; }

  /// Starts lip with the arguments in the working directory, with the endpoint of the fixture;
  /// its standard streams are redirected.
  public Process Start(
      IEnumerable<string> args,
      string workingDirectory,
      IReadOnlyDictionary<string, string>? environment = null) {
    // LIP_TEST_EXECUTABLE runs the tests with a published lip instead of the one built here. A
    // Release build ignores LIP_DAEMON_ENDPOINT and uses the daemon of the user, which the fixture
    // stops: it is for CI, where the user has no daemon of their own.
    string? executable = Environment.GetEnvironmentVariable("LIP_TEST_EXECUTABLE");
    ProcessStartInfo startInfo = new(
        executable ?? Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet") {
      WorkingDirectory = workingDirectory,
      RedirectStandardInput = true,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
    };
    startInfo.Environment["LIP_DAEMON_ENDPOINT"] = Endpoint;
    foreach ((string name, string value) in environment ?? new Dictionary<string, string>()) {
      startInfo.Environment[name] = value;
    }

    if (executable is null) {
      startInfo.ArgumentList.Add(Path.Join(AppContext.BaseDirectory, "lip.dll"));
    }
    foreach (string arg in args) {
      startInfo.ArgumentList.Add(arg);
    }

    return Process.Start(startInfo)!;
  }

  /// Runs lip to its end with input as its standard input.
  public async Task<(int ExitCode, string Stdout, string Stderr)> Run(
      IEnumerable<string> args,
      string workingDirectory,
      string input = "",
      IReadOnlyDictionary<string, string>? environment = null) {
    using Process process = Start(args, workingDirectory, environment);
    await process.StandardInput.WriteAsync(input);
    process.StandardInput.Close();
    Task<string> stdout = process.StandardOutput.ReadToEndAsync();
    Task<string> stderr = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync(TestContext.Current.CancellationToken);
    return (process.ExitCode, await stdout, await stderr);
  }

  public void Dispose() {
    string directory = Directory.CreateTempSubdirectory("lip-cli-tests-").FullName;
    try {
      using Process stop = Start(["daemon", "stop"], directory);
      stop.StandardInput.Close();
      stop.WaitForExit(TimeSpan.FromSeconds(30));
    }
    finally {
      Directory.Delete(directory, recursive: true);
      if (_socketDirectory is not null && Directory.Exists(_socketDirectory)) {
        Directory.Delete(_socketDirectory, recursive: true);
      }
    }
  }
}
