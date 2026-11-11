using Lip.Core.Infrastructure;
using Lip.Core.PublicApi;
using Spectre.Console.Cli;

namespace Lip.Cli.Commands;

public class PkgMigrateCommand(LipClient lipClient, IUserInteraction userInteraction) : AsyncCommand<PkgMigrateCommand.Settings> {
  private readonly LipClient _lipClient = lipClient;
  private readonly IUserInteraction _userInteraction = userInteraction;

  public class Settings : CommandSettings {
  }

  public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken) {
    await _lipClient.Migrate();
    await _userInteraction.PrintSuccess("Migrated tooth.json to lip.toml.");
    return 0;
  }
}
