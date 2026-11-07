using Spectre.Console.Cli;

namespace Lip.Cli;

/// An argument a command does not take is a usage error (protocol/cli.md §1, §2): an unknown
/// option, or "--" and any argument after it, which lip does not define. Spectre.Console.Cli
/// parses in relaxed mode and leaves these in the remaining arguments; this rejects them before the
/// command runs.
public sealed class UnexpectedArgumentsInterceptor : ICommandInterceptor {
  public void Intercept(CommandContext context, CommandSettings settings) {
    if (Find(context.Arguments, context.Remaining) is { } usageError) {
      throw usageError;
    }
  }

  /// Returns the usage error for the arguments of a command that were not mapped to it, or null.
  ///
  /// Spectre.Console.Cli binds the arguments before any interceptor runs, so an unexpected argument
  /// can also surface as an error about a missing required argument: in relaxed mode an unknown
  /// option takes the next argument as its value, and the arguments after "--" are not mapped. The
  /// exception handler calls this to report the actual cause instead.
  public static UsageException? Find(IReadOnlyList<string> args, IRemainingArguments remaining) {
    if (remaining.Raw.Count > 0) {
      return new UsageException(
          $"Unexpected argument '{remaining.Raw[0]}': lip takes no arguments after '--'.");
    }
    // Spectre.Console.Cli always reads "--" as the separator, never as a value.
    if (args.Contains("--")) {
      return new UsageException("Unexpected argument '--': lip takes no '--' separator.");
    }
    if (remaining.Parsed.FirstOrDefault() is { } option) {
      return new UsageException($"Unknown option '{option.Key}'.");
    }
    return null;
  }
}

/// A usage error found by lip rather than by Spectre.Console.Cli, which reports its own as
/// CommandAppException.
public sealed class UsageException(string message) : Exception(message);
