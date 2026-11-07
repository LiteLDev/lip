using Spectre.Console.Cli;

namespace Lip.Cli;

/// An argument a command does not take is a usage error (protocol/cli.md §1, §2): an unknown
/// option, or any argument after "--", which lip does not define. Spectre.Console.Cli parses in
/// relaxed mode and leaves these in the remaining arguments; this rejects them before the command
/// runs.
public sealed class UnexpectedArgumentsInterceptor : ICommandInterceptor {
  public void Intercept(CommandContext context, CommandSettings settings) {
    if (context.Remaining.Raw.Count > 0) {
      throw new UsageException(
          $"Unexpected argument '{context.Remaining.Raw[0]}': lip takes no arguments after '--'.");
    }
    if (context.Remaining.Parsed.FirstOrDefault() is { } option) {
      throw new UsageException($"Unknown option '{option.Key}'.");
    }
  }
}

/// A usage error found by lip rather than by Spectre.Console.Cli, which reports its own as
/// CommandAppException.
public sealed class UsageException(string message) : Exception(message);
