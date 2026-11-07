using Lip.Core.Infrastructure;

namespace Lip.Core.RegressionTests.Infrastructure;

public class CommandRunnerTests {
  [Fact]
  public async Task Run_SuccessfulCommand_DoesNotThrow() {
    CommandRunner runner = new();

    await runner.Run("echo hello", Path.GetTempPath());
  }

  [Fact]
  public async Task Run_InWorkingDirectory_RunsThere() {
    CommandRunner runner = new();
    string directory = Directory.CreateTempSubdirectory().FullName;

    try {
      await runner.Run(OperatingSystem.IsWindows() ? "echo x> marker" : "echo x > marker", directory);

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
      await File.WriteAllTextAsync(Path.Join(directory, "a b.txt"), "content");

      await runner.Run(
          OperatingSystem.IsWindows() ? "copy \"a b.txt\" \"c d.txt\"" : "cp \"a b.txt\" \"c d.txt\"",
          directory);

      Assert.Equal("content", await File.ReadAllTextAsync(Path.Join(directory, "c d.txt")));
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
          directory);

      Assert.Equal("\"hello\"", (await File.ReadAllTextAsync(Path.Join(directory, "out.txt"))).Trim());
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
      await File.WriteAllTextAsync(Path.Join(directory, "a b.txt"), "content");

      string program = OperatingSystem.IsWindows()
          ? Path.Join(Environment.SystemDirectory, "findstr.exe") + "\" \"content"
          : "cat";
      await runner.Run($"\"{program}\" \"a b.txt\"> \"c d.txt\"", directory);

      Assert.Equal("content", (await File.ReadAllTextAsync(Path.Join(directory, "c d.txt"))).Trim());
    }
    finally {
      Directory.Delete(directory, recursive: true);
    }
  }
}
