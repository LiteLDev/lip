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
}
