using Spectre.Console.Cli;

namespace Lip.Cli;

/// An argument a command does not take is a usage error (protocol/lip.md, "Terms"): an unknown
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

  /// Returns the usage error for an option of a command that takes a value and is given an empty
  /// one as the next argument, such as 'lip update --platform ""', or null (protocol/lip.md,
  /// "Arguments": a value must not be empty, and such an option has none). Spectre.Console.Cli
  /// binds the empty argument as the value, and writes the help without validating the settings
  /// when -h or --help is given too, which this usage error does not allow; so lip checks for it
  /// before parsing. "--platform=" is already rejected by Spectre.Console.Cli.
  public static UsageException? FindEmptyValue(string[] args) {
    string[] valueOptions = args switch {
      ["install" or "uninstall" or "update", ..] => ["--platform"],
      ["pkg", "pack", ..] => ["-o", "--output"],
      _ => [],
    };
    for (int i = 0; i + 1 < args.Length; i++) {
      if (valueOptions.Contains(args[i]) && args[i + 1] == "") {
        return new UsageException(
            $"Option '{args[i]}' has no value: its value must not be empty.");
      }
    }
    return null;
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
