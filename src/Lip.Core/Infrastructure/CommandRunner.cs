using CliWrap;

namespace Lip.Core.Infrastructure;

public interface ICommandRunner {
  /// Runs a command with the platform shell in the current working directory.
  Task Run(string command);

  /// Runs a command with the platform shell (cmd /c on Windows, sh -c elsewhere) in the given
  /// working directory, or in the current one when it is null.
  Task Run(string command, string? workingDirectory);
}

public class CommandRunner : ICommandRunner {
  public Task Run(string command) => Run(command, workingDirectory: null);

  public async Task Run(string command, string? workingDirectory) {
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
