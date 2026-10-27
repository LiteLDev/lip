using System.ComponentModel;
using Lip.Core.Infrastructure;
using Lip.Core.PublicApi;
using Spectre.Console.Cli;

namespace Lip.Cli.Commands;

public class MigrateCommand(ILipClient lipClient, IUserInteraction userInteraction) : AsyncCommand<MigrateCommand.Settings> {
  private readonly ILipClient _lipClient = lipClient;
  private readonly IUserInteraction _userInteraction = userInteraction;

  public class Settings : CommandSettings {
    [CommandArgument(0, "<FILE>")]
    [Description("The tooth.json to convert")]
    public required string File { get; init; }

    [CommandArgument(1, "<OUTPUT>")]
    [Description("The manifest.json to write")]
    public required string Output { get; init; }
  }

  public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken) {
    await _lipClient.Migrate(settings.File, settings.Output);
    await _userInteraction.PrintSuccess("Migration completed successfully.");
    return 0;
  }
}
