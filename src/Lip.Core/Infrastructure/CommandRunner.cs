using CliWrap;

namespace Lip.Core.Infrastructure;

/// Thrown when a hook command cannot be started or exits with a non-zero code.
public class CommandFailedException(string message, Exception? inner = null)
    : Exception(message, inner);

public interface ICommandRunner {
  /// Runs the hook command in the workspace whose root is workingDirectory, for the platform of
  /// its environment (protocol/lip-workspace.md, "Terms" and "Running programs").
  Task Run(string command, string workingDirectory, string platform);
}

/// Runs hook commands, writing their standard output and standard error to the stream that
/// openOutput opens for each command: the standard error of lip when it is not given.
public class CommandRunner(Func<Stream>? openOutput = null) : ICommandRunner {
  private readonly Func<Stream> _openOutput = openOutput ?? Console.OpenStandardError;

  public async Task Run(string command, string workingDirectory, string platform) {
    using Stream stdErrStream = _openOutput();
    PipeTarget output = PipeTarget.Merge(
        PipeTarget.ToStream(stdErrStream),
        PipeTarget.ToDelegate(line => RunTrace.Write("hook", $"| {line}")));

    ProgramStartInfo start;
    try {
      start = WorkspaceProgram.HookCommand(
          command, ProgramHost.Current(platform, workingDirectory));
    }
    catch (InvalidOperationException ex) {
      RunTrace.Write("hook", $"'{command}' could not be started: {ex.Message}");
      throw WithData(
          new CommandFailedException(
              $"Could not run the command in '{workingDirectory}' for the platform {platform}:"
              + $" {ex.Message}",
              ex),
          command,
          workingDirectory,
          exitCode: null);
    }

    string program = start.FileName;
    RunTrace.Write("hook", $"Running '{command}' for {platform} with {program}"
        + $" {string.Join(' ', start.Arguments.Take(start.Arguments.Count - 1))}"
        + $" in {workingDirectory}"
        + string.Concat(start.EnvironmentOverrides.Select(v => $", {v.Key}={v.Value}")));
    CommandResult result;
    try {
      Command cli = Cli.Wrap(start.FileName);
      cli = start.VerbatimArguments
          ? cli.WithArguments(start.JoinedArguments)
          : cli.WithArguments(start.Arguments);
      result = await cli
          .WithWorkingDirectory(start.WorkingDirectory)
          .WithEnvironmentVariables(start.EnvironmentOverrides.ToDictionary(
              v => v.Key, string? (v) => v.Value))
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
              $"Could not start {program} in '{workingDirectory}' to run the command.", ex),
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
