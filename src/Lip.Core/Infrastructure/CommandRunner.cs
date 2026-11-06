using CliWrap;

namespace Lip.Core.Infrastructure;

/// Thrown when a hook command cannot be started or exits with a non-zero code.
public class CommandFailedException(string message, Exception? inner = null)
    : Exception(message, inner);

public interface ICommandRunner {
  /// Runs the command with the given working directory.
  Task Run(string command, string workingDirectory);
}

public class CommandRunner : ICommandRunner {
  public async Task Run(string command, string workingDirectory) {
    using Stream stdErrStream = Console.OpenStandardError();
    PipeTarget output = PipeTarget.Merge(
        PipeTarget.ToStream(stdErrStream),
        PipeTarget.ToDelegate(line => RunTrace.Write("hook", $"| {line}")));

    string shell = OperatingSystem.IsWindows() ? "cmd.exe" : "sh";
    RunTrace.Write("hook", $"Running '{command}' with {shell} in {workingDirectory}");
    CommandResult result;
    try {
      result = await Cli.Wrap(shell)
          .WithArguments([
              OperatingSystem.IsWindows() ? "/c" : "-c",
                  command
          ])
          .WithWorkingDirectory(workingDirectory)
          .WithStandardInputPipe(PipeSource.Null)
          .WithStandardOutputPipe(output)
          .WithStandardErrorPipe(output)
          .WithValidation(CommandResultValidation.None)
          .ExecuteAsync();
    }
    catch (Exception ex) when (ex is not OperationCanceledException) {
      RunTrace.Write("hook", $"'{command}' could not be started: {ex.GetType().Name}: {ex.Message}");
      throw WithData(
          new CommandFailedException(
              $"Could not start {shell} in '{workingDirectory}' to run the command.", ex),
          command,
          workingDirectory,
          exitCode: null);
    }

    RunTrace.Write("hook", $"'{command}' exited with code {result.ExitCode}"
        + $" after {result.RunTime.TotalMilliseconds:0} ms");
    if (!result.IsSuccess) {
      throw WithData(
          new CommandFailedException(
              $"The command exited with code {result.ExitCode}. Its output is shown above and in"
              + " the trace of the full report."),
          command,
          workingDirectory,
          result.ExitCode);
    }
  }

  // The details, for the full report.
  private static CommandFailedException WithData(
      CommandFailedException ex, string command, string workingDirectory, int? exitCode) {
    ex.Data["Command"] = command;
    ex.Data["Working directory"] = workingDirectory;
    if (exitCode is int code) {
      ex.Data["Exit code"] = code;
    }

    return ex;
  }
}
