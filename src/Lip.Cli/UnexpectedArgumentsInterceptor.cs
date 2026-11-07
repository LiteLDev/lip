using Spectre.Console.Cli;

namespace Lip.Cli;

/// An argument a command does not take is a usage error (protocol/cli.md §1, §2): an unknown
/// option, or "--" and any argument after it, which lip does not define. Spectre.Console.Cli
/// parses in relaxed mode and leaves unknown options in the remaining arguments; this rejects them
/// before the command runs. "--" is rejected before parsing, by FindSeparator.
public sealed class UnexpectedArgumentsInterceptor : ICommandInterceptor {
  public void Intercept(CommandContext context, CommandSettings settings) {
    if (Find(context.Remaining) is { } usageError) {
      throw usageError;
    }
  }

  /// Returns the usage error for "--" in the arguments of lip, or null.
  ///
  /// Spectre.Console.Cli always reads "--" as the separator, never as an option value, and stops
  /// parsing there: after a branch such as "lip config --", it applies no default command and
  /// writes the help of the branch to standard output without an error, so no interceptor or
  /// exception handler would report it. lip checks for it before parsing instead.
  public static UsageException? FindSeparator(string[] args) {
    int separator = Array.IndexOf(args, "--");
    if (separator < 0) {
      return null;
    }
    return separator + 1 < args.Length
        ? new UsageException(
            $"Unexpected argument '{args[separator + 1]}': lip takes no arguments after '--'.")
        : new UsageException("Unexpected argument '--': lip takes no '--' separator.");
  }

  /// Returns the usage error for the arguments of a command that were not mapped to it, or null.
  ///
  /// Spectre.Console.Cli binds the arguments before any interceptor runs, so an unknown option can
  /// also surface as an error about a missing required argument: in relaxed mode it takes the next
  /// argument as its value. The exception handler calls this to report the actual cause instead.
  public static UsageException? Find(IRemainingArguments remaining) {
    if (remaining.Parsed.FirstOrDefault() is { } option) {
      return new UsageException($"Unknown option '{option.Key}'.");
    }
    return null;
  }
}

/// A usage error found by lip rather than by Spectre.Console.Cli, which reports its own as
/// CommandAppException.
public sealed class UsageException(string message) : Exception(message);
