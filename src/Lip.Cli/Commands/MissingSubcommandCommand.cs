using Spectre.Console.Cli;

namespace Lip.Cli.Commands;

/// The default command of a branch such as "lip config": a branch without a subcommand is a usage
/// error, reported on standard error (refs/cli.md §1, §1.3).
public class MissingSubcommandCommand : Command<MissingSubcommandCommand.Settings> {
  public class Settings : CommandSettings {
  }

  public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken) {
    string branch = string.Join(' ', context.Arguments);
    throw new InvalidOperationException(
        $"'lip {branch}' needs a subcommand. Run 'lip {branch} --help' for the list.");
  }
}
