using System.Diagnostics;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Lip.Cli.Commands;

/// The default command of a branch such as "lip config": a branch without a subcommand is a usage
/// error, reported on standard error without a failure report (protocol/cli.md §2). A failed
/// validation is reported as a usage error, so the command never runs.
public class MissingSubcommandCommand : Command<MissingSubcommandCommand.Settings> {
  public class Settings : CommandSettings {
  }

  public override ValidationResult Validate(CommandContext context, Settings settings) {
    string branch = string.Join(' ', context.Arguments);
    return ValidationResult.Error(
        $"'lip {branch}' needs a subcommand. Run 'lip {branch} --help' for the list.");
  }

  public override int Execute(
      CommandContext context, Settings settings, CancellationToken cancellationToken) =>
      throw new UnreachableException();
}
