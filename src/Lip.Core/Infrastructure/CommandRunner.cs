using CliWrap;

namespace Lip.Core.Infrastructure;

public interface ICommandRunner {
  Task Run(string command);

  /// Runs the command with the given working directory instead of the process's.
  Task Run(string command, string workingDirectory);
}

public class CommandRunner : ICommandRunner {
  public Task Run(string command) => RunCore(command, workingDirectory: null);

  public Task Run(string command, string workingDirectory) => RunCore(command, workingDirectory);

  private static async Task RunCore(string command, string? workingDirectory) {
    using Stream stdErrStream = Console.OpenStandardError();

    Command cli = Cli.Wrap(OperatingSystem.IsWindows() ? "cmd.exe" : "sh")
        .WithArguments([
            OperatingSystem.IsWindows() ? "/c" : "-c",
                command
        ])
        .WithStandardInputPipe(PipeSource.Null)
        .WithStandardOutputPipe(PipeTarget.ToStream(stdErrStream))
        .WithStandardErrorPipe(PipeTarget.ToStream(stdErrStream));

    if (workingDirectory is not null) {
      cli = cli.WithWorkingDirectory(workingDirectory);
    }

    CommandResult result = await cli.ExecuteAsync();

    if (!result.IsSuccess) {
      throw new InvalidOperationException($"Command failed with exit code {result.ExitCode}");
    }
  }
}
