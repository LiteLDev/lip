using Lip.Core.Infrastructure;
using Lip.Core.PublicApi;
using Spectre.Console.Cli;

namespace Lip.Cli.Commands;

public class CacheCleanCommand(LipClient lipClient, IUserInteraction userInteraction) : AsyncCommand<CacheCleanCommand.Settings> {
  private readonly LipClient _lipClient = lipClient;
  private readonly IUserInteraction _userInteraction = userInteraction;

  public class Settings : CommandSettings {
  }

  public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken) {
    await _lipClient.CacheClean();
    await _userInteraction.PrintSuccess("Cache cleaned successfully.");
    return 0;
  }
}
