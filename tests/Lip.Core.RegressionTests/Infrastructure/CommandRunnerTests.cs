using Lip.Core.Infrastructure;

namespace Lip.Core.RegressionTests.Infrastructure;

public class CommandRunnerTests {
  // A platform whose hook commands run without Wine wherever the test runs
  // (protocol/lip-workspace.md, "Running programs").
  private static readonly string _hostPlatform =
      OperatingSystem.IsWindows() ? "win-x64" : "linux-x64";

  [Fact]
  public async Task Run_SuccessfulCommand_DoesNotThrow() {
    CommandRunner runner = new();

    await runner.Run("echo hello", Path.GetTempPath(), _hostPlatform);
  }

  [Fact]
  public async Task Run_InWorkingDirectory_RunsThere() {
    CommandRunner runner = new();
    string directory = Directory.CreateTempSubdirectory().FullName;

    try {
      await runner.Run(OperatingSystem.IsWindows() ? "echo x> marker" : "echo x > marker", directory, _hostPlatform);

      Assert.True(File.Exists(Path.Join(directory, "marker")));
    }
    finally {
      Directory.Delete(directory, recursive: true);
    }
  }

  [Fact]
  public async Task Run_QuotedPaths_ReachShellAsWritten() {
    CommandRunner runner = new();
    string directory = Directory.CreateTempSubdirectory().FullName;

    try {
      await File.WriteAllTextAsync(Path.Join(directory, "a b.txt"), "content", TestContext.Current.CancellationToken);

      await runner.Run(
          OperatingSystem.IsWindows() ? "copy \"a b.txt\" \"c d.txt\"" : "cp \"a b.txt\" \"c d.txt\"",
          directory,
          _hostPlatform);

      Assert.Equal("content", await File.ReadAllTextAsync(Path.Join(directory, "c d.txt"), TestContext.Current.CancellationToken));
    }
    finally {
      Directory.Delete(directory, recursive: true);
    }
  }

  [Fact]
  public async Task Run_QuotedArgument_KeepsQuotes() {
    CommandRunner runner = new();
    string directory = Directory.CreateTempSubdirectory().FullName;

    try {
      await runner.Run(
          OperatingSystem.IsWindows() ? "echo \"hello\"> out.txt" : "echo '\"hello\"' > out.txt",
          directory,
          _hostPlatform);

      Assert.Equal("\"hello\"", (await File.ReadAllTextAsync(Path.Join(directory, "out.txt"), TestContext.Current.CancellationToken)).Trim());
    }
    finally {
      Directory.Delete(directory, recursive: true);
    }
  }

  [Fact]
  public async Task Run_CommandStartingAndEndingWithQuotes_KeepsQuotes() {
    CommandRunner runner = new();
    string directory = Directory.CreateTempSubdirectory().FullName;

    try {
      await File.WriteAllTextAsync(Path.Join(directory, "a b.txt"), "content", TestContext.Current.CancellationToken);

      string program = OperatingSystem.IsWindows()
          ? Path.Join(Environment.SystemDirectory, "findstr.exe") + "\" \"content"
          : "cat";
      await runner.Run($"\"{program}\" \"a b.txt\"> \"c d.txt\"", directory, _hostPlatform);

      Assert.Equal("content", (await File.ReadAllTextAsync(Path.Join(directory, "c d.txt"), TestContext.Current.CancellationToken)).Trim());
    }
    finally {
      Directory.Delete(directory, recursive: true);
    }
  }
}
